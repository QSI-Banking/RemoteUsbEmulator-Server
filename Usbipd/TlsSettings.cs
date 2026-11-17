// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Usbipd;

/// <summary>
/// TLS settings from the "qsiusbd:Tls" configuration section (appsettings.json next to qsiusbd.exe).
/// </summary>
sealed class TlsSettings
{
    public const string SectionName = "qsiusbd:Tls";
    public const ushort DefaultPort = 3241;

    public ushort Port { get; private init; }

    /// <summary>
    /// Local address to listen on; <see langword="null"/> means all interfaces (IPv4 and IPv6).
    /// </summary>
    public IPAddress? ListenAddress { get; private init; }

    /// <summary>
    /// SHA-1 thumbprint of the server certificate in LocalMachine\My; compared without regard to case.
    /// </summary>
    public string ServerCertificateThumbprint { get; private init; } = string.Empty;

    /// <summary>
    /// A client certificate is accepted only if one of these CA certificates is in its chain.
    /// </summary>
    public HashSet<string> ClientCaThumbprints { get; private init; } = [];

    public TimeSpan HandshakeTimeout { get; private init; }

    public X509RevocationMode RevocationMode { get; private init; }

    /// <summary>
    /// The section as written in appsettings.json.
    /// </summary>
    sealed class Section
    {
        [Range(1, 65535)]
        public int Port { get; set; } = DefaultPort;

        public string? ListenAddress { get; set; }

        [Required]
        public string? ServerCertificateThumbprint { get; set; }

        [MinLength(1, ErrorMessage = "ClientCaThumbprints is required: the thumbprint of the CA that issues client certificates.")]
        public List<string> ClientCaThumbprints { get; } = [];

        [Range(1, 300)]
        public int HandshakeTimeoutSeconds { get; set; } = 10;

        [EnumDataType(typeof(X509RevocationMode))]
        public X509RevocationMode RevocationMode { get; set; } = X509RevocationMode.NoCheck;
    }

    /// <exception cref="InvalidOperationException">The configuration is missing or invalid.</exception>
    public static TlsSettings Read(IConfiguration configuration)
    {
        var section = SettingsReader.Read(configuration, SectionName, new Section());
        IPAddress? listenAddress = null;
        if (!string.IsNullOrWhiteSpace(section.ListenAddress) && !IPAddress.TryParse(section.ListenAddress, out listenAddress))
        {
            throw new InvalidOperationException($"{SectionName}:ListenAddress '{section.ListenAddress}' is not an IP address.");
        }
        return new()
        {
            Port = (ushort)section.Port,
            ListenAddress = listenAddress,
            ServerCertificateThumbprint = CleanThumbprint(section.ServerCertificateThumbprint!),
            ClientCaThumbprints = new(section.ClientCaThumbprints.Select(CleanThumbprint), StringComparer.OrdinalIgnoreCase),
            HandshakeTimeout = TimeSpan.FromSeconds(section.HandshakeTimeoutSeconds),
            RevocationMode = section.RevocationMode,
        };
    }

    /// <summary>
    /// A thumbprint copied from the Windows certificate dialog starts with an invisible left-to-right mark (U+200E).
    /// </summary>
    public static string CleanThumbprint(string thumbprint) => thumbprint.Replace("\u200E", "", StringComparison.Ordinal).Trim();
}
