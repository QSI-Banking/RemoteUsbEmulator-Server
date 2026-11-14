// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd.Data;

/// <summary>
/// A user that may attach one device after authenticating with a one-time password.
/// </summary>
sealed class User
{
    public int Id { get; set; }

    /// <summary>
    /// Unique, compared case-insensitively (database collation), see <see cref="UserNames"/>.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Windows account of the administrator who added the user.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;

    public UserDevice? UserDevice { get; set; }

    public OtpSecret? OtpSecret { get; set; }
}

/// <summary>
/// The TOTP secret of a user (RFC 6238: HMAC-SHA1, 6 digits, 30 seconds).
/// <para>
/// Stored in plain text for now; encryption with key rotation is planned (step 8e). Protect read access to this table.
/// </para>
/// </summary>
sealed class OtpSecret
{
    public int UserId { get; set; }

    public User? User { get; set; }

    public byte[] Secret { get; set; } = [];

    /// <summary>
    /// The time step of the last accepted code; a code is accepted only for a later step, so it cannot be used twice.
    /// The only column the service writes.
    /// </summary>
    public long LastAcceptedStep { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// A USB device on a server. The identity is the server plus the Windows device instance ID,
/// so it survives unbind and rebind (which give the device a new bind GUID).
/// </summary>
sealed class Device
{
    public int Id { get; set; }

    /// <summary>
    /// Computer name of the server the device is plugged into.
    /// </summary>
    public string ServerName { get; set; } = string.Empty;

    public string InstanceId { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Set while the device is shared ('usbipd bind').
    /// </summary>
    public Bind? Bind { get; set; }
}

/// <summary>
/// A shared device ('usbipd bind'). Unbind deletes the row; a rebind gets a new GUID.
/// <para>
/// A bind is only valid while the service has watched the device without a break since an administrator confirmed it:
/// <see cref="ConfirmedRunId"/> must be the current run of the service (see <see cref="ServerRun"/>), and the service
/// deletes the bind as soon as the device is removed.
/// </para>
/// </summary>
sealed class Bind
{
    /// <summary>
    /// The bind GUID shown by 'usbipd list' (persisted GUID).
    /// </summary>
    public Guid Id { get; set; }

    public int DeviceId { get; set; }

    public Device? Device { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public Attachment? Attachment { get; set; }

    /// <summary>
    /// Recorded when the bind is confirmed, see <see cref="DeviceIdentity"/>; <see langword="null"/> until then.
    /// </summary>
    public string? Fingerprint { get; set; }

    public string? LocationPath { get; set; }

    public string? HardwareId { get; set; }

    public string? SerialNumber { get; set; }

    public string? Interfaces { get; set; }

    /// <summary>
    /// The run of the service in which an administrator confirmed the bind ('usbipd bind', 'usbipd bind --review').
    /// </summary>
    public Guid? ConfirmedRunId { get; set; }

    public DateTime? ConfirmedUtc { get; set; }

    public string? ConfirmedBy { get; set; }
}

/// <summary>
/// The current run of the service on a server; a new one for every start. Written by the service.
/// </summary>
sealed class ServerRun
{
    public string ServerName { get; set; } = string.Empty;

    public Guid RunId { get; set; }

    public DateTime StartedUtc { get; set; }
}

/// <summary>
/// A shared device that is attached to a client right now. Written by the service; deleting the row
/// ('usbipd detach') makes the service detach the device.
/// </summary>
sealed class Attachment
{
    public Guid BindId { get; set; }

    public Bind? Bind { get; set; }

    public string BusId { get; set; } = string.Empty;

    public string ClientAddress { get; set; } = string.Empty;

    /// <summary>
    /// Instance ID of the stub (VBoxUSB) device while attached.
    /// </summary>
    public string StubInstanceId { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; }
}

/// <summary>
/// A client certificate that may connect ('usbipd client add'). The list is shared by all servers.
/// A certificate must also be issued by a CA in usbipd:Tls:ClientCaThumbprints.
/// </summary>
sealed class ClientCertificate
{
    public int Id { get; set; }

    /// <summary>
    /// SHA-1 thumbprint, 40 upper case hex digits.
    /// </summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>
    /// For administrators, for example the computer name of the client.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The certificate subject, if it was added from a certificate file.
    /// </summary>
    public string? Subject { get; set; }

    public DateTime? NotAfterUtc { get; set; }

    /// <summary>
    /// Disabled certificates are refused, and their sessions end.
    /// </summary>
    public bool Enabled { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// The one device a user may attach.
/// </summary>
sealed class UserDevice
{
    public int UserId { get; set; }

    public User? User { get; set; }

    public int DeviceId { get; set; }

    public Device? Device { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Every administrative change (users, mappings, binds, certificates, OTP secrets).
/// </summary>
sealed class AdminAuditEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>
    /// Windows account that made the change.
    /// </summary>
    public string Account { get; set; } = string.Empty;

    public string ServerName { get; set; } = string.Empty;

    /// <summary>
    /// For example "user add".
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// What was changed, for example the user name; never a secret.
    /// </summary>
    public string Target { get; set; } = string.Empty;
}
