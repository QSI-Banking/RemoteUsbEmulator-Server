// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;

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

    /// <summary>
    /// The admin session splits typed lines with System.CommandLine; this is what it relies on.
    /// </summary>
    [TestMethod]
    public void SplitCommandLine()
    {
        CollectionAssert.AreEqual(ExpectedQuoted,
            CommandLineParser.SplitCommandLine(@"client add --certificate ""C:\certs\pc 42.cer""  --name ""Lab PC 42"" ").ToList());
        CollectionAssert.AreEqual(ExpectedEmpty,
            CommandLineParser.SplitCommandLine(@"user add --name """"").ToList());
        Assert.IsEmpty(CommandLineParser.SplitCommandLine("   ").ToList());
    }

    static IConfiguration GroupConfiguration(string? group) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [AdminAuthorization.GroupKey] = group }).Build();

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("QSIUSBD unit test group that does not exist")]
    public void Group_MissingOrUnknown_Refused(string? group)
    {
        Assert.IsFalse(AdminAuthorization.TryGetGroup(GroupConfiguration(group), new TestConsole(), out _, out _));
    }

    [TestMethod]
    public void Group_Found()
    {
        // A group that exists on every Windows machine.
        Assert.IsTrue(AdminAuthorization.TryGetGroup(GroupConfiguration(@"BUILTIN\Users"), new TestConsole(), out var name, out var sid));
        Assert.AreEqual(@"BUILTIN\Users", name);
        Assert.AreEqual("S-1-5-32-545", sid.Value);
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
