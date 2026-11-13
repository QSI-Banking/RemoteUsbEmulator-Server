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

    // RFC 6238 appendix B (SHA-1); the last 6 of the 8 digits there.
    static readonly byte[] RfcSecret = "12345678901234567890"u8.ToArray();

    [TestMethod]
    [DataRow(59L, "287082")]
    [DataRow(1111111109L, "081804")]
    [DataRow(1111111111L, "050471")]
    [DataRow(1234567890L, "005924")]
    [DataRow(2000000000L, "279037")]
    [DataRow(20000000000L, "353130")]
    public void Totp_Rfc6238(long unixSeconds, string expected)
    {
        var step = Totp.GetStep(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.AreEqual(expected, Totp.Compute(RfcSecret, step));
    }

    [TestMethod]
    public void Totp_Verify_AcceptsDriftOfOneStep()
    {
        const long now = 1000;
        Assert.AreEqual(999L, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, 999), now, 0));
        Assert.AreEqual(1000L, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, 1000), now, 0));
        Assert.AreEqual(1001L, Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, 1001), now, 0));
        Assert.IsNull(Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, 998), now, 0));
        Assert.IsNull(Totp.Verify(RfcSecret, Totp.Compute(RfcSecret, 1002), now, 0));
    }

    [TestMethod]
    public void Totp_Verify_RefusesReusedCode()
    {
        const long now = 1000;
        var code = Totp.Compute(RfcSecret, now);
        Assert.AreEqual(now, Totp.Verify(RfcSecret, code, now, now - 1));
        // The step was accepted (recorded as last accepted): the same code is refused.
        Assert.IsNull(Totp.Verify(RfcSecret, code, now, now));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("12345")]
    [DataRow("1234567")]
    [DataRow("12a456")]
    [DataRow("١٢٣٤٥٦")]
    public void Totp_Verify_RefusesMalformed(string code)
    {
        Assert.IsNull(Totp.Verify(RfcSecret, code, 1000, 0));
    }

    [TestMethod]
    public void Totp_Base32()
    {
        // RFC 4648 test vectors (without padding).
        Assert.AreEqual("", Totp.ToBase32(""u8));
        Assert.AreEqual("MY", Totp.ToBase32("f"u8));
        Assert.AreEqual("MZXQ", Totp.ToBase32("fo"u8));
        Assert.AreEqual("MZXW6", Totp.ToBase32("foo"u8));
        Assert.AreEqual("MZXW6YQ", Totp.ToBase32("foob"u8));
        Assert.AreEqual("MZXW6YTB", Totp.ToBase32("fooba"u8));
        Assert.AreEqual("MZXW6YTBOI", Totp.ToBase32("foobar"u8));
        Assert.AreEqual("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(RfcSecret));
    }

    [TestMethod]
    public void Totp_KeyUri()
    {
        Assert.AreEqual(
            "otpauth://totp/QSI%20USB%2FIP%3Aalice?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=QSI%20USB%2FIP&algorithm=SHA1&digits=6&period=30",
            Totp.GetKeyUri("QSI USB/IP", "alice", RfcSecret));
    }

    [TestMethod]
    public void Totp_CreateSecret()
    {
        var secret = Totp.CreateSecret();
        Assert.HasCount(Totp.SecretLength, secret);
        Assert.IsFalse(secret.SequenceEqual(Totp.CreateSecret()));
    }
}
