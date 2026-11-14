// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using static Usbipd.Interop.VBoxUsb;
using static Usbipd.Interop.VBoxUsbMon;

namespace Usbipd;

/// <summary>
/// The VBoxUSBMon driver is installed because the VBoxUSB device driver needs it; this server only checks that it runs.
/// Capturing devices on demand through its filters is not used: every shared device is bound to VBoxUSB by an administrator.
/// </summary>
sealed partial class VBoxUsbMon : IDisposable
{
    readonly DeviceFile UsbMonitor = new(USBMON_DEVICE_NAME);

    public static UsbSupVersion? GetRunningVersion()
    {
        try
        {
            using var mon = new VBoxUsbMon();
            return mon.GetVersion().Result;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    public static bool IsServiceInstalled()
    {
        return ServiceController.GetDevices().Any(sc => sc.ServiceName.Equals(USBMON_SERVICE_NAME_W, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsVersionSupported(UsbSupVersion version)
    {
        return (version.major == USBMON_MAJOR_VERSION) && (version.minor >= USBMON_MINOR_VERSION);
    }

    public async Task<UsbSupVersion> GetVersion()
    {
        var output = new byte[Unsafe.SizeOf<UsbSupVersion>()];
        _ = await UsbMonitor.IoControlAsync(SUPUSBFLT_IOCTL.GET_VERSION, null, output);
        return MemoryMarshal.AsRef<UsbSupVersion>(output);
    }

    bool IsDisposed;
    public void Dispose()
    {
        if (!IsDisposed)
        {
            UsbMonitor.Dispose();
            IsDisposed = true;
        }
    }
}
