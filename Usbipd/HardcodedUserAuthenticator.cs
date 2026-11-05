// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;

namespace Usbipd;

/// <summary>
/// TEMPORARY: a single hardcoded test user. Replace with persistent credential storage
/// (and single-use OTP enforcement) by registering another <see cref="IUserAuthenticator"/>.
/// </summary>
sealed class HardcodedUserAuthenticator : IUserAuthenticator
{
    public const string UserName = "qsi-test";
    const string Otp = "123456";

    public bool Authenticate(string userName, string otp)
    {
        var userOk = UserNames.Comparer.Equals(userName, UserName);
        var otpOk = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(otp), Encoding.UTF8.GetBytes(Otp));
        // Non-short-circuiting, so both checks always run.
        return userOk & otpOk;
    }
}
