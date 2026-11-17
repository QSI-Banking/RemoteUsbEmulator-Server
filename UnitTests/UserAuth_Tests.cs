// SPDX-FileCopyrightText: 2026 QSI Banking
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

    static readonly byte[] Secret = "12345678901234567890"u8.ToArray();

    /// <summary>
    /// The time at the start of a 30 second step, and the code for it (computed by Otp.NET).
    /// </summary>
    static (DateTime Time, string Code) At(long step)
    {
        var time = DateTime.UnixEpoch.AddSeconds(step * 30);
        return (time, new OtpNet.Totp(Secret).ComputeTotp(time));
    }

    [TestMethod]
    public void Otp_Verify_AcceptsOneStepEitherWay()
    {
        var now = At(1000).Time;
        Assert.AreEqual(999L, Otp.Verify(Secret, At(999).Code, now, 0));
        Assert.AreEqual(1000L, Otp.Verify(Secret, At(1000).Code, now, 0));
        Assert.AreEqual(1001L, Otp.Verify(Secret, At(1001).Code, now, 0));
        Assert.IsNull(Otp.Verify(Secret, At(998).Code, now, 0));
        Assert.IsNull(Otp.Verify(Secret, At(1002).Code, now, 0));
    }

    [TestMethod]
    public void Otp_Verify_RefusesReusedCode()
    {
        var (now, code) = At(1000);
        Assert.AreEqual(1000L, Otp.Verify(Secret, code, now, 999));
        // Once step 1000 is recorded as the last accepted one, the same code is refused.
        Assert.IsNull(Otp.Verify(Secret, code, now, 1000));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("12345")]
    [DataRow("1234567")]
    [DataRow("12a456")]
    [DataRow("\u0661\u0662\u0663\u0664\u0665\u0666")] // Arabic-Indic digits
    public void Otp_Verify_RefusesMalformed(string code)
    {
        Assert.IsNull(Otp.Verify(Secret, code, At(1000).Time, 0));
    }

    [TestMethod]
    public void Otp_KeyUri()
    {
        var uri = new Uri(Otp.GetKeyUri("QSI USB/IP", "alice", Secret));
        Assert.AreEqual("otpauth", uri.Scheme);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri.Query);
        Assert.Contains("alice", uri.AbsolutePath);
    }

    [TestMethod]
    public void Otp_CreateSecret()
    {
        var secret = Otp.CreateSecret();
        Assert.HasCount(20, secret);
        Assert.IsFalse(secret.SequenceEqual(Otp.CreateSecret()));
    }
}
