// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Win32;
using Windows.Win32.Devices.Usb;
using static Usbipd.Interop.WinSDK;
using static Usbipd.Tools;

namespace Usbipd;

/// <summary>
/// What a bind records about the device, and what is compared before the device is shared again:
/// where it is plugged in, what it says it is, and a fingerprint of its USB descriptors.
/// <para>
/// A device can lie about all of this (a programmable device can copy descriptors and serial numbers), so a match does not
/// prove it is the same hardware. It does catch a different device in a bound port, and a device whose interfaces changed.
/// </para>
/// </summary>
/// <param name="Fingerprint">SHA-256 (hex) of the device descriptor and all configuration descriptors.</param>
/// <param name="LocationPath">See <see cref="WindowsDevice.LocationPath"/>.</param>
/// <param name="HardwareId">VID:PID.</param>
/// <param name="SerialNumber">See <see cref="WindowsDevice.SerialNumber"/>.</param>
/// <param name="Interfaces">The interface classes, for administrators (for example "03 HID, 08 Mass storage").</param>
sealed record DeviceIdentity(string Fingerprint, string LocationPath, string HardwareId, string? SerialNumber, string Interfaces)
{
    const int MaxConfigurations = 8;

    /// <summary>
    /// Reads the identity of a present device from its hub; works for any driver, also while the device is not claimed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The identity cannot be read (not present, hub not supported, or the device does not answer).</exception>
    public static async Task<DeviceIdentity> ReadAsync(WindowsDevice device)
    {
        if (!device.IsPresent)
        {
            throw new InvalidOperationException("the device is not present");
        }
        var busId = device.BusId;
        if (busId.IsIncompatibleHub)
        {
            throw new InvalidOperationException("the device is on a hub that is not supported");
        }
        var locationPath = device.LocationPath ?? throw new InvalidOperationException("Windows does not report where the device is plugged in");
        try
        {
            using var hubFile = device.OpenHubInterface();

            var info = new USB_NODE_CONNECTION_INFORMATION_EX() { ConnectionIndex = busId.Port };
            var infoBuffer = StructToBytes(info);
            _ = await hubFile.IoControlAsync(PInvoke.IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, infoBuffer, infoBuffer);
            BytesToStruct(infoBuffer, out info);
            var deviceDescriptor = StructToBytes(info.DeviceDescriptor);
            var configurations = Math.Min((int)info.DeviceDescriptor.bNumConfigurations, MaxConfigurations);
            if (configurations == 0)
            {
                throw new InvalidOperationException("the device reports no configuration");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendWithLength(hash, deviceDescriptor);
            var interfaceClasses = new List<byte>();
            for (var index = 0; index < configurations; ++index)
            {
                var configuration = await GetConfigurationDescriptorAsync(hubFile, busId.Port, (byte)index);
                AppendWithLength(hash, configuration);
                AddInterfaceClasses(configuration, interfaceClasses);
            }
            var hardwareId = string.Create(CultureInfo.InvariantCulture,
                $"{info.DeviceDescriptor.idVendor:x4}:{info.DeviceDescriptor.idProduct:x4}");
            return new(Convert.ToHexString(hash.GetHashAndReset()), locationPath, hardwareId, device.SerialNumber,
                DescribeInterfaces(interfaceClasses));
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or IOException)
        {
            throw new InvalidOperationException($"the device descriptors cannot be read ({ex.Message})", ex);
        }
    }

    /// <summary>
    /// Why this (current) identity is not the recorded one, or <see langword="null"/> if it is.
    /// </summary>
    public string? GetMismatch(string? recordedFingerprint, string? recordedLocationPath)
    {
        return recordedFingerprint is null || recordedLocationPath is null ? "no identity was recorded for the bind"
            : !string.Equals(LocationPath, recordedLocationPath, StringComparison.OrdinalIgnoreCase) ? "the device is in another port than when it was bound"
            : !string.Equals(Fingerprint, recordedFingerprint, StringComparison.OrdinalIgnoreCase) ? "the device is not the one that was bound (its USB descriptors differ)"
            : null;
    }

    static void AppendWithLength(IncrementalHash hash, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        hash.AppendData(length);
        hash.AppendData(data);
    }

    /// <returns>The whole configuration descriptor (with its interface and endpoint descriptors).</returns>
    static async Task<byte[]> GetConfigurationDescriptorAsync(DeviceFile hub, ushort connectionIndex, byte configurationIndex)
    {
        var requestSize = Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>();
        var wValue = (ushort)((PInvoke.USB_CONFIGURATION_DESCRIPTOR_TYPE << 8) | configurationIndex);
        ushort totalLength;
        {
            var buffer = new byte[requestSize + Unsafe.SizeOf<USB_CONFIGURATION_DESCRIPTOR>()];
            SetRequest(buffer, connectionIndex, wValue);
            _ = await hub.IoControlAsync(PInvoke.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buffer, buffer);
            totalLength = MemoryMarshal.AsRef<USB_CONFIGURATION_DESCRIPTOR>(buffer.AsSpan(requestSize)).wTotalLength;
        }
        if (totalLength < Unsafe.SizeOf<USB_CONFIGURATION_DESCRIPTOR>())
        {
            throw new InvalidOperationException("the device reports a broken configuration descriptor");
        }
        {
            var buffer = new byte[requestSize + totalLength];
            SetRequest(buffer, connectionIndex, wValue);
            _ = await hub.IoControlAsync(PInvoke.IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buffer, buffer);
            return buffer[requestSize..];
        }
    }

    static void SetRequest(byte[] buffer, ushort connectionIndex, ushort wValue)
    {
        ref var request = ref MemoryMarshal.AsRef<USB_DESCRIPTOR_REQUEST>(buffer.AsSpan(0, Unsafe.SizeOf<USB_DESCRIPTOR_REQUEST>()));
        // All other fields are ignored on input (they are implied by the IOCTL code).
        request.ConnectionIndex = connectionIndex;
        request.SetupPacket.wValue = wValue;
    }

    static void AddInterfaceClasses(ReadOnlySpan<byte> configuration, List<byte> interfaceClasses)
    {
        var offset = 0;
        while (configuration.Length - offset >= Unsafe.SizeOf<USB_COMMON_DESCRIPTOR>())
        {
            ref readonly var common = ref MemoryMarshal.AsRef<USB_COMMON_DESCRIPTOR>(configuration[offset..]);
            if (common.bLength < Unsafe.SizeOf<USB_COMMON_DESCRIPTOR>() || common.bLength > configuration.Length - offset)
            {
                // Broken configuration.
                break;
            }
            if (common.bDescriptorType == PInvoke.USB_INTERFACE_DESCRIPTOR_TYPE && common.bLength >= Unsafe.SizeOf<USB_INTERFACE_DESCRIPTOR>())
            {
                ref readonly var interfaceDescriptor = ref MemoryMarshal.AsRef<USB_INTERFACE_DESCRIPTOR>(configuration[offset..]);
                if (interfaceDescriptor.bAlternateSetting == 0 && !interfaceClasses.Contains(interfaceDescriptor.bInterfaceClass))
                {
                    interfaceClasses.Add(interfaceDescriptor.bInterfaceClass);
                }
            }
            offset += common.bLength;
        }
    }

    internal static string DescribeInterfaces(IEnumerable<byte> interfaceClasses)
    {
        var text = string.Join(", ", interfaceClasses.Order().Select(c => string.Create(CultureInfo.InvariantCulture, $"{c:x2} {ClassName(c)}")));
        return text.Length == 0 ? "none" : text;
    }

    static string ClassName(byte interfaceClass)
    {
        return interfaceClass switch
        {
            0x01 => "Audio",
            0x02 => "Communications",
            0x03 => "HID (keyboard, mouse, ...)",
            0x05 => "Physical",
            0x06 => "Image",
            0x07 => "Printer",
            0x08 => "Mass storage",
            0x09 => "Hub",
            0x0a => "CDC data",
            0x0b => "Smart card",
            0x0d => "Content security",
            0x0e => "Video",
            0x0f => "Personal healthcare",
            0x10 => "Audio/video",
            0xdc => "Diagnostic",
            0xe0 => "Wireless",
            0xef => "Miscellaneous",
            0xfe => "Application specific",
            0xff => "Vendor specific",
            _ => "Unknown",
        };
    }
}
