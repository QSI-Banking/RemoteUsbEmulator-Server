// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Usbipd.Data;

namespace UnitTests;

/// <summary>
/// The ConfigEncrypt library finds the certificate in a store when decrypting, so these tests add a temporary
/// certificate to CurrentUser\My and remove it (and its key) afterwards.
/// </summary>
[TestClass]
sealed class Cryptography_Tests
{
    static readonly byte[] Secret = "12345678901234567890"u8.ToArray();

    sealed class StoredCertificate : IDisposable
    {
        public X509Certificate2 Certificate { get; }

        public StoredCertificate()
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=usbipd-unit-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            Certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(Certificate);
        }

        public void Dispose()
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadWrite);
                store.Remove(Certificate);
            }
            using (var rsa = Certificate.GetRSAPrivateKey())
            {
                if (rsa is RSACng cng)
                {
                    cng.Key.Delete();
                }
            }
            Certificate.Dispose();
        }
    }

    static string Encrypt(int userId, X509Certificate2 certificate) =>
        Cryptography.Encrypt(userId, Secret, certificate, StoreLocation.CurrentUser);

    [TestMethod]
    public void RoundTrip()
    {
        using var stored = new StoredCertificate();
        var payload = Encrypt(42, stored.Certificate);
        CollectionAssert.AreEqual(Secret, Cryptography.Decrypt(42, payload));
    }

    [TestMethod]
    public void Payload_IsConfigEncryptFormat()
    {
        using var stored = new StoredCertificate();
        var payload = Encrypt(1, stored.Certificate);
        Assert.StartsWith("enc:", payload);
        var bytes = Convert.FromBase64String(payload["enc:".Length..]);
        Assert.AreEqual(2, bytes[0]); // CryptographyVersion.RsaCertificateKeyPair
        Assert.AreEqual((byte)StoreName.My, bytes[1]);
        Assert.AreEqual((byte)StoreLocation.CurrentUser, bytes[2]);
        Assert.AreEqual(20, BitConverter.ToInt32(bytes, 3));
        CollectionAssert.AreEqual(stored.Certificate.GetCertHash(), bytes[7..27]);
    }

    [TestMethod]
    public void OtherUser_Refused()
    {
        using var stored = new StoredCertificate();
        var payload = Encrypt(42, stored.Certificate);
        _ = Assert.ThrowsExactly<CryptographicException>(() => Cryptography.Decrypt(43, payload));
    }

    [TestMethod]
    public void Tampered_Refused()
    {
        using var stored = new StoredCertificate();
        var bytes = Convert.FromBase64String(Encrypt(42, stored.Certificate)["enc:".Length..]);
        bytes[^1] ^= 1; // last ciphertext byte
        // AuthenticationTagMismatchException, a CryptographicException (which is what callers catch)
        _ = Assert.Throws<CryptographicException>(() => Cryptography.Decrypt(42, "enc:" + Convert.ToBase64String(bytes)));
    }

    [TestMethod]
    public void MissingCertificate_Refused()
    {
        string payload;
        using (var stored = new StoredCertificate())
        {
            payload = Encrypt(42, stored.Certificate);
        }
        _ = Assert.ThrowsExactly<CryptographicException>(() => Cryptography.Decrypt(42, payload));
    }

    [TestMethod]
    public void NonRsaCertificate_Refused()
    {
        using var ecdsa = ECDsa.Create();
        var request = new CertificateRequest("CN=usbipd-unit-test", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => Encrypt(42, certificate));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("enc:")]
    [DataRow("not base64!")]
    [DataRow("AQ==")] // version 1 (password-based) is not supported
    [DataRow("enc:Ag==")] // version 2, truncated
    public void Malformed_Refused(string payload)
    {
        _ = Assert.ThrowsExactly<CryptographicException>(() => Cryptography.Decrypt(42, payload));
    }
}
