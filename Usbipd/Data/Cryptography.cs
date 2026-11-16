// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ConfigEncrypt.Services;

namespace Usbipd.Data;

/// <summary>
/// Encrypts OTP secrets with the in-house ConfigEncrypt library (<see cref="ConfigEncryption"/>, RSA certificate format only).
/// <para>
/// The stored value is exactly what the library produces ("enc:" + Base64). The only addition is what gets encrypted:
/// "&lt;user id&gt;:&lt;secret in hex&gt;". The user id is checked after decryption, so a value copied to another user's row
/// is refused.
/// </para>
/// <para>
/// Anyone may encrypt (only the public key is used); only an account with the private key (the service) can decrypt.
/// </para>
/// </summary>
static class Cryptography
{
    /// <param name="storeLocation">The store location written into the payload; the library looks the certificate up there
    /// (in the My store) when decrypting. Only tests use anything other than LocalMachine.</param>
    /// <returns>The payload, to store with the certificate's thumbprint.</returns>
    /// <exception cref="InvalidOperationException">The certificate has no RSA public key.</exception>
    public static string Encrypt(int userId, ReadOnlySpan<byte> secret, X509Certificate2 certificate,
        StoreLocation storeLocation = StoreLocation.LocalMachine)
    {
        // The library throws a plain Exception for non-RSA certificates; check first so callers get a clear error.
        using (var rsa = certificate.GetRSAPublicKey())
        {
            if (rsa is null)
            {
                throw new InvalidOperationException($"Certificate {certificate.Thumbprint} does not have an RSA key; OTP secrets need an RSA certificate.");
            }
        }
        var plaintext = string.Create(CultureInfo.InvariantCulture, $"{userId}:{Convert.ToHexString(secret)}");
        return ConfigEncryption.Encrypt(plaintext, CryptographyVersion.RsaCertificateKeyPair, StoreName.My, storeLocation, certificate);
    }

    /// <returns>The secret.</returns>
    /// <exception cref="CryptographicException">The payload is malformed, of another version, for another user, tampered with,
    /// or its certificate (with private key) is not available.</exception>
    public static byte[] Decrypt(int userId, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            // The library returns empty input unchanged; for an OTP secret that is never valid.
            throw new CryptographicException("The protected secret is empty.");
        }

        string text;
        try
        {
            text = ConfigEncryption.Decrypt(payload);
        }
        // The library reports bad input with several exception types; callers only need to handle one.
        catch (InvalidOperationException ex) { throw Unreadable(ex); }
        catch (FormatException ex) { throw Unreadable(ex); }
        catch (EndOfStreamException ex) { throw Unreadable(ex); }
        catch (NotSupportedException ex) { throw Unreadable(ex); }
        catch (IndexOutOfRangeException ex) { throw Unreadable(ex); }
        catch (ArgumentException ex) { throw Unreadable(ex); }

        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0
            || !int.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var payloadUserId)
            || payloadUserId != userId)
        {
            throw new CryptographicException("The protected secret belongs to another user.");
        }
        try
        {
            return Convert.FromHexString(text.AsSpan(separator + 1));
        }
        catch (FormatException ex)
        {
            throw Unreadable(ex);
        }
    }

    public static X509Certificate2? FindInLocalMachineMy(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        return found.Count > 0 ? found[0] : null;
    }

    static CryptographicException Unreadable(Exception inner) => new($"The protected secret cannot be decrypted: {inner.Message}", inner);
}
