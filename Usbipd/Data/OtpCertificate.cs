// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography.X509Certificates;

namespace Usbipd.Data;

/// <summary>
/// The certificate that OTP secrets are encrypted to (see <see cref="Cryptography"/>): usbipd:Otp:CertificateThumbprint,
/// or else the TLS server certificate. It must have an RSA key; the service needs its private key.
/// <para>
/// Rotation: point the setting (or the TLS certificate) to a new certificate and keep the old one installed. The service
/// re-encrypts every secret to the new certificate when it starts;
/// 'usbipd user otp status' shows when no secret uses the old certificate anymore, and it can then be removed.
/// </para>
/// </summary>
static class OtpCertificate
{
    public const string ThumbprintKey = "usbipd:Otp:CertificateThumbprint";

    /// <exception cref="InvalidOperationException">No usable thumbprint is configured.</exception>
    public static string GetThumbprint(IConfiguration configuration)
    {
        var value = configuration[ThumbprintKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            return TlsSettings.NormalizeThumbprint(configuration[$"{TlsSettings.SectionName}:ServerCertificateThumbprint"], "ServerCertificateThumbprint");
        }
        try
        {
            return TlsSettings.NormalizeThumbprint(value, "unused");
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{ThumbprintKey} must be a 40 hex digit (SHA-1) certificate thumbprint.", ex);
        }
    }

    /// <summary>
    /// The current certificate, from LocalMachine\My (the public key is enough to encrypt).
    /// </summary>
    /// <exception cref="InvalidOperationException">Not configured, not found, or without an RSA key.</exception>
    public static X509Certificate2 Load(IConfiguration configuration)
    {
        var thumbprint = GetThumbprint(configuration);
        var certificate = Cryptography.FindInLocalMachineMy(thumbprint)
            ?? throw new InvalidOperationException($"The certificate for OTP secrets ({thumbprint}) is not in LocalMachine\\My.");
        using (var rsa = certificate.GetRSAPublicKey())
        {
            if (rsa is null)
            {
                certificate.Dispose();
                throw new InvalidOperationException($"The certificate for OTP secrets ({thumbprint}) does not have an RSA key.");
            }
        }
        return certificate;
    }
}
