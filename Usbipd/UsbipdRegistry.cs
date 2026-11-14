// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Security;
using Microsoft.Win32;
using Usbipd.Automation;

namespace Usbipd;

/// <summary>
/// The registry state of earlier versions (HKLM\SOFTWARE\usbipd-win). Binds, attachments and users are now in
/// SQL Server (see <see cref="Data.BindStore"/> and <see cref="Data.UserStore"/>); this is only read by 'usbipd db import-registry'.
/// The installer still records the installation folder here.
/// </summary>
sealed class UsbipdRegistry : IDisposable
{
    public static UsbipdRegistry Instance { get => TestInstance ?? field; } = new(Registry.LocalMachine);

#pragma warning disable CS0649 // Field is never assigned to. Used only by UnitTests.
    internal static UsbipdRegistry? TestInstance;
#pragma warning restore CS0649

    readonly RegistryKey? ReadOnlyBaseKey;
    readonly RegistryKey? WritableBaseKey;

    public void Dispose()
    {
        ReadOnlyBaseKey?.Dispose();
        WritableBaseKey?.Dispose();
    }

    const string RegistryPath = @"SOFTWARE\usbipd-win";

    public UsbipdRegistry(RegistryKey parentKey)
    {
        try
        {
            ReadOnlyBaseKey = parentKey.OpenSubKey(RegistryPath, false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { }
        try
        {
            WritableBaseKey = parentKey.OpenSubKey(RegistryPath, true);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { }
    }

    RegistryKey BaseKey(bool writable)
    {
        return ReadOnlyBaseKey is null
            ? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.")
            : writable ? WritableBaseKey ?? throw new SecurityException("No write access to registry key.") : ReadOnlyBaseKey;
    }

    const string ApplicationFolderName = "APPLICATIONFOLDER";
    const string DevicesName = "Devices";
    const string InstanceIdName = "InstanceId";
    const string DescriptionName = "Description";
    const string AttachedName = "Attached";
    const string BusIdName = "BusId";
    const string IPAddressName = "IPAddress";
    const string UsersName = "Users";
    const string DeviceGuidName = "DeviceGuid";

    /// <summary>
    /// <see langword="null"/> if not installed
    /// </summary>
    public string? InstallationFolder => ReadOnlyBaseKey?.GetValue(ApplicationFolderName) as string;

    RegistryKey GetDevicesKey(bool writable)
    {
        return BaseKey(writable).OpenSubKey(DevicesName, writable)
            ?? throw new UnexpectedResultException("Registry key not found; try reinstalling the software.");
    }

    RegistryKey? GetDeviceKey(Guid guid, bool writable)
    {
        using var devicesKey = GetDevicesKey(writable);
        return devicesKey.OpenSubKey(guid.ToString("B"), writable);
    }

    /// <summary>
    /// Policy rules are no longer supported; only counted so that 'usbipd db import-registry' can say they are skipped.
    /// </summary>
    public int CountPolicyRules()
    {
        using var policyKey = ReadOnlyBaseKey?.OpenSubKey("Policy", false);
        return policyKey?.SubKeyCount ?? 0;
    }

    public void Persist(string instanceId, string description)
    {
        var guid = Guid.NewGuid();
        using var deviceKey = GetDevicesKey(true).CreateSubKey($"{guid:B}");
        deviceKey.SetValue(InstanceIdName, instanceId);
        deviceKey.SetValue(DescriptionName, description);
    }

    public void StopSharingDevice(Guid guid)
    {
        using var devicesKey = GetDevicesKey(true);
        devicesKey.DeleteSubKeyTree(guid.ToString("B"), false);
    }

    public void StopSharingAllDevices()
    {
        using var devicesKey = GetDevicesKey(true);
        foreach (var subKeyName in devicesKey.GetSubKeyNames())
        {
            devicesKey.DeleteSubKeyTree(subKeyName, false);
        }
    }

    /// <summary>
    /// Enumerates all bound devices.
    /// <para>
    /// This retrieves the entire (valid) registry state.
    /// </para>
    /// </summary>
    public IEnumerable<Device> GetBoundDevices()
    {
        var guids = new SortedSet<Guid>();
        using var devicesKey = GetDevicesKey(false);
        foreach (var subKeyName in devicesKey.GetSubKeyNames())
        {
            if (Guid.TryParseExact(subKeyName, "B", out var guid))
            {
                // Sanitize uniqueness.
                _ = guids.Add(guid);
            }
        }
        var ignoreAttached = !Server.IsRunning();
        var persistedDevices = new Dictionary<string, Device>();
        foreach (var guid in guids)
        {
            using var deviceKey = GetDeviceKey(guid, false);
            if (deviceKey is null)
            {
                continue;
            }
            if (deviceKey.GetValue(InstanceIdName) is not string instanceId)
            {
                // Must exist.
                continue;
            }
            // In the past, instance IDs where not normalized to upper case. Entries may still exist that have lower case letters.
            instanceId = instanceId.ToUpperInvariant();
            if (persistedDevices.ContainsKey(instanceId))
            {
                // Sanitize uniqueness.
                continue;
            }
            if (deviceKey.GetValue(DescriptionName) is not string description)
            {
                // Must exist.
                continue;
            }
            if (WindowsDevice.TryCreate(instanceId, out var device))
            {
                // The device exists. Note that it may not be present (as the original device), it could be attached via a stub device.
                // If the server is not running, ignore any left-over attaches as they are no longer valid.
                if (!ignoreAttached)
                {
                    using var attachedKey = deviceKey.OpenSubKey(AttachedName, false);
                    if (attachedKey is not null)
                    {
                        if (BusId.TryParse(attachedKey.GetValue(BusIdName) as string ?? "", out var busId)
                            && IPAddress.TryParse(attachedKey.GetValue(IPAddressName) as string ?? "", out var ipAddress)
                            && attachedKey.GetValue(InstanceIdName) is string stubInstanceId)
                        {
                            // In the past, instance IDs where not normalized to upper case. Entries may still exist that have lower case letters.
                            stubInstanceId = stubInstanceId.ToUpperInvariant();

                            // Everything checks out, report the device as attached.
                            persistedDevices.Add(instanceId, new(
                                instanceId: instanceId,
                                description: description,
                                isForced: device.HasVBoxDriver,
                                busId: busId,
                                persistedGuid: guid,
                                stubInstanceId: stubInstanceId,
                                clientIPAddress: ipAddress
                                ));
                            continue;
                        }
                    }
                }
                // This device is not attached.
                persistedDevices.Add(instanceId, new(
                    instanceId: instanceId,
                    description: description,
                    isForced: device.HasVBoxDriver,
                    busId: device.IsPresent ? device.BusId : null,
                    persistedGuid: guid,
                    stubInstanceId: null,
                    clientIPAddress: null
                    ));
            }
            else
            {
                // This device no longer exists (uninstalled), but we still have it persisted, it could be installed again.
                persistedDevices.Add(instanceId, new(
                    instanceId: instanceId,
                    description: description,
                    isForced: false,
                    busId: null,
                    persistedGuid: guid,
                    stubInstanceId: null,
                    clientIPAddress: null
                    ));
            }
        }
        return persistedDevices.Values;
    }

    public bool HasWriteAccess => WritableBaseKey is not null;

    /// <summary>
    /// Maps a user (see <see cref="UserNames"/>) to the bind GUID of the device that user may attach.
    /// Stored as HKLM\SOFTWARE\usbipd-win\Users\&lt;name&gt;\DeviceGuid. Replaces any existing mapping.
    /// </summary>
    public void SetUserDevice(string userName, Guid deviceGuid)
    {
        if (!UserNames.IsValid(userName))
        {
            throw new ArgumentException("Invalid user name", nameof(userName));
        }
        using var usersKey = BaseKey(true).CreateSubKey(UsersName, true);
        using var userKey = usersKey.CreateSubKey(userName, true);
        userKey.SetValue(DeviceGuidName, deviceGuid.ToString("B"));
    }

    /// <returns><see langword="false"/> if the user did not exist.</returns>
    public bool RemoveUser(string userName)
    {
        if (!UserNames.IsValid(userName))
        {
            return false;
        }
        using var usersKey = BaseKey(true).OpenSubKey(UsersName, true);
        if (usersKey is null)
        {
            return false;
        }
        using (var userKey = usersKey.OpenSubKey(userName, false))
        {
            if (userKey is null)
            {
                return false;
            }
        }
        usersKey.DeleteSubKeyTree(userName, false);
        return true;
    }

    public void RemoveAllUsers()
    {
        using var usersKey = BaseKey(true).OpenSubKey(UsersName, true);
        if (usersKey is null)
        {
            return;
        }
        foreach (var subKeyName in usersKey.GetSubKeyNames())
        {
            usersKey.DeleteSubKeyTree(subKeyName, false);
        }
    }

    /// <returns>The bind GUID mapped to the user, or <see langword="null"/>.</returns>
    public Guid? GetUserDevice(string userName)
    {
        if (!UserNames.IsValid(userName))
        {
            return null;
        }
        using var usersKey = BaseKey(false).OpenSubKey(UsersName, false);
        using var userKey = usersKey?.OpenSubKey(userName, false);
        return Guid.TryParseExact(userKey?.GetValue(DeviceGuidName) as string, "B", out var guid) ? guid : null;
    }

    /// <summary>
    /// Enumerates all valid user mappings; invalid entries are ignored.
    /// </summary>
    public SortedDictionary<string, Guid> GetUserDevices()
    {
        var users = new SortedDictionary<string, Guid>(UserNames.Comparer);
        using var usersKey = BaseKey(false).OpenSubKey(UsersName, false);
        if (usersKey is null)
        {
            return users;
        }
        foreach (var userName in usersKey.GetSubKeyNames())
        {
            if (GetUserDevice(userName) is Guid guid)
            {
                users[userName] = guid;
            }
        }
        return users;
    }
}
