// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_client_Tests
    : ParseTestBase
{
    const string TestThumbprint = "C9AFC9A5AABA73F34694634DD4E26D2D6D6F5CB1";
    const string TestName = "usbip-client";

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "client", "--help");
    }

    [TestMethod]
    public void CommandMissing()
    {
        Test(ExitCode.ParseError, "client");
    }

    [TestMethod]
    public void Add_ThumbprintSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientAdd(TestThumbprint, null, TestName,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        // The invisible mark the certificate dialog puts in front of a copied thumbprint is removed.
        Test(ExitCode.Success, mock, "client", "add", "--thumbprint", $"‎{TestThumbprint}", "--name", TestName);
    }

    [TestMethod]
    public void Add_CertificateSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientAdd(null, "client.cer", TestName,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "client", "add", "--certificate", "client.cer", "--name", TestName);
    }

    [TestMethod]
    public void Add_BothFails()
    {
        Test(ExitCode.ParseError, "client", "add", "--thumbprint", TestThumbprint, "--certificate", "client.cer", "--name", TestName);
    }

    [TestMethod]
    public void Add_NeitherFails()
    {
        Test(ExitCode.ParseError, "client", "add", "--name", TestName);
    }

    [TestMethod]
    public void Add_NameMissing()
    {
        Test(ExitCode.ParseError, "client", "add", "--thumbprint", TestThumbprint);
    }


    [TestMethod]
    public void ListSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientList(It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "client", "list");
    }

    [TestMethod]
    public void DisableSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientDisable(TestThumbprint, It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "client", "disable", "--thumbprint", TestThumbprint);
    }

    [TestMethod]
    public void EnableSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientEnable(TestThumbprint, It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "client", "enable", "--thumbprint", TestThumbprint);
    }

    [TestMethod]
    public void RemoveSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.ClientRemove(TestThumbprint, It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "client", "remove", "--thumbprint", TestThumbprint);
    }

    [TestMethod]
    public void DisableThumbprintMissing()
    {
        Test(ExitCode.ParseError, "client", "disable");
    }
}
