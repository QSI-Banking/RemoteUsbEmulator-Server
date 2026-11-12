// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Web.Script.Serialization;
using WixToolset.Dtf.WindowsInstaller;

namespace Usbipd.InstallerActions;

public static class CustomActions
{
    const string PortProperty = "TLS_PORT";
    const string ServerCertificateProperty = "TLS_SERVER_CERT";
    const string ClientCaProperty = "TLS_CLIENT_CA";
    const string SessionMaxMinutesProperty = "SESSION_MAX_MINUTES";
    const string DefaultSessionMaxMinutes = "10";
    const string SqlServerProperty = "SQL_SERVER";
    const string SqlDatabaseProperty = "SQL_DATABASE";
    const string SqlTrustProperty = "SQL_TRUST_SERVER_CERT";
    const string DefaultSqlDatabase = "usbipd";
    const string DefaultPort = "3241";

    const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    /// Presets TLS_PORT, TLS_SERVER_CERT, TLS_CLIENT_CA, SESSION_MAX_MINUTES and the SQL_* properties from an existing appsettings.json,
    /// unless they were passed on the command line. Runs in both the UI and the execute sequence,
    /// so a silent upgrade keeps the current settings and the firewall rule keeps the current port.
    /// </summary>
    [CustomAction]
    public static ActionResult ReadTlsSettings(Session session)
    {
        string port = null;
        string server = null;
        string ca = null;
        string sessionMinutes = null;
        string connectionString = null;
        try
        {
            var folder = session["APPLICATIONFOLDER"];
            var path = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "appsettings.json");
            if (path is not null && File.Exists(path))
            {
                var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as IDictionary<string, object>;
                var tls = Child(Child(root, "usbipd"), "Tls");
                port = Text(Child(tls, "Port"));
                server = Text(Child(tls, "ServerCertificateThumbprint"));
                ca = Text(Child(tls, "ClientCaThumbprints"));
                sessionMinutes = Text(Child(Child(Child(root, "usbipd"), "Session"), "MaxDurationMinutes"));
                connectionString = Text(Child(Child(root, "ConnectionStrings"), "Usbipd"));
                session.Log($"ReadTlsSettings: read {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            session.Log($"ReadTlsSettings: cannot read appsettings.json: {ex.Message}");
        }

        SetIfEmpty(session, PortProperty, string.IsNullOrWhiteSpace(port) ? DefaultPort : port);
        SetIfEmpty(session, ServerCertificateProperty, server);
        SetIfEmpty(session, ClientCaProperty, ca);
        SetIfEmpty(session, SessionMaxMinutesProperty, string.IsNullOrWhiteSpace(sessionMinutes) ? DefaultSessionMaxMinutes : sessionMinutes);

        // An empty SQL_SERVER keeps the connection string as it is (usbipd only replaces it when a server is given).
        var (sqlServer, sqlDatabase, sqlTrust) = ParseConnectionString(session, connectionString);
        SetIfEmpty(session, SqlServerProperty, sqlServer);
        SetIfEmpty(session, SqlDatabaseProperty, string.IsNullOrWhiteSpace(sqlDatabase) ? DefaultSqlDatabase : sqlDatabase);
        SetIfEmpty(session, SqlTrustProperty, sqlTrust ? "1" : null);
        return ActionResult.Success;
    }

    /// <summary>
    /// Fills the drop-downs of the TLS dialog: server certificates (LocalMachine\My, with a private key)
    /// and CA certificates (LocalMachine\Root and LocalMachine\CA). Preselects the only candidate, if there is one.
    /// </summary>
    [CustomAction]
    public static ActionResult ListCertificates(Session session)
    {
        try
        {
            using var view = session.Database.OpenView("SELECT `Property`, `Order`, `Value`, `Text` FROM `ComboBox`");
            view.Execute();

            var servers = Certificates(StoreName.My).Where(c => c.HasPrivateKey).ToList();
            var preferred = servers.Where(IsServerCertificate).ToList();
            AddItems(view, ServerCertificateProperty, preferred.Concat(servers.Except(preferred)));
            if (string.IsNullOrEmpty(session[ServerCertificateProperty]) && preferred.Count == 1)
            {
                session[ServerCertificateProperty] = preferred[0].Thumbprint;
            }

            var authorities = Certificates(StoreName.Root).Concat(Certificates(StoreName.CertificateAuthority))
                .GroupBy(c => c.Thumbprint).Select(g => g.First())
                .OrderBy(c => c.GetNameInfo(X509NameType.SimpleName, false), StringComparer.OrdinalIgnoreCase).ToList();
            AddItems(view, ClientCaProperty, authorities);

            // A value from appsettings.json may hold several CAs; show the first one in the drop-down.
            var current = session[ClientCaProperty];
            if (!string.IsNullOrEmpty(current) && current.IndexOfAny([',', ';', ' ']) >= 0)
            {
                session[ClientCaProperty] = current.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)[0];
            }

            session.Log($"ListCertificates: {servers.Count} server certificate(s), {authorities.Count} CA certificate(s)");
        }
        catch (Exception ex) when (ex is CryptographicException or InstallerException)
        {
            session.Log($"ListCertificates: {ex.Message}");
        }
        return ActionResult.Success;
    }

