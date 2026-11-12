// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.EntityFrameworkCore;
using Usbipd.Data;

namespace Usbipd;

/// <summary>
/// The service's view of the user mappings in SQL Server (connection string from appsettings.json, as the service account).
/// A mapping names the device by instance ID; it resolves to the device's current bind, so it survives unbind and rebind.
/// </summary>
sealed class SqlUserDeviceMap(IConfiguration configuration) : IUserDeviceMap
{
    readonly IConfiguration Configuration = configuration;

    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The database cannot be reached.</exception>
    public Guid? GetDeviceGuid(string userName)
    {
        using var db = Database.CreateContext(Database.GetConnectionString(Configuration));
        var serverName = UserStore.ServerName;
        var instanceId = db.UserDevices.AsNoTracking()
            .Where(m => m.User!.Name == userName && m.Device!.ServerName == serverName)
            .Select(m => m.Device!.InstanceId)
            .SingleOrDefault();
        return instanceId is null
            ? null
            : UsbipdRegistry.Instance.GetBoundDevices()
                .FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))?.PersistedGuid;
    }
}
