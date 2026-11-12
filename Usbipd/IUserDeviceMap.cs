// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Maps a user to the current bind GUID of the one device that user may attach (see <see cref="SqlUserDeviceMap"/>),
/// and tells which attachments are still recorded.
/// </summary>
interface IUserDeviceMap
{
    /// <returns>The bind GUID, or <see langword="null"/> if no device is mapped to the user.</returns>
    Guid? GetDeviceGuid(string userName);

    /// <returns>The bind GUIDs that have an attachment record; a missing record means 'usbipd detach' or unbind.</returns>
    IReadOnlySet<Guid> GetAttachedDevices();
}
