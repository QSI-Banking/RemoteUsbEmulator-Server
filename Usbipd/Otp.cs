// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using OtpNet;

namespace Usbipd;

/// <summary>
/// One-time passwords from authenticator apps (TOTP: SHA-1, 6 digits, 30 seconds), using Otp.NET.
/// </summary>
static class Otp
{
    public const string IssuerKey = "qsiusbd:Otp:Issuer";
    const string DefaultIssuer = "QSI USB/IP";
    const int SecretLength = 20; // 160 bits, as RFC 4226 recommends

    public static byte[] CreateSecret() => KeyGeneration.GenerateRandomKey(SecretLength);

    /// <summary>
    /// Accepts the code of the current 30 second step, or of the step before or after it (a clock that is a little off).
    /// Steps up to <paramref name="lastAcceptedStep"/> are refused, so each code works only once.
    /// </summary>
    /// <returns>The matching step, to record as the last accepted one; or <see langword="null"/>.</returns>
    public static long? Verify(byte[] secret, string code, DateTime utcNow, long lastAcceptedStep)
    {
        var matched = new Totp(secret).VerifyTotp(utcNow, code, out var step, VerificationWindow.RfcSpecifiedNetworkDelay);
        return matched && step > lastAcceptedStep ? step : null;
    }

    public static string GetIssuer(IConfiguration configuration)
    {
        var issuer = configuration[IssuerKey];
        return string.IsNullOrWhiteSpace(issuer) ? DefaultIssuer : issuer.Trim();
    }

    public static string ToBase32(byte[] secret) => Base32Encoding.ToString(secret);

    /// <summary>
    /// The otpauth:// URI that authenticator apps import; also the content of the enrollment QR code.
    /// </summary>
    public static string GetKeyUri(string issuer, string account, byte[] secret) =>
        new OtpUri(OtpType.Totp, ToBase32(secret), account, issuer).ToString();
}
