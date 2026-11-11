// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace UnitTests;

[TestClass]
sealed class TlsSettings_Tests
{
    const string ServerThumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";
    const string CaThumbprint = "89ABCDEF0123456789ABCDEF0123456789ABCDEF";
    const string OtherThumbprint = "FEDCBA9876543210FEDCBA9876543210FEDCBA98";

    static IConfiguration CreateConfiguration(params (string Key, string Value)[] values)
    {
        var data = new Dictionary<string, string?>
        {
            { "usbipd:Tls:ServerCertificateThumbprint", ServerThumbprint },
            { "usbipd:Tls:ClientCaThumbprints:0", CaThumbprint },
        };
        foreach (var (key, value) in values)
        {
            data[key] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [TestMethod]
    public void Read_Defaults()
    {
        var settings = TlsSettings.Read(CreateConfiguration());

        Assert.AreEqual(TlsSettings.DefaultPort, settings.Port);
        Assert.IsNull(settings.ListenAddress);
        Assert.AreEqual(ServerThumbprint, settings.ServerCertificateThumbprint);
        Assert.HasCount(1, settings.ClientCaThumbprints);
        Assert.IsTrue(settings.ClientCaThumbprints.Contains(CaThumbprint));
        Assert.IsEmpty(settings.AllowedClientThumbprints);
        Assert.AreEqual(TlsSettings.DefaultHandshakeTimeout, settings.HandshakeTimeout);
        Assert.AreEqual(X509RevocationMode.NoCheck, settings.RevocationMode);
    }

    [TestMethod]
    public void Read_AllValues()
    {
        var settings = TlsSettings.Read(CreateConfiguration(
            ("usbipd:Tls:Port", "4443"),
            ("usbipd:Tls:ListenAddress", "192.168.1.10"),
            ("usbipd:Tls:ClientCaThumbprints:1", OtherThumbprint),
            ("usbipd:Tls:AllowedClientThumbprints:0", OtherThumbprint),
            ("usbipd:Tls:HandshakeTimeoutSeconds", "30"),
            ("usbipd:Tls:RevocationMode", "online")));

        Assert.AreEqual(4443, settings.Port);
        Assert.AreEqual(IPAddress.Parse("192.168.1.10"), settings.ListenAddress);
        Assert.HasCount(2, settings.ClientCaThumbprints);
        Assert.IsTrue(settings.AllowedClientThumbprints.Contains(OtherThumbprint));
        Assert.AreEqual(TimeSpan.FromSeconds(30), settings.HandshakeTimeout);
        Assert.AreEqual(X509RevocationMode.Online, settings.RevocationMode);
    }

    [TestMethod]
    public void Read_CaListAsString()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "usbipd:Tls:ServerCertificateThumbprint", ServerThumbprint },
            { "usbipd:Tls:ClientCaThumbprints", $"{CaThumbprint}, {OtherThumbprint}" },
        }).Build();

        var settings = TlsSettings.Read(configuration);

        Assert.HasCount(2, settings.ClientCaThumbprints);
    }

    [TestMethod]
    public void NormalizeThumbprint_CopiedFromCertificateDialog()
    {
        // The Windows certificate dialog prefixes an invisible left-to-right mark and uses lower case with spaces.
        var copied = "‎01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01:23:45:67";

        Assert.AreEqual(ServerThumbprint, TlsSettings.NormalizeThumbprint(copied, "Test"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0123")]
    [DataRow("0123456789ABCDEF0123456789ABCDEF0123456G")]
    [DataRow("0123456789ABCDEF0123456789ABCDEF0123456789")]
    public void NormalizeThumbprint_Invalid(string value)
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => TlsSettings.NormalizeThumbprint(value, "Test"));
    }

    [TestMethod]
    public void Read_ServerThumbprintMissing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "usbipd:Tls:ClientCaThumbprints:0", CaThumbprint },
        }).Build();

        _ = Assert.ThrowsExactly<InvalidOperationException>(() => TlsSettings.Read(configuration));
    }

    [TestMethod]
    public void Read_ClientCaMissing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "usbipd:Tls:ServerCertificateThumbprint", ServerThumbprint },
        }).Build();

        _ = Assert.ThrowsExactly<InvalidOperationException>(() => TlsSettings.Read(configuration));
    }

    [TestMethod]
    [DataRow("usbipd:Tls:Port", "0")]
    [DataRow("usbipd:Tls:Port", "65536")]
    [DataRow("usbipd:Tls:Port", "abc")]
    [DataRow("usbipd:Tls:ListenAddress", "not-an-address")]
    [DataRow("usbipd:Tls:HandshakeTimeoutSeconds", "0")]
    [DataRow("usbipd:Tls:HandshakeTimeoutSeconds", "301")]
    [DataRow("usbipd:Tls:RevocationMode", "Sometimes")]
    [DataRow("usbipd:Tls:RevocationMode", "1")]
    public void Read_InvalidValue(string key, string value)
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => TlsSettings.Read(CreateConfiguration((key, value))));
    }
}
