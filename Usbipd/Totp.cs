// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Usbipd;

/// <summary>
/// Time-based one-time passwords (RFC 6238) as used by authenticator apps: HMAC-SHA1, 6 digits, 30 second steps.
/// </summary>
static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int SecretLength = 20; // 160 bits, as RFC 4226 recommends
    public const string DefaultIssuer = "QSI USB/IP";
    public const string IssuerKey = "usbipd:Otp:Issuer";

    /// <summary>
    /// Codes of the previous and the next step are also accepted, for clocks that are a little off.
    /// </summary>
    const int AllowedDriftSteps = 1;

    public static byte[] CreateSecret()
    {
        return RandomNumberGenerator.GetBytes(SecretLength);
    }

    public static long GetStep(DateTimeOffset time)
    {
        return time.ToUnixTimeSeconds() / StepSeconds;
    }

    public static string Compute(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms (RFC 6238 and authenticator apps use HMAC-SHA1, which is not affected by SHA-1 collisions)
        _ = HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <returns>The step of the matching code, or <see langword="null"/>. Only steps after <paramref name="lastAcceptedStep"/> match.</returns>
    public static long? Verify(ReadOnlySpan<byte> secret, string code, long currentStep, long lastAcceptedStep)
    {
        if (code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return null;
        }
        var codeBytes = Encoding.ASCII.GetBytes(code);
        long? accepted = null;
        // Every candidate is computed and compared, so the time taken does not depend on which one matches.
        for (var step = currentStep - AllowedDriftSteps; step <= currentStep + AllowedDriftSteps; ++step)
        {
            var match = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, step)), codeBytes);
            if (match && step > lastAcceptedStep && accepted is null)
            {
                accepted = step;
            }
        }
        return accepted;
    }

    public static string GetIssuer(IConfiguration configuration)
    {
        var issuer = configuration[IssuerKey];
        return string.IsNullOrWhiteSpace(issuer) ? DefaultIssuer : issuer.Trim();
    }

    /// <summary>
    /// RFC 4648 base32 without padding, as authenticator apps expect.
    /// </summary>
    public static string ToBase32(ReadOnlySpan<byte> data)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var b in data)
        {
            buffer = ((buffer << 8) | b) & 0xFFFF;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                _ = builder.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0)
        {
            _ = builder.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }
        return builder.ToString();
    }

    /// <summary>
    /// The key URI that authenticator apps import (also the content of the enrollment QR code).
    /// </summary>
    public static string GetKeyUri(string issuer, string account, ReadOnlySpan<byte> secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        return $"otpauth://totp/{label}?secret={ToBase32(secret)}&issuer={Uri.EscapeDataString(issuer)}" +
            $"&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}
