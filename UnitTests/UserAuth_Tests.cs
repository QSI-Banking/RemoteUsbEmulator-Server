// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class UserAuth_Tests
{
    [TestMethod]
    [DataRow("qsi-test")]
    [DataRow("A")]
    [DataRow("user.name_01-x")]
    public void UserNames_Valid(string userName)
    {
        Assert.IsTrue(UserNames.IsValid(userName));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("has space")]
    [DataRow("back\\slash")]
    [DataRow("ümlaut")]
    public void UserNames_Invalid(string? userName)
    {
        Assert.IsFalse(UserNames.IsValid(userName));
    }

    [TestMethod]
    public void UserNames_TooLong()
    {
        Assert.IsTrue(UserNames.IsValid(new string('a', UserNames.MaxLength)));
        Assert.IsFalse(UserNames.IsValid(new string('a', UserNames.MaxLength + 1)));
    }

    [TestMethod]
    public void Hardcoded_Success()
    {
        var authenticator = new HardcodedUserAuthenticator();
        Assert.IsTrue(authenticator.Authenticate("qsi-test", "123456"));
        Assert.IsTrue(authenticator.Authenticate("QSI-TEST", "123456"));
    }

    [TestMethod]
    [DataRow("qsi-test", "123457")]
    [DataRow("qsi-test", "12345")]
    [DataRow("qsi-test", "")]
    [DataRow("other", "123456")]
    public void Hardcoded_Failure(string userName, string otp)
    {
        var authenticator = new HardcodedUserAuthenticator();
        Assert.IsFalse(authenticator.Authenticate(userName, otp));
    }
}
