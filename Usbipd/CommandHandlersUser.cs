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
            device = Data.BindStore.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid == guid.Value);
            if (device is null)
            {
                console.ReportError($"There is no shared device with guid '{guid.Value:D}'; run 'usbipd bind' as administrator first.");
                return ExitCode.Failure;
            }
        }
        else if (busId.HasValue)
        {
            device = Data.BindStore.GetBoundDevices().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId.Value);
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
            // A new user (or one without a secret yet) gets a TOTP secret; an existing secret is kept.
            byte[]? secret = null;
            if (user.OtpSecret is null && SecretDisplay.CheckCanShow(console))
            {
                secret = UserStore.SetNewOtpSecret(user);
                UserStore.Audit(db, "user otp reset", user.Name);
            }
            else if (user.OtpSecret is null)
            {
                console.ReportWarning($"User '{user.Name}' has no one-time password secret yet; run 'usbipd user otp reset --name {user.Name}' in an interactive console.");
            }
            _ = await db.SaveChangesAsync(cancellationToken);

            console.ReportInfo($"User '{user.Name}' mapped to '{device.Description}' ({device.InstanceId}).");
            if (secret is not null)
            {
                ReportOtpSecret(user.Name, secret, console);
            }
            return ExitCode.Success;
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// 'usbipd user otp reset': a new TOTP secret for an existing user; codes from the old secret stop working.
    /// </summary>
    async Task<ExitCode> ICommandHandlers.UserOtpReset(string userName, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        // Before anything changes: a secret that cannot be shown must not replace one that works.
        if (!SecretDisplay.CheckCanShow(console))
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
                console.ReportError($"There is no user '{userName}'; add the user first with 'usbipd user add'.");
                return ExitCode.Failure;
            }
            var secret = UserStore.SetNewOtpSecret(user);
            UserStore.Audit(db, "user otp reset", user.Name);
            _ = await db.SaveChangesAsync(cancellationToken);
            ReportOtpSecret(user.Name, secret, console);
            return ExitCode.Success;
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"User database: {ex.Message}");
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// Shows the secret once, for the user's authenticator app, on the alternate screen (see <see cref="SecretDisplay"/>),
    /// so it does not stay in the console. It cannot be shown again; a lost secret needs a reset.
    /// </summary>
    static void ReportOtpSecret(string userName, byte[] secret, IConsole console)
    {
        var issuer = Totp.GetIssuer(Data.Database.LoadConfiguration());
        var base32 = Totp.ToBase32(secret);
        var grouped = string.Join(' ', Enumerable.Range(0, (base32.Length + 3) / 4)
            .Select(i => base32.Substring(i * 4, Math.Min(4, base32.Length - (i * 4)))));
        var keyUri = Totp.GetKeyUri(issuer, userName, secret);
        var shown = SecretDisplay.Show(console, $"""
            One-time password secret for '{userName}'. Scan the QR code with the user's authenticator app now, or enter
            the secret by hand; it is not shown again. Anyone who sees this secret can generate the user's codes.

              Issuer:   {issuer}
              Account:  {userName}
              Secret:   {grouped}
              Type:     time-based (TOTP), SHA-1, {Totp.Digits} digits, {Totp.StepSeconds} seconds
            """, keyUri);
        if (shown)
        {
            console.ReportInfo($"The secret for '{userName}' was shown and removed from the screen.");
        }
        else
        {
            console.ReportWarning($"User '{userName}' has a new secret that was not shown; codes from an earlier secret no longer work.");
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
        var boundDevices = Data.BindStore.GetBoundDevices().ToList();
#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other list commands)
        console.Out.WriteLine("Users:");
        console.Out.WriteLine($"{"USER",-24}  {"OTP",-7}  {"BUSID",-5}  {"STATE",-22}  DEVICE");
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
            console.Out.Write($"{(user.HasOtpSecret ? "Set" : "Not set"),-7}  ");
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
    /// One-time move of what earlier versions kept in the registry (HKLM\SOFTWARE\usbipd-win): binds, policy rules and users.
    /// Binds keep their GUID. Imported entries are removed from the registry; entries that conflict with the database are kept
    /// (with a warning) and the database is not changed for them.
    /// </summary>
    async Task<ExitCode> ICommandHandlers.ImportRegistry(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        var registryDevices = UsbipdRegistry.Instance.GetBoundDevices().Where(d => d.PersistedGuid.HasValue).ToList();
        var registryRules = UsbipdRegistry.Instance.GetPolicyRules();
        var registryUsers = UsbipdRegistry.Instance.GetUserDevices();
        if (registryDevices.Count == 0 && registryRules.Count == 0 && registryUsers.Count == 0)
        {
            console.ReportInfo("There is nothing in the registry to import.");
            return ExitCode.Success;
        }

        var importedBinds = new List<Device>();
        var importedRules = new List<Guid>();
        var importedUsers = new List<string>();
        try
        {
            await using var db = UserStore.Open();
            var account = UserStore.CurrentAccount;
            foreach (var device in registryDevices)
            {
                var guid = device.PersistedGuid!.Value;
                var databaseDevice = await UserStore.GetOrAddDeviceAsync(db, device.InstanceId, device.Description, cancellationToken);
                if (databaseDevice.Id != 0 && await db.Binds.AnyAsync(b => b.DeviceId == databaseDevice.Id, cancellationToken))
                {
                    console.ReportWarning($"Device '{device.InstanceId}' is already shared in the database; the registry entry is kept.");
                    continue;
                }
                if (await db.Binds.AnyAsync(b => b.Id == guid, cancellationToken))
                {
                    console.ReportWarning($"Bind guid '{guid:D}' already exists in the database; the registry entry is kept.");
                    continue;
                }
                _ = db.Binds.Add(new()
                {
                    Id = guid,
                    Device = databaseDevice,
                    CreatedUtc = DateTime.UtcNow,
                    CreatedBy = account,
                });
                UserStore.Audit(db, "bind import", $"{databaseDevice.InstanceId} {guid:B}");
                importedBinds.Add(device);
            }
            // Saved before the users, which may refer to these devices.
            _ = await db.SaveChangesAsync(cancellationToken);

            var databaseRules = Data.BindStore.GetPolicyRules();
            foreach (var (guid, rule) in registryRules)
            {
                if (!databaseRules.ContainsValue(rule))
                {
                    _ = Data.BindStore.AddPolicyRule(rule);
                }
                importedRules.Add(guid);
            }

            foreach (var (userName, deviceGuid) in registryUsers)
            {
                if (await UserStore.FindUserAsync(db, userName, cancellationToken) is not null)
                {
                    console.ReportWarning($"User '{userName}' already exists in the database; the registry entry is kept.");
                    continue;
                }
                var device = registryDevices.SingleOrDefault(d => d.PersistedGuid == deviceGuid);
                if (device is null)
                {
                    console.ReportWarning($"User '{userName}' is mapped to guid '{deviceGuid:D}', which is not shared; the registry entry is kept.");
                    continue;
                }
                var databaseDevice = await UserStore.GetOrAddDeviceAsync(db, device.InstanceId, device.Description, cancellationToken);
                _ = await UserStore.SetMappingAsync(db, userName, databaseDevice, cancellationToken);
                UserStore.Audit(db, "user import", $"{userName} -> {databaseDevice.InstanceId}");
                importedUsers.Add(userName);
            }
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Database: {ex.Message}");
            console.ReportWarning("Nothing was removed from the registry; it is safe to run the import again.");
            return ExitCode.Failure;
        }

        foreach (var device in importedBinds)
        {
            UsbipdRegistry.Instance.StopSharingDevice(device.PersistedGuid!.Value);
            console.ReportInfo($"Imported bind '{device.PersistedGuid:D}' ({device.Description}).");
        }
        foreach (var guid in importedRules)
        {
            UsbipdRegistry.Instance.RemovePolicyRule(guid);
            console.ReportInfo($"Imported policy rule '{guid:D}'.");
        }
        foreach (var userName in importedUsers)
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
