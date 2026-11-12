// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Usbipd.Data;

namespace Usbipd;

/// <summary>
/// Performs the server side of the mutual TLS handshake for every client connection.
/// </summary>
sealed class TlsServerAuthenticator : IDisposable
{
    static readonly Oid ClientAuthenticationOid = new("1.3.6.1.5.5.7.3.2", "Client Authentication");
    static readonly StoreName[] ServerCertificateStores = [StoreName.My];
    static readonly StoreName[] CaCertificateStores = [StoreName.Root, StoreName.CertificateAuthority, StoreName.My];

    /// <exception cref="InvalidOperationException">A configured certificate is missing or unusable.</exception>
    public TlsServerAuthenticator(TlsSettings settings, IClientAllowList allowList, ILogger logger)
    {
        Settings = settings;
        AllowList = allowList;
        Logger = logger;

        ServerCertificate = FindCertificate(settings.ServerCertificateThumbprint, ServerCertificateStores)
            ?? throw new InvalidOperationException(
                $"Server certificate {settings.ServerCertificateThumbprint} was not found in LocalMachine\\My.");
        CheckPrivateKey(ServerCertificate);

        foreach (var thumbprint in settings.ClientCaThumbprints)
        {
            using var caCertificate = FindCertificate(thumbprint, CaCertificateStores)
                ?? throw new InvalidOperationException(
                    $"Client CA certificate {thumbprint} was not found in LocalMachine\\Root, LocalMachine\\CA or LocalMachine\\My.");
        }

        Options = new()
        {
            ServerCertificate = ServerCertificate,
            ClientCertificateRequired = true,
            RemoteCertificateValidationCallback = ValidateClientCertificate,
            AllowRenegotiation = false,
        };
    }

    readonly TlsSettings Settings;
    readonly IClientAllowList AllowList;
    readonly ILogger Logger;
    readonly SslServerAuthenticationOptions Options;

    public X509Certificate2 ServerCertificate { get; }

    public void Dispose()
    {
        ServerCertificate.Dispose();
    }

    /// <summary>
    /// Runs the TLS handshake, limited to <see cref="TlsSettings.HandshakeTimeout"/>.
    /// </summary>
    /// <returns>The authenticated stream; the caller owns it.</returns>
    /// <exception cref="OperationCanceledException">The handshake timed out, or <paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<SslStream> AuthenticateAsync(Stream networkStream, CancellationToken cancellationToken)
    {
        var sslStream = new SslStream(networkStream, false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Settings.HandshakeTimeout);
            await sslStream.AuthenticateAsServerAsync(Options, timeout.Token);
            return sslStream;
        }
        catch
        {
            await sslStream.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The chain is built with the Windows trust store, then pinned: one of the configured CA certificates must be in it.
    /// The chain that SslStream built is not used, because it does not require the client authentication purpose.
    /// </summary>
    bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? sslStreamChain, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is not X509Certificate2 clientCertificate
            || sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            Logger.ClientCertificateRejected("(none)", "(none)", "the client did not send a certificate");
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = Settings.RevocationMode;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        _ = chain.ChainPolicy.ApplicationPolicy.Add(ClientAuthenticationOid);
        if (!chain.Build(clientCertificate))
        {
            var reasons = string.Join(", ", chain.ChainStatus.Select(s => $"{s.Status}"));
            Logger.ClientCertificateRejected(clientCertificate.Subject, clientCertificate.Thumbprint, $"chain validation failed: {reasons}");
            return false;
        }

        var issuedByTrustedCa = false;
        for (var i = 1; i < chain.ChainElements.Count; ++i)
        {
            if (Settings.ClientCaThumbprints.Contains(chain.ChainElements[i].Certificate.Thumbprint))
            {
                issuedByTrustedCa = true;
                break;
            }
        }
        if (!issuedByTrustedCa)
        {
            Logger.ClientCertificateRejected(clientCertificate.Subject, clientCertificate.Thumbprint,
                "not issued by a CA listed in usbipd:Tls:ClientCaThumbprints");
            return false;
        }

        bool allowed;
        try
        {
            allowed = AllowList.IsAllowed(clientCertificate.Thumbprint);
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            // No connections while the allow-list cannot be read.
            Logger.ClientCertificateRejected(clientCertificate.Subject, clientCertificate.Thumbprint,
                $"the client certificate list cannot be read from the database: {ex.Message}");
            return false;
        }
        if (!allowed)
        {
            Logger.ClientCertificateRejected(clientCertificate.Subject, clientCertificate.Thumbprint,
                "not on the client certificate list, or disabled ('usbipd client list')");
            return false;
        }
        return true;
    }

    static X509Certificate2? FindCertificate(string thumbprint, StoreName[] storeNames)
    {
        foreach (var storeName in storeNames)
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            try
            {
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            }
            catch (CryptographicException)
            {
                continue;
            }
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (found.Count > 0)
            {
                return found[0];
            }
        }
        return null;
    }

    /// <summary>
    /// HasPrivateKey is also true when the service account may not use the key, so actually open it.
    /// </summary>
    static void CheckPrivateKey(X509Certificate2 certificate)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"Server certificate {certificate.Thumbprint} has no private key.");
        }
        try
        {
            using var rsa = certificate.GetRSAPrivateKey();
            using var ecdsa = rsa is null ? certificate.GetECDsaPrivateKey() : null;
            if ((rsa is null) && (ecdsa is null))
            {
                throw new InvalidOperationException($"Server certificate {certificate.Thumbprint} must have an RSA or ECDSA key.");
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"The private key of server certificate {certificate.Thumbprint} cannot be used by this account: {ex.Message}", ex);
        }
    }
}
