// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Usbipd;

/// <summary>
/// 'usbipd installer configure_tls': the MSI runs this (deferred, as SYSTEM) to write the TLS and session settings chosen
/// in the installer into appsettings.json next to usbipd.exe. Settings that are not passed are kept, and the
/// result is validated exactly as the service validates it at start, so a bad choice fails the installation.
/// </summary>
static class InstallerTlsConfiguration
{
    static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static ExitCode Configure(IConsole console, string? port, string? serverCertificate, string? clientCertificateAuthorities,
        string? sessionMaxMinutes)
    {
        try
        {
            var root = Load(SettingsPath);
            var tls = GetObject(GetObject(root, "usbipd"), "Tls");

            if (!string.IsNullOrWhiteSpace(port))
            {
                if (!ushort.TryParse(port.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || (value == 0))
                {
                    throw new InvalidOperationException($"TLS port '{port}' must be a number from 1 to 65535.");
                }
                tls["Port"] = value;
            }

            if (!string.IsNullOrWhiteSpace(serverCertificate))
            {
                tls["ServerCertificateThumbprint"] = TlsSettings.NormalizeThumbprint(serverCertificate, "ServerCertificateThumbprint");
            }

            if (!string.IsNullOrWhiteSpace(clientCertificateAuthorities))
            {
                var thumbprints = new JsonArray();
                foreach (var thumbprint in TlsSettings.SplitList(clientCertificateAuthorities))
                {
                    // the JsonNode overload; the generic Add<T> is not trim/AOT safe
                    thumbprints.Add((JsonNode)JsonValue.Create(TlsSettings.NormalizeThumbprint(thumbprint, "ClientCaThumbprints")));
                }
                tls["ClientCaThumbprints"] = thumbprints;
            }

            if (!string.IsNullOrWhiteSpace(sessionMaxMinutes))
            {
                if (!int.TryParse(sessionMaxMinutes.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                    || minutes < 1 || minutes > 1440)
                {
                    throw new InvalidOperationException($"Session limit '{sessionMaxMinutes}' must be a number of minutes from 1 to 1440.");
                }
                GetObject(GetObject(root, "usbipd"), "Session")["MaxDurationMinutes"] = minutes;
            }

            var json = Serialize(root);
            Validate(json);

            File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
            console.ReportInfo($"TLS settings written to {SettingsPath}.");
            return ExitCode.Success;
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException
            or UnauthorizedAccessException or CryptographicException)
        {
            console.ReportError(ex.Message);
            return ExitCode.Failure;
        }
    }

    /// <summary>
    /// The same checks as at service start: required values present, certificates found, private key usable.
    /// </summary>
    static void Validate(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        _ = SessionSettings.Read(configuration);
        var settings = TlsSettings.Read(configuration);
        using var authenticator = new TlsServerAuthenticator(settings, NullLogger.Instance);
    }

    static JsonObject Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        var node = JsonNode.Parse(File.ReadAllText(path),
            new JsonNodeOptions { PropertyNameCaseInsensitive = true },
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return node as JsonObject ?? throw new InvalidOperationException($"{path} does not contain a JSON object.");
    }

    static JsonObject GetObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject child)
        {
            return child;
        }
        JsonObject created = [];
        parent[name] = created;
        return created;
    }

    static string Serialize(JsonNode node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            node.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }
}
