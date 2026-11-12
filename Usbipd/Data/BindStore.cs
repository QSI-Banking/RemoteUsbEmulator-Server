// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Microsoft.EntityFrameworkCore;
using Usbipd.Automation;
using AutomationDevice = Usbipd.Automation.Device;

namespace Usbipd.Data;

/// <summary>
/// Binds (shared devices), attachments and policy rules of this server, in SQL Server.
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

    public static Guid AddPolicyRule(PolicyRule rule)
    {
        if (!rule.IsValid())
        {
            throw new ArgumentException("Invalid policy rule", nameof(rule));
        }
        if (GetPolicyRules().ContainsValue(rule))
        {
            throw new ArgumentException("Duplicate policy rule", nameof(rule));
        }
        var entry = new PolicyRuleEntry
        {
            Id = Guid.NewGuid(),
            ServerName = UserStore.ServerName,
            Effect = rule.Effect.ToString(),
            Operation = rule.Operation.ToString(),
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = UserStore.CurrentAccount,
        };
        switch (rule)
        {
            case PolicyRuleAutoBind autoBind:
                entry.BusId = autoBind.BusId?.ToString();
                entry.HardwareId = autoBind.HardwareId?.ToString();
                break;
            default:
                throw new ArgumentException("Unsupported policy rule", nameof(rule));
        }
        using var db = UserStore.Open();
        _ = db.PolicyRules.Add(entry);
        UserStore.Audit(db, "policy add", $"{entry.Id:B} {rule}");
        _ = db.SaveChanges();
        return entry.Id;
    }

    public static void RemovePolicyRule(Guid guid)
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        if (db.PolicyRules.Where(p => p.Id == guid && p.ServerName == serverName).ExecuteDelete() > 0)
        {
            UserStore.Audit(db, "policy remove", $"{guid:B}");
            _ = db.SaveChanges();
        }
    }

    public static void RemovePolicyRuleAll()
    {
        var serverName = UserStore.ServerName;
        using var db = UserStore.Open();
        var count = db.PolicyRules.Where(p => p.ServerName == serverName).ExecuteDelete();
        UserStore.Audit(db, "policy remove --all", $"{count} rule(s)");
        _ = db.SaveChanges();
    }

    /// <summary>
    /// The policy rules of this server; invalid rules and duplicates are ignored.
    /// </summary>
    public static SortedDictionary<Guid, PolicyRule> GetPolicyRules()
    {
        var serverName = UserStore.ServerName;
        List<PolicyRuleEntry> entries;
        using (var db = UserStore.Open())
        {
            entries = [.. db.PolicyRules.AsNoTracking().Where(p => p.ServerName == serverName)];
        }
        var rules = new SortedDictionary<Guid, PolicyRule>();
        foreach (var entry in entries.OrderBy(e => e.Id))
        {
            if (!Enum.TryParse<PolicyRuleEffect>(entry.Effect, true, out var effect)
                || !Enum.TryParse<PolicyRuleOperation>(entry.Operation, true, out var operation))
            {
                continue;
            }
            PolicyRule rule;
            switch (operation)
            {
                case PolicyRuleOperation.AutoBind:
                    BusId? busId = BusId.TryParse(entry.BusId ?? string.Empty, out var parsedBusId) ? parsedBusId : null;
                    VidPid? hardwareId = VidPid.TryParse(entry.HardwareId ?? string.Empty, out var parsedHardwareId) ? parsedHardwareId : null;
                    rule = new PolicyRuleAutoBind(effect, busId, hardwareId);
                    break;
                default:
                    continue;
            }
            if (!rule.IsValid() || rules.ContainsValue(rule))
            {
                continue;
            }
            rules.Add(entry.Id, rule);
        }
        return rules;
    }
}
