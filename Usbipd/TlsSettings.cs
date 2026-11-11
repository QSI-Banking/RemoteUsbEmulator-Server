// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Usbipd;

/// <summary>
/// TLS settings from the "usbipd:Tls" configuration section (appsettings.json next to usbipd.exe).
/// </summary>
sealed class TlsSettings
{
    public const string SectionName = "usbipd:Tls";
    public const ushort DefaultPort = 3241;
    public static readonly TimeSpan DefaultHandshakeTimeout = TimeSpan.FromSeconds(10);
    static readonly char[] ListSeparators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// TCP port for TLS connections.
    /// </summary>
    public ushort Port { get; private init; } = DefaultPort;

    /// <summary>
    /// Local address to listen on; <see langword="null"/> means all interfaces (IPv4 and IPv6).
    /// </summary>
    public IPAddress? ListenAddress { get; private init; }

    /// <summary>
    /// SHA-1 thumbprint of the server certificate in LocalMachine\My (upper case hex).
    /// </summary>
    public string ServerCertificateThumbprint { get; private init; } = string.Empty;

    /// <summary>
    /// A client certificate is accepted only if one of these CA certificates is in its chain.
    /// </summary>
    public HashSet<string> ClientCaThumbprints { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If not empty, only these client certificates are accepted.
    /// </summary>
    public HashSet<string> AllowedClientThumbprints { get; } = new(StringComparer.OrdinalIgnoreCase);

    public TimeSpan HandshakeTimeout { get; private init; } = DefaultHandshakeTimeout;

    public X509RevocationMode RevocationMode { get; private init; } = X509RevocationMode.NoCheck;

    /// <exception cref="InvalidOperationException">The configuration is missing or invalid.</exception>
    public static TlsSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var settings = new TlsSettings
        {
            Port = ReadPort(section["Port"]),
            ListenAddress = ReadListenAddress(section["ListenAddress"]),
            ServerCertificateThumbprint = NormalizeThumbprint(section["ServerCertificateThumbprint"], "ServerCertificateThumbprint"),
            HandshakeTimeout = ReadHandshakeTimeout(section["HandshakeTimeoutSeconds"]),
            RevocationMode = ReadRevocationMode(section["RevocationMode"]),
        };
        foreach (var value in ReadList(section.GetSection("ClientCaThumbprints")))
        {
            _ = settings.ClientCaThumbprints.Add(NormalizeThumbprint(value, "ClientCaThumbprints"));
        }
        if (settings.ClientCaThumbprints.Count == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:ClientCaThumbprints is required: the thumbprint of the CA that issues client certificates.");
        }
        foreach (var value in ReadList(section.GetSection("AllowedClientThumbprints")))
        {
            _ = settings.AllowedClientThumbprints.Add(NormalizeThumbprint(value, "AllowedClientThumbprints"));
        }
        return settings;
    }

    /// <summary>
    /// Accepts thumbprints as copied from the certificate dialog or certutil: spaces, colons and
    /// invisible direction marks are removed, and the result is upper case.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not a SHA-1 thumbprint.</exception>
    public static string NormalizeThumbprint(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{key} is required; set it in appsettings.json next to usbipd.exe.");
        }
        var builder = new StringBuilder(40);
        foreach (var c in value)
        {
            if (char.IsAsciiHexDigit(c))
            {
                _ = builder.Append(char.ToUpperInvariant(c));
            }
            else if (!(char.IsWhiteSpace(c) || c is ':' or '‎' or '‏' or '﻿'))
            {
                throw new InvalidOperationException($"{SectionName}:{key} contains an invalid character '{c}'.");
            }
        }
        if (builder.Length != 40)
        {
            throw new InvalidOperationException($"{SectionName}:{key} must be a 40 hex digit (SHA-1) certificate thumbprint.");
        }
        return builder.ToString();
    }

    static ushort ReadPort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPort;
        }
        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port == 0)
        {
            throw new InvalidOperationException($"{SectionName}:Port '{value}' must be a number from 1 to 65535.");
        }
        return port;
    }

    static IPAddress? ReadListenAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException($"{SectionName}:ListenAddress '{value}' is not an IP address.");
        }
        return address;
    }

    static TimeSpan ReadHandshakeTimeout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultHandshakeTimeout;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds < 1 || seconds > 300)
        {
            throw new InvalidOperationException($"{SectionName}:HandshakeTimeoutSeconds '{value}' must be a number from 1 to 300.");
        }
        return TimeSpan.FromSeconds(seconds);
    }

    static X509RevocationMode ReadRevocationMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return X509RevocationMode.NoCheck;
        }
        if (!Enum.TryParse<X509RevocationMode>(value, true, out var mode) || !Enum.IsDefined(mode)
            || value.Trim().All(char.IsAsciiDigit))
        {
            throw new InvalidOperationException($"{SectionName}:RevocationMode '{value}' must be NoCheck, Online or Offline.");
        }
        return mode;
    }

    /// <summary>
    /// Splits a list given as one string, with entries separated by commas, semicolons or spaces.
    /// </summary>
    public static string[] SplitList(string value) => value.Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Accepts a JSON array, or a single string with entries separated by commas, semicolons or spaces.
    /// </summary>
    static List<string> ReadList(IConfigurationSection section)
    {
        if (!string.IsNullOrWhiteSpace(section.Value))
        {
            return [.. SplitList(section.Value)];
        }
        var values = new List<string>();
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
            {
                values.Add(child.Value);
            }
        }
        return values;
    }
}
