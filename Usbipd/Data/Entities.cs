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
