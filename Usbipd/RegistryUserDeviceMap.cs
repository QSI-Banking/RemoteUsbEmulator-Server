// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Reads the mappings written by `usbipd user add` (HKLM\SOFTWARE\usbipd-win\Users\&lt;name&gt;).
/// </summary>
sealed class RegistryUserDeviceMap : IUserDeviceMap
{
    /// <summary>
    /// TEMPORARY: device used for <see cref="HardcodedUserAuthenticator.UserName"/> when no mapping exists.
    /// HD Pro Webcam C920, USB\VID_046D&amp;PID_082D\A5BA06AF.
    /// </summary>
    public static readonly Guid FallbackDeviceGuid = new("b60eda76-b612-4337-8270-fb97e53f8376");

    public Guid? GetDeviceGuid(string userName)
    {
        var guid = UsbipdRegistry.Instance.GetUserDevice(userName);
        if (guid.HasValue)
        {
            return guid;
        }
        return UserNames.Comparer.Equals(userName, HardcodedUserAuthenticator.UserName) ? FallbackDeviceGuid : null;
    }
}
