// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;

namespace UnitTests;

[TestClass]
sealed class Parse_user_Tests
    : ParseTestBase
{
    static readonly BusId TestBusId = BusId.Parse("3-42");
    static readonly Guid TestGuid = Guid.Parse("{B60EDA76-B612-4337-8270-FB97E53F8376}");
    const string TestUser = "qsi-test";

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "user", "--help");
    }

    [TestMethod]
    public void CommandMissing()
    {
        Test(ExitCode.ParseError, "user");
    }

    [TestMethod]
    public void CommandInvalid()
    {
        Test(ExitCode.ParseError, "user", "invalid-command");
    }

    [TestMethod]
    public void Add_GuidSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserAdd(TestUser, TestGuid, null, false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "add", "--name", TestUser, "--guid", TestGuid.ToString());
    }

    [TestMethod]
    public void Add_BusIdSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserAdd(TestUser, null, TestBusId, false,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "add", "--name", TestUser, "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void Add_ForceSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserAdd(TestUser, null, TestBusId, true,
            It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "add", "--name", TestUser, "--busid", TestBusId.ToString(), "--force");
    }

    [TestMethod]
    public void Add_NameMissing()
    {
        Test(ExitCode.ParseError, "user", "add", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void Add_NameInvalid()
    {
        Test(ExitCode.ParseError, "user", "add", "--name", "bad name!", "--busid", TestBusId.ToString());
    }

    [TestMethod]
    public void Add_DeviceMissing()
    {
        Test(ExitCode.ParseError, "user", "add", "--name", TestUser);
    }

    [TestMethod]
    public void Add_BothDevices()
    {
        Test(ExitCode.ParseError, "user", "add", "--name", TestUser, "--busid", TestBusId.ToString(), "--guid", TestGuid.ToString());
    }

    [TestMethod]
    public void List_Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserList(It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "list");
    }

    [TestMethod]
    public void List_StrayArgument()
    {
        Test(ExitCode.ParseError, "user", "list", "stray-argument");
    }

    [TestMethod]
    public void Remove_NameSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserRemove(TestUser, It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "remove", "--name", TestUser);
    }

    [TestMethod]
    public void Remove_AllSuccess()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserRemoveAll(It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "remove", "--all");
    }

    [TestMethod]
    public void Remove_NoneSpecified()
    {
        Test(ExitCode.ParseError, "user", "remove");
    }

    [TestMethod]
    public void Remove_Both()
    {
        Test(ExitCode.ParseError, "user", "remove", "--all", "--name", TestUser);
    }

    [TestMethod]
    public void OtpReset_Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.UserOtpReset(TestUser, It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "user", "otp", "reset", "--name", TestUser);
    }

    [TestMethod]
    public void OtpReset_NameMissing()
    {
        Test(ExitCode.ParseError, "user", "otp", "reset");
    }

    [TestMethod]
    public void OtpReset_NameInvalid()
    {
        Test(ExitCode.ParseError, "user", "otp", "reset", "--name", "has space");
    }

    [TestMethod]
    public void Otp_CommandMissing()
    {
        Test(ExitCode.ParseError, "user", "otp");
    }
}
