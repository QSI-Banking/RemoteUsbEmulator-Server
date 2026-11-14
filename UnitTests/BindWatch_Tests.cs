// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace UnitTests;

[TestClass]
sealed class BindWatch_Tests
{
    const string Fingerprint = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    const string Location = "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(2)";

    [TestMethod]
    public void InterfaceKey_FromRemovalEvent_MatchesInstanceId()
    {
        var fromEvent = BindWatcher.GetInterfaceKey(@"\\?\USB#VID_046D&PID_082D#A5BA06AF#{00873fdf-cafe-80ee-aa5e-00c04fb1720b}");
        Assert.AreEqual(Usbipd.Data.BindStore.GetInterfaceKey(@"USB\VID_046D&PID_082D\A5BA06AF"), fromEvent);
    }

    [TestMethod]
    public void InterfaceKey_PortIdentifiedDevice_CaseInsensitive()
    {
        var fromEvent = BindWatcher.GetInterfaceKey(@"\\?\usb#vid_0529&pid_0001#5&1a2b3c&0&2#{00873fdf-cafe-80ee-aa5e-00c04fb1720b}");
        Assert.AreEqual(Usbipd.Data.BindStore.GetInterfaceKey(@"USB\VID_0529&PID_0001\5&1A2B3C&0&2"), fromEvent);
    }

    [TestMethod]
    public void InterfaceKey_Malformed()
    {
        Assert.IsNull(BindWatcher.GetInterfaceKey("not an interface path"));
        Assert.IsNull(BindWatcher.GetInterfaceKey(@"\\?\#{00873fdf-cafe-80ee-aa5e-00c04fb1720b}"));
    }

    [TestMethod]
    public void Identity_Match()
    {
        var identity = new DeviceIdentity(Fingerprint, Location, "046d:082d", "A5BA06AF", "0e Video");
        Assert.IsNull(identity.GetMismatch(Fingerprint.ToLowerInvariant(), Location));
    }

    [TestMethod]
    public void Identity_OtherPort()
    {
        var identity = new DeviceIdentity(Fingerprint, Location, "046d:082d", null, "0e Video");
        Assert.AreEqual("the device is in another port than when it was bound",
            identity.GetMismatch(Fingerprint, "PCIROOT(0)#PCI(1400)#USBROOT(0)#USB(3)"));
    }

    [TestMethod]
    public void Identity_OtherDevice()
    {
        var identity = new DeviceIdentity(Fingerprint, Location, "046d:082d", null, "03 HID (keyboard, mouse, ...)");
        Assert.AreEqual("the device is not the one that was bound (its USB descriptors differ)",
            identity.GetMismatch(new string('F', 64), Location));
    }

    [TestMethod]
    public void Identity_NothingRecorded()
    {
        var identity = new DeviceIdentity(Fingerprint, Location, "046d:082d", null, "0e Video");
        Assert.AreEqual("no identity was recorded for the bind", identity.GetMismatch(null, null));
    }

    [TestMethod]
    public void Identity_DescribeInterfaces()
    {
        Assert.AreEqual("03 HID (keyboard, mouse, ...), 08 Mass storage", DeviceIdentity.DescribeInterfaces([0x08, 0x03]));
        Assert.AreEqual("none", DeviceIdentity.DescribeInterfaces([]));
    }
}
