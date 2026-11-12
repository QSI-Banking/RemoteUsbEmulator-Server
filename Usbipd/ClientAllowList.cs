// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Usbipd.Data;

namespace Usbipd;

/// <summary>
/// The client certificates that may connect (see <see cref="SqlClientAllowList"/>).
/// </summary>
interface IClientAllowList
{
    /// <summary>
    /// Uses a recent copy of the list (see <see cref="SqlClientAllowList.CacheDuration"/>).
    /// </summary>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The database cannot be reached.</exception>
    bool IsAllowed(string thumbprint);

    /// <summary>
    /// Reloads the list now.
    /// </summary>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The database cannot be reached.</exception>
    void Refresh();
}

/// <summary>
/// The enabled certificates of the ClientCertificates table ('usbipd client add'), shared by all servers.
/// An empty list refuses every client.
/// </summary>
sealed class SqlClientAllowList(IConfiguration configuration) : IClientAllowList
{
    /// <summary>
    /// How long the handshake may use a loaded list. Sessions are checked against a fresh list (see <see cref="SessionMonitor"/>).
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    readonly IConfiguration Configuration = configuration;
    readonly Lock Lock = new();
    HashSet<string>? Allowed;
    long LoadedTimestamp;

    public bool IsAllowed(string thumbprint)
    {
        lock (Lock)
        {
            if (Allowed is not null && Stopwatch.GetElapsedTime(LoadedTimestamp) < CacheDuration)
            {
                return Allowed.Contains(thumbprint);
            }
        }
        return Load().Contains(thumbprint);
    }

    public void Refresh()
    {
        _ = Load();
    }

    HashSet<string> Load()
    {
        using var db = Database.CreateContext(Database.GetConnectionString(Configuration));
        var allowed = new HashSet<string>(
            db.ClientCertificates.AsNoTracking().Where(c => c.Enabled).Select(c => c.Thumbprint),
            StringComparer.OrdinalIgnoreCase);
        lock (Lock)
        {
            Allowed = allowed;
            LoadedTimestamp = Stopwatch.GetTimestamp();
        }
        return allowed;
    }
}

/// <summary>
/// Refuses every client; for checking the TLS configuration without a database ('usbipd installer configure').
/// </summary>
sealed class EmptyClientAllowList : IClientAllowList
{
    public static readonly EmptyClientAllowList Instance = new();

    public bool IsAllowed(string thumbprint) => false;

    public void Refresh()
    {
    }
}
