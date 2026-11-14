// SPDX-FileCopyrightText: 2025 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.Win32;

namespace UnitTests;

[TestClass]
[DoNotParallelize]
sealed class UsbipdRegistry_Tests
{
    static RegistryKey TestBaseKey = null!;

    const string TestBaseKeyName = @"usbipd-win-tests-can-be-removed";

    [ClassInitialize]
    public static void ClassInitialize(TestContext testContext)
    {
        _ = testContext;

        TestBaseKey = Registry.CurrentUser.CreateSubKey(TestBaseKeyName);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        Registry.CurrentUser.DeleteSubKeyTree(TestBaseKeyName, false);
    }

    string TestRegistryName = null!;
    RegistryKey TestRegistry = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        // NOTE: None of these can be volatile, because UsbipdRegistry needs to be able to create stable subkeys in them.

        TestRegistryName = Guid.NewGuid().ToString("B");
        TestRegistry = TestBaseKey.CreateSubKey(TestRegistryName);
        using var usbipdKey = TestRegistry.CreateSubKey(@"SOFTWARE\usbipd-win");
        usbipdKey.SetValue("APPLICATIONFOLDER", AppDomain.CurrentDomain.BaseDirectory);
#pragma warning disable CS0436 // Type conflicts with imported type
        usbipdKey.SetValue("Version", GitVersionInformation.MajorMinorPatch);
#pragma warning restore CS0436 // Type conflicts with imported type
        using var devicesKey = usbipdKey.CreateSubKey("Devices");
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
    }

    void SetUninstalled()
    {
        TestRegistry.DeleteSubKeyTree(@"SOFTWARE\usbipd-win", false);
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = null;
        TestRegistry?.Dispose();
        TestRegistry = null!;
        TestBaseKey.DeleteSubKeyTree(TestRegistryName, false);
        TestRegistryName = null!;
    }

    [TestMethod]
    public void InstallationFolder()
    {
        Assert.IsNotNull(UsbipdRegistry.Instance.InstallationFolder);
    }

    [TestMethod]
    public void InstallationFolder_NotInstalled()
    {
        SetUninstalled();

        Assert.IsNull(UsbipdRegistry.Instance.InstallationFolder);
    }

    [TestMethod]
    public void Persist()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        Assert.HasCount(2, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_MissingInstanceId()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        // Manually remove the InstanceId value from one of the devices.
        TestRegistry.OpenSubKey($@"SOFTWARE\usbipd-win\Devices\{bound.First().PersistedGuid:B}", true)!.DeleteValue("InstanceId");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.PersistedGuid == bound.First().PersistedGuid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_DuplicateInstanceId()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        // We test case invariance as well.
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"usb\vid_1111&pid_1111\111111", "Test 2");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void GetBoundDevices_MissingDescription()
    {
        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        // Manually remove the InstanceId value from one of the devices.
        TestRegistry.OpenSubKey($@"SOFTWARE\usbipd-win\Devices\{bound.First().PersistedGuid:B}", true)!.DeleteValue("Description");

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.PersistedGuid == bound.First().PersistedGuid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void StopSharingDevice()
    {
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        var bound = UsbipdRegistry.Instance.GetBoundDevices();

        UsbipdRegistry.Instance.StopSharingDevice(bound.First().PersistedGuid!.Value);

        Assert.HasCount(1, UsbipdRegistry.Instance.GetBoundDevices());
        Assert.DoesNotContain(d => d.PersistedGuid == bound.First().PersistedGuid, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void StopSharingAllDevices()
    {
        UsbipdRegistry.Instance.Persist(@"USB\VID_1111&PID_1111\111111", "Test 1");
        UsbipdRegistry.Instance.Persist(@"USB\VID_2222&PID_2222\222222", "Test 2");

        Assert.HasCount(2, UsbipdRegistry.Instance.GetBoundDevices());

        UsbipdRegistry.Instance.StopSharingAllDevices();

        Assert.HasCount(0, UsbipdRegistry.Instance.GetBoundDevices());
    }

    [TestMethod]
    public void HasWriteAccess()
    {
        // The test registry is always writable.
        Assert.IsTrue(UsbipdRegistry.Instance.HasWriteAccess);
    }

    [TestMethod]
    public void CountPolicyRules()
    {
        // Policy rules are no longer supported; earlier versions' rules are only counted for 'usbipd db import-registry'.
        Assert.AreEqual(0, UsbipdRegistry.Instance.CountPolicyRules());
        using (var usbipdKey = TestRegistry.OpenSubKey(@"SOFTWARE\usbipd-win", true)!)
        using (var policyKey = usbipdKey.CreateSubKey("Policy"))
        {
            policyKey.CreateSubKey(Guid.NewGuid().ToString("B")).Dispose();
            policyKey.CreateSubKey(Guid.NewGuid().ToString("B")).Dispose();
        }
        UsbipdRegistry.TestInstance?.Dispose();
        UsbipdRegistry.TestInstance = new(TestRegistry);
        Assert.AreEqual(2, UsbipdRegistry.Instance.CountPolicyRules());
    }

    static readonly Guid TestDeviceGuid = Guid.Parse("{B60EDA76-B612-4337-8270-FB97E53F8376}");
    static readonly Guid OtherDeviceGuid = Guid.Parse("{E863A2AF-AE47-440B-A32B-FAB1C03017AB}");

    [TestMethod]
    public void UserDevice_NoUsersKey()
    {
        Assert.IsNull(UsbipdRegistry.Instance.GetUserDevice("qsi-test"));
        Assert.IsEmpty(UsbipdRegistry.Instance.GetUserDevices());
        Assert.IsFalse(UsbipdRegistry.Instance.RemoveUser("qsi-test"));
        UsbipdRegistry.Instance.RemoveAllUsers();
    }

    [TestMethod]
    public void UserDevice_SetGetCaseInsensitive()
    {
        UsbipdRegistry.Instance.SetUserDevice("qsi-test", TestDeviceGuid);
        Assert.AreEqual(TestDeviceGuid, UsbipdRegistry.Instance.GetUserDevice("qsi-test"));
        Assert.AreEqual(TestDeviceGuid, UsbipdRegistry.Instance.GetUserDevice("QSI-Test"));
    }

    [TestMethod]
    public void UserDevice_Replace()
    {
        UsbipdRegistry.Instance.SetUserDevice("qsi-test", TestDeviceGuid);
        UsbipdRegistry.Instance.SetUserDevice("qsi-test", OtherDeviceGuid);
        Assert.AreEqual(OtherDeviceGuid, UsbipdRegistry.Instance.GetUserDevice("qsi-test"));
        Assert.HasCount(1, UsbipdRegistry.Instance.GetUserDevices());
    }

    [TestMethod]
    public void UserDevice_Remove()
    {
        UsbipdRegistry.Instance.SetUserDevice("alice", TestDeviceGuid);
        UsbipdRegistry.Instance.SetUserDevice("bob", TestDeviceGuid);
        Assert.IsTrue(UsbipdRegistry.Instance.RemoveUser("alice"));
        Assert.IsFalse(UsbipdRegistry.Instance.RemoveUser("alice"));
        Assert.IsNull(UsbipdRegistry.Instance.GetUserDevice("alice"));
        Assert.AreEqual(TestDeviceGuid, UsbipdRegistry.Instance.GetUserDevice("bob"));
    }

    [TestMethod]
    public void UserDevice_RemoveAll()
    {
        UsbipdRegistry.Instance.SetUserDevice("alice", TestDeviceGuid);
        UsbipdRegistry.Instance.SetUserDevice("bob", OtherDeviceGuid);
        Assert.HasCount(2, UsbipdRegistry.Instance.GetUserDevices());
        UsbipdRegistry.Instance.RemoveAllUsers();
        Assert.IsEmpty(UsbipdRegistry.Instance.GetUserDevices());
    }

    [TestMethod]
    public void UserDevice_InvalidName()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
        {
            UsbipdRegistry.Instance.SetUserDevice("bad name", TestDeviceGuid);
        });
        Assert.IsNull(UsbipdRegistry.Instance.GetUserDevice("bad name"));
    }

    [TestMethod]
    public void UserDevice_InvalidGuidIgnored()
    {
        using (var userKey = TestRegistry.CreateSubKey(@"SOFTWARE\usbipd-win\Users\broken"))
        {
            userKey.SetValue("DeviceGuid", "not-a-guid");
        }
        Assert.IsNull(UsbipdRegistry.Instance.GetUserDevice("broken"));
        Assert.IsEmpty(UsbipdRegistry.Instance.GetUserDevices());
    }
}
