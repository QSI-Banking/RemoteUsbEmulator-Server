// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Usbipd.Data;

/// <summary>
/// Connection to the usbipd database. The connection string is ConnectionStrings:Usbipd in appsettings.json
/// next to usbipd.exe (or the environment variable ConnectionStrings__Usbipd). Use Windows authentication:
/// the service connects as its own account, the CLI as the administrator running it, so no password is stored.
/// </summary>
static class Database
{
    public const string ConnectionStringName = "Usbipd";

    /// <summary>
    /// Used to generate migrations only (dotnet ef); the application never falls back to it.
    /// </summary>
    public const string DevelopmentConnectionString =
        @"Server=.\SQLEXPRESS;Database=usbipd;Integrated Security=True;TrustServerCertificate=True";

    /// <summary>
    /// The same configuration the service reads: appsettings.json next to usbipd.exe, then environment variables.
    /// </summary>
    public static IConfiguration LoadConfiguration()
    {
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <exception cref="InvalidOperationException">The connection string is not configured.</exception>
    public static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        return string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is required in appsettings.json next to usbipd.exe.")
            : connectionString;
    }

    public static UsbipdDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<UsbipdDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", UsbipdDbContext.Schema))
            .Options;
        return new UsbipdDbContext(options);
    }
}

/// <summary>
/// For 'dotnet ef migrations add' and 'dotnet ef migrations script'. Run those with the environment variable EfDesign=true,
/// which adds the EF Core design package to the build (it is left out of usbipd.exe otherwise).
/// The connection string is only used if a command actually connects ('dotnet ef database update').
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Created by the EF Core design-time tools")]
sealed class UsbipdDbContextDesignTimeFactory : IDesignTimeDbContextFactory<UsbipdDbContext>
{
    public UsbipdDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Usbipd");
        return Database.CreateContext(string.IsNullOrWhiteSpace(connectionString) ? Database.DevelopmentConnectionString : connectionString);
    }
}
