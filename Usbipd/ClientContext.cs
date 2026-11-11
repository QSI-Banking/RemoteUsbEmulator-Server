// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Net.Sockets;
using Usbipd.Automation;

namespace Usbipd;

sealed partial class ClientContext : IDisposable
{
    public TcpClient TcpClient { get; set; } = new();
    /// <summary>
    /// The authenticated TLS stream over <see cref="TcpClient"/>; all protocol traffic goes through it.
    /// </summary>
    public Stream Stream { get; set; } = Stream.Null;
    /// <summary>
    /// Subject of the client certificate that passed the TLS handshake.
    /// </summary>
    public string? ClientCertificateSubject { get; set; }
    /// <summary>
    /// SHA-1 thumbprint of the client certificate that passed the TLS handshake.
    /// </summary>
    public string? ClientCertificateThumbprint { get; set; }
    /// <summary>
    /// Canonical remote client IP address (either IPv4 or IPv6).
    /// </summary>
    public IPAddress ClientAddress { get; set; } = IPAddress.Any;
    public BusId? AttachedBusId { get; set; }
    public DeviceFile? AttachedDevice { get; set; }
    /// <summary>
    /// User name that passed OP_REQ_AUTH on this connection, if any.
    /// </summary>
    public string? AuthenticatedUser { get; set; }
    /// <summary>
    /// Bind GUID of the only device this connection may import.
    /// </summary>
    public Guid? AuthorizedGuid { get; set; }

    void IDisposable.Dispose()
    {
        Stream.Dispose();
        TcpClient.Dispose();
        AttachedDevice?.Dispose();
    }
}
