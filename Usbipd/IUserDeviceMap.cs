// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Maps a user to the bind GUID (HKLM\SOFTWARE\usbipd-win\Devices\{GUID}) of the one device that user may attach.
/// </summary>
interface IUserDeviceMap
{
    /// <returns>The bind GUID, or <see langword="null"/> if no device is mapped to the user.</returns>
    Guid? GetDeviceGuid(string userName);
}
