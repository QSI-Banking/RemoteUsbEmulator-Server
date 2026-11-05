// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics.CodeAnalysis;

namespace Usbipd;

/// <summary>
/// Rules for user names used by `usbipd user` and by OP_REQ_AUTH.
/// </summary>
static class UserNames
{
    /// <summary>
    /// Same as <see cref="Interop.UsbIp.USBIP_MAX_USER"/>.
    /// </summary>
    public const int MaxLength = 63;

    public const string RulesText = "1 to 63 characters from A-Z, a-z, 0-9, '.', '_' and '-'";

    /// <summary>
    /// User names are compared case-insensitively (registry key names are case-insensitive as well).
    /// </summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static bool IsValid([NotNullWhen(true)] string? userName)
    {
        if (string.IsNullOrEmpty(userName) || userName.Length > MaxLength)
        {
            return false;
        }
        foreach (var c in userName)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            {
                return false;
            }
        }
        return true;
    }
}
