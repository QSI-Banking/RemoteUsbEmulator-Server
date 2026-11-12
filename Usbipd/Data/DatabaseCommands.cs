// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Usbipd.Data;

/// <summary>
/// 'usbipd db migrate': creates or upgrades the database schema. Run it as an account that may change the schema
/// (or let a DBA run Database\usbipd.sql instead). The service never migrates the database itself.
/// </summary>
static class DatabaseCommands
{
    public static async Task<ExitCode> MigrateAsync(IConsole console, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = Database.GetConnectionString(Database.LoadConfiguration());
            await using var db = Database.CreateContext(connectionString);

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count == 0)
            {
                console.ReportInfo("The database is up to date.");
                return ExitCode.Success;
            }

            await db.Database.MigrateAsync(cancellationToken);
            foreach (var migration in pending)
            {
                console.ReportInfo($"Applied migration {migration}.");
            }
            return ExitCode.Success;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqlException)
        {
            console.ReportError(ex.Message);
            return ExitCode.Failure;
        }
    }
}
