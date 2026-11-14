// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Microsoft.EntityFrameworkCore;
using Usbipd.Automation;
using AutomationDevice = Usbipd.Automation.Device;

namespace Usbipd.Data;

/// <summary>
/// Binds (shared devices) and attachments of this server, in SQL Server.
/// <para>
/// All methods throw on database errors (see <see cref="UserStore.IsDatabaseError"/>); callers decide whether that is fatal.
/// </para>
/// </summary>
static class BindStore
{
    sealed record BindRow(Guid Guid, string InstanceId, string Description, string? BusId, string? ClientAddress, string? StubInstanceId);

    /// <summary>
    /// Enumerates the shared devices of this server, combined with their current state in Windows.
    /// </summary>
    public static List<AutomationDevice> GetBoundDevices()
    {
        var serverName = UserStore.ServerName;
        List<BindRow> rows;
        using (var db = UserStore.Open())
        {
            rows = [.. db.Binds.AsNoTracking()
                .Where(b => b.Device!.ServerName == serverName)
                .Select(b => new BindRow(b.Id, b.Device!.InstanceId, b.Device.Description,
                    b.Attachment == null ? null : b.Attachment.BusId,
                    b.Attachment == null ? null : b.Attachment.ClientAddress,
                    b.Attachment == null ? null : b.Attachment.StubInstanceId))];
        }

        // If the server is not running, ignore any left-over attachments as they are no longer valid.
        var ignoreAttached = !Server.IsRunning();
        var devices = new List<AutomationDevice>(rows.Count);
        foreach (var row in rows.OrderBy(r => r.Guid))
        {
            var instanceId = row.InstanceId.ToUpperInvariant();
            if (!WindowsDevice.TryCreate(instanceId, out var device))
            {
                // This device no longer exists (uninstalled), but it is still shared; it could be installed again.
                devices.Add(new(instanceId: instanceId, description: row.Description, isForced: false, busId: null,
                    persistedGuid: row.Guid, stubInstanceId: null, clientIPAddress: null));
                continue;
            }
            if (!ignoreAttached
                && BusId.TryParse(row.BusId ?? string.Empty, out var attachedBusId)
                && IPAddress.TryParse(row.ClientAddress ?? string.Empty, out var clientAddress)
                && row.StubInstanceId is not null)
            {
                devices.Add(new(instanceId: instanceId, description: row.Description, isForced: device.HasVBoxDriver, busId: attachedBusId,
                    persistedGuid: row.Guid, stubInstanceId: row.StubInstanceId.ToUpperInvariant(), clientIPAddress: clientAddress));
                continue;
            }
            // The device exists, but is not attached. Note that it may not be present.
            devices.Add(new(instanceId: instanceId, description: row.Description, isForced: device.HasVBoxDriver,
                busId: device.IsPresent ? device.BusId : null, persistedGuid: row.Guid, stubInstanceId: null, clientIPAddress: null));
        }
        return devices;
    }

    /// <summary>
    /// Shares the device ('usbipd bind'); a no-op if it is already shared.
    /// </summary>
    /// <returns>The bind GUID.</returns>
    public static Guid Persist(string instanceId, string description)
    {
        using var db = UserStore.Open();
        var device = GetOrAddDevice(db, instanceId, description);
        if (device.Id != 0 && db.Binds.SingleOrDefault(b => b.DeviceId == device.Id) is { } existing)
        {
            _ = db.SaveChanges();
            return existing.Id;
        }
        var bind = new Bind
        {
            Id = Guid.NewGuid(),
            Device = device,
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = UserStore.CurrentAccount,
        };
        _ = db.Binds.Add(bind);
        UserStore.Audit(db, "bind", $"{device.InstanceId} {bind.Id:B}");
        _ = db.SaveChanges();
        return bind.Id;
    }

