// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.EntityFrameworkCore;
using Usbipd.Automation;
using static Usbipd.ConsoleTools;
using UserStore = Usbipd.Data.UserStore;

namespace Usbipd;

/// <summary>
/// Users and their device mappings, stored in SQL Server (see <see cref="UserStore"/>).
/// The CLI connects as the administrator running it.
/// </summary>
sealed partial class CommandHandlers : ICommandHandlers
{
    async Task<ExitCode> ICommandHandlers.UserAdd(string userName, Guid? guid, BusId? busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        Device? device;
        if (guid.HasValue)
        {
            device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid == guid.Value);
            if (device is null)
            {
                console.ReportError($"There is no shared device with guid '{guid.Value:D}'; run 'usbipd bind' as administrator first.");
                return ExitCode.Failure;
            }
        }
        else if (busId.HasValue)
        {
            device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId.Value);
            if (device is null)
            {
                console.ReportError($"There is no shared device with busid '{busId.Value}'; run 'usbipd bind --busid {busId.Value}' as administrator first.");
                return ExitCode.Failure;
            }
        }
        else
        {
            throw new UnexpectedResultException("Either guid or busid is required.");
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        try
        {
            await using var db = UserStore.Open();
            var existing = await UserStore.FindUserAsync(db, userName, cancellationToken);
            if (existing?.UserDevice?.Device is { } current && !force)
            {
                console.ReportError($"User '{userName}' is already mapped to '{current.Description}' ({current.InstanceId}) on {current.ServerName}; use '--force' to replace the mapping.");
                return ExitCode.Failure;
            }

            var databaseDevice = await UserStore.GetOrAddDeviceAsync(db, device.InstanceId, device.Description, cancellationToken);
            var user = await UserStore.SetMappingAsync(db, userName, databaseDevice, cancellationToken);
            UserStore.Audit(db, existing is null ? "user add" : "user remap", $"{user.Name} -> {databaseDevice.InstanceId}");
            _ = await db.SaveChangesAsync(cancellationToken);

            console.ReportInfo($"User '{user.Name}' mapped to '{device.Description}' ({device.InstanceId}).");
            return ExitCode.Success;
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }
    }

    async Task<ExitCode> ICommandHandlers.UserList(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        List<UserStore.UserMapping> users;
        try
        {
            await using var db = UserStore.Open();
            users = await UserStore.ListAsync(db, cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }

        var serverName = UserStore.ServerName;
        var boundDevices = UsbipdRegistry.Instance.GetBoundDevices().ToList();
#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other list commands)
        console.Out.WriteLine("Users:");
        console.Out.WriteLine($"{"USER",-24}  {"BUSID",-5}  {"STATE",-22}  DEVICE");
        foreach (var user in users)
        {
            string state;
            string busIdText = "-";
            if (user.InstanceId is null)
            {
                state = "Not mapped";
            }
            else if (!string.Equals(user.ServerName, serverName, StringComparison.OrdinalIgnoreCase))
            {
                state = $"On {user.ServerName}";
            }
            else
            {
                var device = boundDevices.SingleOrDefault(d => string.Equals(d.InstanceId, user.InstanceId, StringComparison.OrdinalIgnoreCase));
                state = device is null ? "Not shared"
                    : !device.BusId.HasValue ? "Not present"
                    : device.ClientIPAddress is not null ? $"Attached ({device.ClientIPAddress})"
                    : "Shared";
                if (device is not null && device.BusId.HasValue)
                {
                    busIdText = device.BusId.Value.ToString();
                }
            }
            console.Out.Write($"{user.UserName,-24}  ");
            console.Out.Write($"{busIdText,-5}  ");
            console.Out.Write($"{state,-22}  ");
            console.Out.WriteLine(user.Description ?? "-");
        }
        console.Out.WriteLine();
#pragma warning restore CA1849 // Call async methods when in an async method
        return ExitCode.Success;
    }

    async Task<ExitCode> ICommandHandlers.UserRemove(string userName, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        try
        {
            await using var db = UserStore.Open();
            var user = await UserStore.FindUserAsync(db, userName, cancellationToken);
            if (user is null)
            {
                console.ReportError($"There is no user '{userName}'.");
                return ExitCode.Failure;
            }
            _ = db.Users.Remove(user); // the mapping is deleted with it
            UserStore.Audit(db, "user remove", user.Name);
            _ = await db.SaveChangesAsync(cancellationToken);
            return ExitCode.Success;
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// Removes the users mapped to devices on this server (the database may be shared by other servers).
    /// </summary>
    async Task<ExitCode> ICommandHandlers.UserRemoveAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        try
        {
            await using var db = UserStore.Open();
            var serverName = UserStore.ServerName;
            var users = await db.Users.Where(u => u.UserDevice!.Device!.ServerName == serverName).ToListAsync(cancellationToken);
            db.Users.RemoveRange(users);
            foreach (var user in users)
            {
                UserStore.Audit(db, "user remove", user.Name);
            }
            _ = await db.SaveChangesAsync(cancellationToken);
            console.ReportInfo($"Removed {users.Count} user(s) mapped to devices on {serverName}.");
            return ExitCode.Success;
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// One-time move of the mappings that older versions kept in the registry (HKLM\SOFTWARE\usbipd-win\Users)
    /// into the database. Imported entries are removed from the registry; existing database users are not changed.
    /// </summary>
    async Task<ExitCode> ICommandHandlers.UserImport(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        var registryUsers = UsbipdRegistry.Instance.GetUserDevices();
        if (registryUsers.Count == 0)
        {
            console.ReportInfo("There are no users in the registry to import.");
            return ExitCode.Success;
        }

        var boundDevices = UsbipdRegistry.Instance.GetBoundDevices().ToList();
        var imported = new List<string>();
        try
        {
            await using var db = UserStore.Open();
            foreach (var (userName, deviceGuid) in registryUsers)
            {
                if (await UserStore.FindUserAsync(db, userName, cancellationToken) is not null)
                {
                    console.ReportWarning($"User '{userName}' already exists in the database; the registry entry is kept.");
                    continue;
                }
                var device = boundDevices.SingleOrDefault(d => d.PersistedGuid == deviceGuid);
                if (device is null)
                {
                    console.ReportWarning($"User '{userName}' is mapped to guid '{deviceGuid:D}', which is not shared; the registry entry is kept.");
                    continue;
                }
                var databaseDevice = await UserStore.GetOrAddDeviceAsync(db, device.InstanceId, device.Description, cancellationToken);
                _ = await UserStore.SetMappingAsync(db, userName, databaseDevice, cancellationToken);
                UserStore.Audit(db, "user import", $"{userName} -> {databaseDevice.InstanceId}");
                imported.Add(userName);
            }
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }

        foreach (var userName in imported)
        {
            _ = UsbipdRegistry.Instance.RemoveUser(userName);
            console.ReportInfo($"Imported user '{userName}'.");
        }
        return ExitCode.Success;
    }

    /// <summary>
    /// Mappings are kept on unbind (so unbind/rebind does not lose users), but the user should know.
    /// </summary>
    static void ReportUsersOfUnboundDevice(string instanceId, IConsole console)
    {
        foreach (var userName in UserStore.TryGetUsersOfDevice(instanceId))
        {
            console.ReportWarning($"User '{userName}' is still mapped to this device; attach fails until it is shared again, or run 'usbipd user remove --name {userName}'.");
        }
    }
}
