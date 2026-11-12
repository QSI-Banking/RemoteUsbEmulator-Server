// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Windows.Win32;
using Windows.Win32.Security;

namespace Usbipd;

sealed partial class Server : BackgroundService
{
    public const string SingletonMutexName = @"Global\usbipd-{A8256F62-728F-49B0-82BB-E5E48F83D28F}";

    public Server(ILogger<Server> logger, IConfiguration config, IServiceScopeFactory serviceScopeFactory, PcapNg _)
    {
        Logger = logger;
        Configuration = config;
        ServiceScopeFactory = serviceScopeFactory;
    }

    readonly ILogger Logger;
    readonly IConfiguration Configuration;
    readonly IServiceScopeFactory ServiceScopeFactory;
    TcpListener? Listener;
    TlsServerAuthenticator? TlsAuthenticator;

    public override void Dispose()
    {
        Listener?.Dispose();
        TlsAuthenticator?.Dispose();
        base.Dispose();
    }

    public static bool IsRunning()
    {
        try
        {
            using var mutex = Mutex.OpenExisting(SingletonMutexName);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists nevertheless.
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    static void EnablePrivilege(string name)
    {
        var tokenPrivileges = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
        };
        PInvoke.LookupPrivilegeValue(null, name, out tokenPrivileges.Privileges[0].Luid).ThrowOnWin32Error(nameof(PInvoke.LookupPrivilegeValue));
        tokenPrivileges.Privileges[0].Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED;

        using var currentProcess = PInvoke.GetCurrentProcess_SafeHandle();
        PInvoke.OpenProcessToken(currentProcess, TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES, out var token).ThrowOnWin32Error(nameof(PInvoke.OpenProcessToken));
        using (token)
        {
            unsafe // DevSkim: ignore DS172412
            {
                PInvoke.AdjustTokenPrivileges(token, false, &tokenPrivileges).ThrowOnWin32Error(nameof(PInvoke.AdjustTokenPrivileges));
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.Debug(GitVersionInformation.InformationalVersion);

        // Every connection is mutual TLS; there is no plaintext listener. Fail fast on configuration errors.
        TlsSettings tlsSettings;
        try
        {
            tlsSettings = TlsSettings.Read(Configuration);
            TlsAuthenticator = new TlsServerAuthenticator(tlsSettings, Logger);
        }
        catch (InvalidOperationException ex)
        {
            Logger.TlsConfigurationError(ex.Message);
            throw;
        }
        var tlsAuthenticator = TlsAuthenticator;
        try
        {
            _ = SessionSettings.Read(Configuration);
            // Users and mappings are in SQL Server; attaches fail (but the service runs) while it cannot be reached.
            _ = Data.Database.GetConnectionString(Configuration);
        }
        catch (InvalidOperationException ex)
        {
            Logger.ConfigurationError(ex.Message);
            throw;
        }

        // Cleanup any left-overs in case the previous instance crashed.
        _ = UsbipdRegistry.Instance.SetAllDevicesAsDetached();

        // Non-interactive services have this disabled by default.
        // We require it so the ConfigurationManager can change the driver.
        EnablePrivilege("SeLoadDriverPrivilege");

        Listener = tlsSettings.ListenAddress is null
            ? TcpListener.Create(tlsSettings.Port)
            : new TcpListener(tlsSettings.ListenAddress, tlsSettings.Port);

        // All client sockets will inherit these. Formally, these options have to
        // be set before the socket reaches connected state, including Accept().
        Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1 /* s */);
        Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10 /* s */);
        Listener.Server.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 5);

        Listener.Start();
        Logger.TlsListening($"{Listener.LocalEndpoint}", tlsAuthenticator.ServerCertificate.Subject,
            tlsAuthenticator.ServerCertificate.Thumbprint, tlsAuthenticator.ServerCertificate.NotAfter);
        while (true)
        {
            var tcpClient = await Listener.AcceptTcpClientAsync(stoppingToken);
            var clientAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)!.Address;
            if (clientAddress.IsIPv4MappedToIPv6)
            {
                clientAddress = clientAddress.MapToIPv4();
            }
            if (clientAddress.Equals(IPAddress.Loopback))
            {
                // HACK: workaround for https://github.com/microsoft/WSL/issues/10741
                Logger.Debug("WSL keep-alive workaround");
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, false);
            }

            _ = Task.Run(() => HandleClientAsync(tcpClient, clientAddress, tlsAuthenticator, stoppingToken), stoppingToken);
        }
    }

    async Task HandleClientAsync(TcpClient tcpClient, IPAddress clientAddress, TlsServerAuthenticator tlsAuthenticator,
        CancellationToken stoppingToken)
    {
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.Debug($"new connection from {clientAddress}");
        }
        try
        {
            using var cancellationTokenRegistration = stoppingToken.Register(() => tcpClient.Close());
            using var serviceScope = ServiceScopeFactory.CreateScope();
            var clientContext = serviceScope.ServiceProvider.GetRequiredService<ClientContext>();
            clientContext.TcpClient = tcpClient;
            clientContext.ClientAddress = clientAddress;

            SslStream? sslStream = null;
            try
            {
                sslStream = await tlsAuthenticator.AuthenticateAsync(tcpClient.GetStream(), stoppingToken);
                clientContext.ClientCertificateSubject = sslStream.RemoteCertificate?.Subject;
                clientContext.ClientCertificateThumbprint = sslStream.RemoteCertificate?.GetCertHashString();
                // The client context owns the stream from here on.
                clientContext.Stream = sslStream;
                sslStream = null;
            }
            catch (AuthenticationException ex)
            {
                Logger.ClientTlsFailed(clientAddress, ex.InnerException?.Message ?? ex.Message);
                return;
            }
            catch (IOException ex)
            {
                Logger.ClientTlsFailed(clientAddress, ex.Message);
                return;
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Logger.ClientTlsFailed(clientAddress, "the TLS handshake timed out");
                return;
            }
            finally
            {
                if (sslStream is not null)
                {
                    await sslStream.DisposeAsync();
                }
            }
            Logger.ClientTlsAuthenticated(clientAddress, clientContext.ClientCertificateSubject ?? string.Empty,
                clientContext.ClientCertificateThumbprint ?? string.Empty);

            var connectedClient = serviceScope.ServiceProvider.GetRequiredService<ConnectedClient>();
            await connectedClient.RunAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.Debug($"connection close: {ex.Message}");
            }
            throw;
        }
        finally
        {
            Logger.Debug("connection closed");
        }
    }
}
