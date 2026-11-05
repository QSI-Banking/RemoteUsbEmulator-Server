// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Verifies a user name and one-time password sent by the client in OP_REQ_AUTH.
/// </summary>
interface IUserAuthenticator
{
    /// <returns><see langword="true"/> if the credentials are valid.</returns>
    bool Authenticate(string userName, string otp);
}