    static List<X509Certificate2> Certificates(StoreName storeName)
    {
        using var store = new X509Store(storeName, StoreLocation.LocalMachine);
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (CryptographicException)
        {
            return [];
        }
        var now = DateTime.Now;
        return [.. store.Certificates.Cast<X509Certificate2>()
            .Where(c => c.NotBefore <= now && now <= c.NotAfter)
            .OrderBy(c => c.GetNameInfo(X509NameType.SimpleName, false), StringComparer.OrdinalIgnoreCase)];
    }

    static bool IsServerCertificate(X509Certificate2 certificate)
    {
        var usages = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        return usages is null || usages.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == ServerAuthenticationOid);
    }

    static void AddItems(View view, string property, IEnumerable<X509Certificate2> certificates)
    {
        var order = 1;
        foreach (var certificate in certificates)
        {
            var name = certificate.GetNameInfo(X509NameType.SimpleName, false);
            var issuer = certificate.GetNameInfo(X509NameType.SimpleName, true);
            var text = string.Format(CultureInfo.InvariantCulture, "{0}  (issued by {1}, expires {2:yyyy-MM-dd})  {3}",
                name, issuer, certificate.NotAfter, certificate.Thumbprint);
            // Square brackets would be read as MSI property references
            text = text.Replace('[', '(').Replace(']', ')');
            using var record = new Record(property, order++, certificate.Thumbprint, text);
            view.InsertTemporary(record);
        }
    }

    /// <summary>
    /// Server, database and TrustServerCertificate from a SqlClient connection string (any synonym of the keywords).
    /// </summary>
    static (string Server, string Database, bool Trust) ParseConnectionString(Session session, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return (null, null, false);
        }
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            string Get(params string[] keys) => keys.Where(builder.ContainsKey).Select(k => Convert.ToString(builder[k], CultureInfo.InvariantCulture)).FirstOrDefault();
            var trust = Get("TrustServerCertificate", "Trust Server Certificate");
            return (Get("Server", "Data Source", "Address", "Addr", "Network Address"), Get("Database", "Initial Catalog"),
                string.Equals(trust, "true", StringComparison.OrdinalIgnoreCase) || trust == "1");
        }
        catch (ArgumentException ex)
        {
            session.Log($"ReadTlsSettings: cannot parse the connection string: {ex.Message}");
            return (null, null, false);
        }
    }

    static void SetIfEmpty(Session session, string property, string value)
    {
        if (string.IsNullOrEmpty(session[property]) && !string.IsNullOrWhiteSpace(value))
        {
            session[property] = value.Trim();
        }
    }

    static object Child(object parent, string name)
    {
        return parent is IDictionary<string, object> dictionary
            ? dictionary.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value
            : null;
    }

    /// <summary>
    /// A JSON scalar as text, or a JSON array as a comma-separated list.
    /// </summary>
    static string Text(object value)
    {
        return value switch
        {
            null => null,
            string s => s,
            IEnumerable list => string.Join(",", list.Cast<object>().Select(o => Convert.ToString(o, CultureInfo.InvariantCulture))),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
