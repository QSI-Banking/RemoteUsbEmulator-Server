// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class Parse_admin_Tests
    : ParseTestBase
{
    [TestMethod]
    public void Success()
    {
        var mock = CreateMock();
        mock.Setup(m => m.Admin(It.IsNotNull<IConsole>(), It.IsAny<CancellationToken>())).Returns(Task.FromResult(ExitCode.Success));

        Test(ExitCode.Success, mock, "admin");
    }

    [TestMethod]
    public void Help()
    {
        Test(ExitCode.Success, "admin", "--help");
    }

    [TestMethod]
    public void OptionInvalid()
    {
        Test(ExitCode.ParseError, "admin", "--invalid");
    }

    static readonly string[] ExpectedQuoted = ["client", "add", "--certificate", @"C:\certs\pc 42.cer", "--name", "Lab PC 42"];
    static readonly string[] ExpectedEmpty = ["user", "add", "--name", ""];

    [TestMethod]
    public void SplitCommandLine()
    {
        CollectionAssert.AreEqual(ExpectedQuoted,
            CommandHandlers.SplitCommandLine(@"client add --certificate ""C:\certs\pc 42.cer""  --name ""Lab PC 42"" "));
        CollectionAssert.AreEqual(ExpectedEmpty,
            CommandHandlers.SplitCommandLine(@"user add --name """""));
        Assert.IsEmpty(CommandHandlers.SplitCommandLine("   "));
    }

    [TestMethod]
    public void SplitUserName()
    {
        Assert.AreEqual(("peter", "QSI"), AdminAuthorization.SplitUserName(@"QSI\peter", string.Empty));
        Assert.AreEqual(("peter", "."), AdminAuthorization.SplitUserName(@".\peter", string.Empty));
        Assert.AreEqual(("peter", "."), AdminAuthorization.SplitUserName("peter", string.Empty));
        Assert.AreEqual(("peter@qsi.example", string.Empty), AdminAuthorization.SplitUserName("peter@qsi.example", string.Empty));
        Assert.AreEqual(("peter", "QSI"), AdminAuthorization.SplitUserName("peter", "QSI"));
    }
}