    /// <summary>
    /// What was recorded for a bind, and whether it is confirmed for a run of the service.
    /// </summary>
    internal sealed record BindInfo(Guid Guid, string InstanceId, string Description, string? Fingerprint, string? LocationPath,
        string? HardwareId, string? SerialNumber, string? Interfaces, Guid? ConfirmedRunId);

    /// <summary>
    /// The binds of this server.
    /// </summary>
    public static List<BindInfo> GetBindInfos()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        return [.. db.Binds.AsNoTracking()
            .Where(b => b.Device!.ServerName == serverName)
            .Select(b => new BindInfo(b.Id, b.Device!.InstanceId, b.Device.Description, b.Fingerprint, b.LocationPath,
                b.HardwareId, b.SerialNumber, b.Interfaces, b.ConfirmedRunId))];
    }

    /// <summary>
    /// The run of the service on this server as last written by it, or <see langword="null"/>.
    /// Only current while the service is running (see <see cref="Server.IsRunning"/>).
    /// </summary>
    public static Guid? GetServiceRunId()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        return db.ServerRuns.AsNoTracking().Where(r => r.ServerName == serverName).Select(r => (Guid?)r.RunId).SingleOrDefault();
    }

    /// <summary>
    /// Called by the service at every start: binds confirmed in an earlier run are no longer valid.
    /// </summary>
    public static void StartServiceRun(Guid runId)
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        var run = db.ServerRuns.SingleOrDefault(r => r.ServerName == serverName);
        if (run is null)
        {
            run = new() { ServerName = serverName };
            _ = db.ServerRuns.Add(run);
        }
        run.RunId = runId;
        run.StartedUtc = DateTime.UtcNow;
        _ = db.SaveChanges();
    }

    /// <summary>
    /// An administrator confirms that the device now present is the one to share: records its identity and the run of the service.
    /// </summary>
    /// <param name="runId"><see langword="null"/> while the service is not running; the bind then needs a review after it starts.</param>
    /// <returns><see langword="false"/> if the bind no longer exists (the service dropped it because the device was removed).</returns>
    public static bool Confirm(Guid guid, DeviceIdentity identity, Guid? runId, bool review)
    {
        var serverName = UserStore.ServerName;
        var account = UserStore.CurrentAccount;
        var now = DateTime.UtcNow;
        using var db = UserStore.Open();
        var updated = db.Binds.Where(b => b.Id == guid && b.Device!.ServerName == serverName)
            .ExecuteUpdate(s => s
                .SetProperty(b => b.Fingerprint, identity.Fingerprint)
                .SetProperty(b => b.LocationPath, identity.LocationPath)
                .SetProperty(b => b.HardwareId, identity.HardwareId)
                .SetProperty(b => b.SerialNumber, identity.SerialNumber)
                .SetProperty(b => b.Interfaces, identity.Interfaces)
                .SetProperty(b => b.ConfirmedRunId, runId)
                .SetProperty(b => b.ConfirmedUtc, now)
                .SetProperty(b => b.ConfirmedBy, account));
        if (updated == 0)
        {
            return false;
        }
        UserStore.Audit(db, review ? "bind review" : "bind confirm", $"{guid:B} {identity.HardwareId} {identity.LocationPath} {identity.Fingerprint}");
        _ = db.SaveChanges();
        return true;
    }

    /// <summary>
    /// The form of an instance ID inside a device interface path (which is what a removal event names):
    /// upper case, with '#' for '\'.
    /// </summary>
    public static string GetInterfaceKey(string instanceId)
    {
        return instanceId.ToUpperInvariant().Replace('\\', '#');
    }

    /// <summary>
    /// Called by the service when a bound device was removed (unplugged, moved, disabled or its driver changed):
    /// drops the binds of the device on this server, with their attachments.
    /// </summary>
    /// <param name="interfaceKey">See <see cref="GetInterfaceKey"/>.</param>
    /// <returns>The dropped binds.</returns>
    public static List<(Guid Guid, string InstanceId)> DropBindsOfRemovedDevice(string interfaceKey)
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        var binds = db.Binds.Include(b => b.Device).Where(b => b.Device!.ServerName == serverName).ToList()
            .Where(b => GetInterfaceKey(b.Device!.InstanceId) == interfaceKey).ToList();
        if (binds.Count == 0)
        {
            return [];
        }
        db.Binds.RemoveRange(binds);
        foreach (var bind in binds)
        {
            UserStore.Audit(db, "bind dropped", $"{bind.Device!.InstanceId} {bind.Id:B} (device removed)");
        }
        _ = db.SaveChanges();
        return [.. binds.Select(b => (b.Id, b.Device!.InstanceId))];
    }

    /// <summary>
    /// 'usbipd unbind'. Also ends its attachment (the service then detaches the device).
    /// </summary>
    public static void StopSharingDevice(Guid guid)
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        var bind = db.Binds.Include(b => b.Device).SingleOrDefault(b => b.Id == guid && b.Device!.ServerName == serverName);
        if (bind is null)
        {
            return;
        }
        _ = db.Binds.Remove(bind);
        UserStore.Audit(db, "unbind", $"{bind.Device!.InstanceId} {bind.Id:B}");
        _ = db.SaveChanges();
    }

    /// <summary>
    /// 'usbipd unbind --all' for this server.
    /// </summary>
    public static void StopSharingAllDevices()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        var count = db.Binds.Where(b => b.Device!.ServerName == serverName).ExecuteDelete();
        UserStore.Audit(db, "unbind --all", $"{count} device(s)");
        _ = db.SaveChanges();
    }

    /// <summary>
    /// Records that the device is attached (by the service). Replaces any earlier record of the device.
    /// </summary>
    public static void SetDeviceAsAttached(Guid guid, BusId busId, IPAddress address, string stubInstanceId, string userName)
    {
        using var db = UserStore.Open();
        _ = db.Attachments.Where(a => a.BindId == guid).ExecuteDelete();
        _ = db.Attachments.Add(new()
        {
            BindId = guid,
            BusId = busId.ToString(),
            ClientAddress = address.ToString(),
            StubInstanceId = stubInstanceId,
            UserName = userName,
            StartedUtc = DateTime.UtcNow,
        });
        _ = db.SaveChanges();
    }

    /// <summary>
    /// Deletes the attachment record. Done by the service after a detach, and by 'usbipd detach' to request one
    /// (the service checks every few seconds, see <see cref="SessionMonitor"/>).
    /// </summary>
    /// <returns><see langword="false"/> if the device was not attached.</returns>
    public static bool SetDeviceAsDetached(Guid guid)
    {
        using var db = UserStore.Open();
        return db.Attachments.Where(a => a.BindId == guid).ExecuteDelete() > 0;
    }

    /// <summary>
    /// Deletes all attachment records of this server.
    /// </summary>
    public static void SetAllDevicesAsDetached()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        _ = db.Attachments.Where(a => a.Bind!.Device!.ServerName == serverName).ExecuteDelete();
    }

    /// <summary>
    /// The bind GUIDs of the devices of this server that have an attachment record.
    /// </summary>
    public static HashSet<Guid> GetAttachedGuids()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        return [.. db.Attachments.AsNoTracking().Where(a => a.Bind!.Device!.ServerName == serverName).Select(a => a.BindId)];
    }

    static Device GetOrAddDevice(UsbipdDbContext db, string instanceId, string description)
    {
        var serverName = UserStore.ServerName;
        instanceId = instanceId.ToUpperInvariant();
        var device = db.Devices.SingleOrDefault(d => d.ServerName == serverName && d.InstanceId == instanceId);
        if (device is null)
        {
            device = new()
            {
                ServerName = serverName,
                InstanceId = instanceId,
                Description = description,
                CreatedUtc = DateTime.UtcNow,
            };
            _ = db.Devices.Add(device);
        }
        else
        {
            device.Description = description;
        }
        return device;
    }
}
