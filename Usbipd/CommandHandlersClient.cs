// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using static Usbipd.ConsoleTools;
using UserStore = Usbipd.Data.UserStore;

namespace Usbipd;

/// <summary>
/// The client certificates that may connect, stored in SQL Server and shared by all servers (see <see cref="SqlClientAllowList"/>).
/// </summary>
sealed partial class CommandHandlers : ICommandHandlers
{
    async Task<ExitCode> ICommandHandlers.ClientAdd(string? thumbprint, string? certificateFile, string name, IConsole console,
        CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        string? subject = null;
        DateTime? notAfterUtc = null;
        if (certificateFile is not null)
        {
            try
            {
                using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificateFile);
                thumbprint = certificate.Thumbprint.ToUpperInvariant();
                subject = certificate.Subject;
                notAfterUtc = certificate.NotAfter.ToUniversalTime();
            }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            {
                console.ReportError($"Cannot read certificate '{certificateFile}': {ex.Message}");
                return ExitCode.Failure;
            }
        }
        if (thumbprint is null)
        {
            throw new UnexpectedResultException("Either thumbprint or certificate is required.");
        }

        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        try
        {
            await using var db = UserStore.Open();
            if (await db.ClientCertificates.AnyAsync(c => c.Thumbprint == thumbprint, cancellationToken))
            {
                console.ReportError($"Client certificate {thumbprint} is already listed; see 'usbipd client list'.");
                return ExitCode.Failure;
            }
            _ = db.ClientCertificates.Add(new()
            {
                Thumbprint = thumbprint,
                Name = name,
                Subject = subject,
                NotAfterUtc = notAfterUtc,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow,
                CreatedBy = UserStore.CurrentAccount,
            });
            UserStore.Audit(db, "client add", $"{thumbprint} {name}");
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Database: {ex.Message}");
            return ExitCode.Failure;
        }
        console.ReportInfo($"Client certificate {thumbprint} ('{name}') may connect to all servers.");
        return ExitCode.Success;
    }

    async Task<ExitCode> ICommandHandlers.ClientList(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        List<Data.ClientCertificate> certificates;
        try
        {
            await using var db = UserStore.Open();
            certificates = await db.ClientCertificates.AsNoTracking().OrderBy(c => c.Name).ThenBy(c => c.Thumbprint)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Database: {ex.Message}");
            return ExitCode.Failure;
        }

#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other list commands)
        console.Out.WriteLine("Client certificates:");
        console.Out.WriteLine($"{"THUMBPRINT",-40}  {"STATE",-8}  {"EXPIRES",-10}  NAME");
        foreach (var certificate in certificates)
        {
            var state = certificate.Enabled ? "Enabled" : "Disabled";
            var expires = certificate.NotAfterUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
            console.Out.WriteLine($"{certificate.Thumbprint,-40}  {state,-8}  {expires,-10}  {certificate.Name}");
        }
        console.Out.WriteLine();
#pragma warning restore CA1849 // Call async methods when in an async method
        return ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.ClientEnable(string thumbprint, IConsole console, CancellationToken cancellationToken)
    {
        return SetClientEnabled(thumbprint, true, console, cancellationToken);
    }

    Task<ExitCode> ICommandHandlers.ClientDisable(string thumbprint, IConsole console, CancellationToken cancellationToken)
    {
        return SetClientEnabled(thumbprint, false, console, cancellationToken);
    }

    static async Task<ExitCode> SetClientEnabled(string thumbprint, bool enabled, IConsole console, CancellationToken cancellationToken)
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
            var certificate = await db.ClientCertificates.SingleOrDefaultAsync(c => c.Thumbprint == thumbprint, cancellationToken);
            if (certificate is null)
            {
                console.ReportError($"Client certificate {thumbprint} is not listed; see 'usbipd client list'.");
                return ExitCode.Failure;
            }
            if (certificate.Enabled == enabled)
            {
                console.ReportInfo($"Client certificate {thumbprint} was already {(enabled ? "enabled" : "disabled")}.");
                return ExitCode.Success;
            }
            certificate.Enabled = enabled;
            UserStore.Audit(db, enabled ? "client enable" : "client disable", $"{thumbprint} {certificate.Name}");
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Database: {ex.Message}");
            return ExitCode.Failure;
        }
        console.ReportInfo(enabled
            ? $"Client certificate {thumbprint} may connect again."
            : $"Client certificate {thumbprint} is disabled; its sessions on all servers end within a few seconds.");
        return ExitCode.Success;
    }

    async Task<ExitCode> ICommandHandlers.ClientRemove(string thumbprint, IConsole console, CancellationToken cancellationToken)
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
            var certificate = await db.ClientCertificates.SingleOrDefaultAsync(c => c.Thumbprint == thumbprint, cancellationToken);
            if (certificate is null)
            {
                console.ReportError($"Client certificate {thumbprint} is not listed; see 'usbipd client list'.");
                return ExitCode.Failure;
            }
            _ = db.ClientCertificates.Remove(certificate);
            UserStore.Audit(db, "client remove", $"{thumbprint} {certificate.Name}");
            _ = await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Database: {ex.Message}");
            return ExitCode.Failure;
        }
        console.ReportInfo($"Client certificate {thumbprint} is removed; its sessions on all servers end within a few seconds.");
        return ExitCode.Success;
    }

    /// <summary>
    /// For command line completion; empty if the database cannot be reached.
    /// </summary>
    internal static List<string> TryGetClientThumbprints()
    {
        try
        {
            using var db = UserStore.Open();
            return [.. db.ClientCertificates.AsNoTracking().OrderBy(c => c.Thumbprint).Select(c => c.Thumbprint)];
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            return [];
        }
    }
}
