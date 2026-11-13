// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Usbipd.Data;

/// <summary>
/// Users and their device mappings in SQL Server.
/// </summary>
static class UserStore
{
    /// <summary>
    /// Devices are identified by the server they are plugged into plus their instance ID.
    /// </summary>
    public static string ServerName => Environment.MachineName;

    /// <summary>
    /// The Windows account recorded as the author of a change.
    /// </summary>
    public static string CurrentAccount => AdminAuthorization.AccountName;

    /// <summary>
    /// A database connection for the CLI: the connection string from appsettings.json, as the current user.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static UsbipdDbContext Open()
    {
        return Database.CreateContext(Database.GetConnectionString(Database.LoadConfiguration()));
    }

    /// <summary>
    /// Exceptions that mean "the database is not configured or not reachable".
    /// </summary>
    public static bool IsDatabaseError(Exception ex)
    {
        return ex is InvalidOperationException or SqlException or DbUpdateException;
    }

    internal sealed record UserMapping(string UserName, string? ServerName, string? InstanceId, string? Description, bool HasOtpSecret);

    public static async Task<List<UserMapping>> ListAsync(UsbipdDbContext db, CancellationToken cancellationToken)
    {
        return await db.Users.AsNoTracking()
            .OrderBy(u => u.Name)
            .Select(u => new UserMapping(u.Name, u.UserDevice!.Device!.ServerName, u.UserDevice.Device.InstanceId,
                u.UserDevice.Device.Description, u.OtpSecret != null))
            .ToListAsync(cancellationToken);
    }

    public static async Task<User?> FindUserAsync(UsbipdDbContext db, string userName, CancellationToken cancellationToken)
    {
        // The column collation is case-insensitive, like UserNames.Comparer.
        return await db.Users.Include(u => u.UserDevice!).ThenInclude(m => m.Device).Include(u => u.OtpSecret)
            .SingleOrDefaultAsync(u => u.Name == userName, cancellationToken);
    }

    /// <summary>
    /// The device on this server with the given instance ID; created if it does not exist yet.
    /// </summary>
    public static async Task<Device> GetOrAddDeviceAsync(UsbipdDbContext db, string instanceId, string description,
        CancellationToken cancellationToken)
    {
        var serverName = ServerName;
        instanceId = instanceId.ToUpperInvariant();
        var device = await db.Devices.SingleOrDefaultAsync(d => d.ServerName == serverName && d.InstanceId == instanceId, cancellationToken);
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

    /// <summary>
    /// Maps the user (created if needed) to the device, replacing any existing mapping.
    /// </summary>
    public static async Task<User> SetMappingAsync(UsbipdDbContext db, string userName, Device device, CancellationToken cancellationToken)
    {
        var account = CurrentAccount;
        var now = DateTime.UtcNow;
        var user = await FindUserAsync(db, userName, cancellationToken);
        if (user is null)
        {
            user = new()
            {
                Name = userName,
                CreatedUtc = now,
                CreatedBy = account,
            };
            _ = db.Users.Add(user);
        }
        if (user.UserDevice is null)
        {
            user.UserDevice = new()
            {
                User = user,
                Device = device,
                CreatedUtc = now,
                CreatedBy = account,
            };
        }
        else
        {
            user.UserDevice.Device = device;
            user.UserDevice.CreatedUtc = now;
            user.UserDevice.CreatedBy = account;
        }
        return user;
    }

    /// <summary>
    /// Gives the user a new TOTP secret, replacing any existing one (codes from the old secret stop working).
    /// </summary>
    /// <returns>The secret, to show to the administrator once.</returns>
    public static byte[] SetNewOtpSecret(User user)
    {
        var secret = Totp.CreateSecret();
        var now = DateTime.UtcNow;
        var account = CurrentAccount;
        if (user.OtpSecret is null)
        {
            user.OtpSecret = new()
            {
                User = user,
                Secret = secret,
                LastAcceptedStep = 0,
                CreatedUtc = now,
                CreatedBy = account,
            };
        }
        else
        {
            user.OtpSecret.Secret = secret;
            user.OtpSecret.LastAcceptedStep = 0;
            user.OtpSecret.CreatedUtc = now;
            user.OtpSecret.CreatedBy = account;
        }
        return secret;
    }

    public static void Audit(UsbipdDbContext db, string action, string target)
    {
        _ = db.AdminAudit.Add(new()
        {
            TimestampUtc = DateTime.UtcNow,
            Account = CurrentAccount,
            ServerName = ServerName,
            Action = action,
            Target = target,
        });
    }

    /// <summary>
    /// Users mapped to the device with this instance ID on this server; empty if the database cannot be reached.
    /// For warnings only.
    /// </summary>
    public static List<string> TryGetUsersOfDevice(string? instanceId)
    {
        try
        {
            using var db = Open();
            var serverName = ServerName;
            var query = db.UserDevices.AsNoTracking().Where(m => m.Device!.ServerName == serverName);
            if (instanceId is not null)
            {
                var normalized = instanceId.ToUpperInvariant();
                query = query.Where(m => m.Device!.InstanceId == normalized);
            }
            return [.. query.Select(m => m.User!.Name).OrderBy(n => n)];
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            return [];
        }
    }

    /// <summary>
    /// For command line completion; empty if the database cannot be reached.
    /// </summary>
    public static List<string> TryGetUserNames()
    {
        try
        {
            using var db = Open();
            return [.. db.Users.AsNoTracking().OrderBy(u => u.Name).Select(u => u.Name)];
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            return [];
        }
    }
}
