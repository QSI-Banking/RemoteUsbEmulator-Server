// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Usbipd.Data;

namespace Usbipd;

/// <summary>
/// Certificate rotation for OTP secrets: re-encrypts every secret that is not yet encrypted to the current certificate
/// (see <see cref="OtpCertificate"/>), once at every service start. Needs the private key of the old certificate;
/// a secret whose certificate is gone cannot be recovered and the user needs 'qsiusbd user otp reset'.
/// </summary>
sealed class OtpSecretRotation(IConfiguration configuration, ILogger<OtpSecretRotation> logger) : BackgroundService
{
    readonly IConfiguration Configuration = configuration;
    readonly ILogger Logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not hold up the start of the service.
        await Task.Yield();
        RotateAll();
    }

    internal void RotateAll()
    {
        try
        {
            var current = OtpCertificate.GetThumbprint(Configuration);
            // Without the current certificate and its private key, no one can sign in: say so in the log.
            using (var certificate = OtpCertificate.Load(Configuration))
            using (var rsa = certificate.GetRSAPrivateKey())
            {
                if (rsa is null)
                {
                    throw new InvalidOperationException($"The service cannot use the private key of the certificate for OTP secrets ({current}).");
                }
            }
            using var db = Database.CreateContext(Database.GetConnectionString(Configuration));
            var pending = db.OtpSecrets.AsNoTracking()
                .Where(o => o.ProtectedSecret != "" && o.CertificateThumbprint != current)
                .Select(o => new { o.UserId, o.ProtectedSecret, o.CertificateThumbprint })
                .ToList();
            foreach (var entry in pending)
            {
                byte[] secret;
                try
                {
                    secret = Cryptography.Decrypt(entry.UserId, entry.ProtectedSecret);
                }
                catch (CryptographicException ex)
                {
                    Logger.OtpSecretNotRotated(entry.UserId, entry.CertificateThumbprint, ex.Message);
                    continue;
                }
                try
                {
                    ReEncrypt(db, entry.UserId, entry.CertificateThumbprint, entry.ProtectedSecret, secret);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secret);
                }
            }
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex) || ex is CryptographicException)
        {
            Logger.OtpRotationFailed(ex.Message);
        }
    }

    /// <summary>
    /// Re-encrypts one secret to the current certificate. Only replaces the payload that was read, so a reset that
    /// happened in between is never overwritten. Failures are logged; the old payload stays valid.
    /// </summary>
    void ReEncrypt(UsbipdDbContext db, int userId, string thumbprint, string protectedSecret, byte[] secret)
    {
        try
        {
            using var certificate = OtpCertificate.Load(Configuration);
            var newProtectedSecret = Cryptography.Encrypt(userId, secret, certificate);
            var newThumbprint = certificate.Thumbprint;
            if (db.OtpSecrets
                .Where(o => o.UserId == userId && o.ProtectedSecret == protectedSecret)
                .ExecuteUpdate(s => s
                    .SetProperty(o => o.ProtectedSecret, newProtectedSecret)
                    .SetProperty(o => o.CertificateThumbprint, newThumbprint)) == 1)
            {
                Logger.OtpSecretRotated(userId, thumbprint, newThumbprint);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException
            or Microsoft.Data.SqlClient.SqlException or DbUpdateException)
        {
            Logger.OtpSecretNotRotated(userId, thumbprint, ex.Message);
        }
    }
}
