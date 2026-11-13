// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Usbipd.Data;

/// <summary>
/// 'usbipd db migrate': creates or upgrades the database schema and the database roles. Run it as an account that may change
/// the schema (or let a DBA run Database\usbipd.sql and Database\usbipd-roles.sql instead). The service never migrates the database itself.
/// </summary>
static class DatabaseCommands
{
    /// <summary>
    /// The database roles; the same as Database\usbipd-roles.sql. Membership is up to the DBA:
    /// usbipd_service for the account the service runs as (LocalSystem: NT AUTHORITY\SYSTEM on the server itself,
    /// DOMAIN\SERVER$ for a remote SQL Server), usbipd_admin for the administrators who run the CLI.
    /// </summary>
    const string RolesSql = """
        IF DATABASE_PRINCIPAL_ID(N'usbipd_service') IS NULL EXEC(N'CREATE ROLE [usbipd_service]');
        IF DATABASE_PRINCIPAL_ID(N'usbipd_admin') IS NULL EXEC(N'CREATE ROLE [usbipd_admin]');
        GRANT SELECT ON SCHEMA::[usbipd] TO [usbipd_service];
        GRANT INSERT, UPDATE, DELETE ON OBJECT::[usbipd].[Attachments] TO [usbipd_service];
        GRANT UPDATE ([LastAcceptedStep]) ON OBJECT::[usbipd].[OtpSecrets] TO [usbipd_service];
        GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[usbipd] TO [usbipd_admin];
        """;

    public static async Task<ExitCode> MigrateAsync(IConsole console, CancellationToken cancellationToken)
    {
        // Connects as the administrator who signs in, who needs permission to change the schema.
        if (!AdminAuthorization.Authorize(console))
        {
            return ExitCode.AccessDenied;
        }
        try
        {
            var connectionString = Database.GetConnectionString(Database.LoadConfiguration());
            await using var db = Database.CreateContext(connectionString);
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                console.ReportInfo("The database schema is up to date.");
            }
            else
            {
                await db.Database.MigrateAsync(cancellationToken);
                foreach (var migration in pending)
                {
                    console.ReportInfo($"Applied migration {migration}.");
                }
            }
            _ = await db.Database.ExecuteSqlRawAsync(RolesSql, cancellationToken);
            console.ReportInfo("The roles usbipd_service and usbipd_admin are up to date; add the service account and the administrators to them.");
            return ExitCode.Success;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqlException)
        {
            console.ReportError(ex.Message);
            return ExitCode.Failure;
        }
    }
}
