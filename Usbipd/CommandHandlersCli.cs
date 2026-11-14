// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Usbipd.Automation;
using static Usbipd.ConsoleTools;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    static List<Device> GetDevicesByHardwareId(VidPid vidPid, bool connectedOnly, IConsole console)
    {
        if (!CheckNoStub(vidPid, console))
        {
            return [];
        }
        var filtered = new List<Device>();
        var devices = DeviceExtensions.GetAll().Where(d => (d.HardwareId == vidPid) && (!connectedOnly || d.BusId.HasValue));
        foreach (var device in devices)
        {
            if (device.BusId.HasValue)
            {
                if (device.BusId.Value.IsIncompatibleHub)
                {
                    console.ReportWarning($"Ignoring device with hardware-id '{vidPid}' connected to an incompatible hub.");
                }
                else
                {
                    console.ReportInfo($"Device with hardware-id '{vidPid}' found at busid '{device.BusId}'.");
                    filtered.Add(device);
                }
            }
            else if (device.PersistedGuid.HasValue)
            {
                console.ReportInfo($"Persisted device with hardware-id '{vidPid}' found at guid '{device.PersistedGuid.Value:D}'.");
                filtered.Add(device);
            }
        }
        if (filtered.Count == 0)
        {
            console.ReportError($"No devices found with hardware-id '{vidPid}'.");
        }
        return filtered;
    }

    static BusId? GetBusIdByHardwareId(VidPid vidPid, IConsole console)
    {
        try
        {
            var device = GetDevicesByHardwareId(vidPid, true, console).SingleOrDefault();
            if (device is null)
            {
                // Already reported.
                return null;
            }
            return device.BusId;
        }
        catch (InvalidOperationException)
        {
            console.ReportError($"Multiple devices with hardware-id '{vidPid}' were found; disambiguate by using '--busid'.");
            return null;
        }
    }

    Task<ExitCode> ICommandHandlers.License(IConsole console, CancellationToken cancellationToken)
    {
#pragma warning disable CA1849 // Call async methods when in an async method
        // 70 leads (approximately) to the GPL default.
        var width = console.IsOutputRedirected ? 70 : console.WindowWidth;
        foreach (var line in Wrap($"""
            {Program.Product} {GitVersionInformation.MajorMinorPatch}
            {Program.Copyright}

            This program is free software: you can redistribute it and/or modify \
            it under the terms of the GNU General Public License as published by \
            the Free Software Foundation, version 3.

            This program is distributed in the hope that it will be useful, \
            but WITHOUT ANY WARRANTY; without even the implied warranty of \
            MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the \
            GNU General Public License for more details.

            You should have received a copy of the GNU General Public License \
            along with this program. If not, see <https://www.gnu.org/licenses/>.

            """.Unwrap()
            , width))
        {
            console.Out.WriteLine(line);
        }
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    static string GetDescription(Device device, bool usbIds)
    {
        if (usbIds)
        {
            var (vendor, product) = device.HardwareId.Descriptions;
            return vendor is not null
                ? $"{vendor}, {product ?? WindowsDevice.UnknownDescription}"
                : WindowsDevice.UnknownDescription;
        }
        else
        {
            return device.Description;
        }
    }

    Task<ExitCode> ICommandHandlers.List(bool usbIds, bool connectedOnly, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

#pragma warning disable CA1849 // Call async methods when in an async method
        var allDevices = DeviceExtensions.GetAll().ToList();
        // A bind is valid only if it was confirmed in the current run of the service (see BindWatcher).
        var serviceRunId = Server.IsRunning() ? Data.BindStore.GetServiceRunId() : null;
        var validBinds = new HashSet<Guid>();
        if (serviceRunId is not null)
        {
            validBinds.UnionWith(Data.BindStore.GetBindInfos().Where(b => b.ConfirmedRunId == serviceRunId).Select(b => b.Guid));
        }
        var needsReview = false;
        console.Out.WriteLine("Connected:");
        console.Out.WriteLine($"{"BUSID",-5}  {"VID:PID",-9}  {"DEVICE",-60}  STATE");
        foreach (var device in allDevices.Where(d => d.BusId.HasValue).OrderBy(d => d.BusId.GetValueOrDefault()))
        {
            Debug.Assert(device.BusId.HasValue);
            var state = device.ClientIPAddress is not null ? "Attached"
                : device.PersistedGuid is Guid guid
                    ? (!device.IsForced ? "Needs bind (driver lost)" : validBinds.Contains(guid) ? "Shared" : "Needs review")
                : device.BusId.Value.IsIncompatibleHub ? "Incompatible hub"
                : device.IsForced ? "Quarantined"
                : "Not shared";
            needsReview |= state is "Needs review" or "Needs bind (driver lost)" or "Quarantined";
            console.Out.Write($"{(device.BusId.Value.IsIncompatibleHub ? string.Empty : device.BusId.Value),-5}  ");
            console.Out.Write($"{device.HardwareId,-9}  ");
            console.WriteTruncated(GetDescription(device, usbIds), 60, true);
            console.Out.WriteLine($"  {state}");
        }
        console.Out.WriteLine();

        if (!connectedOnly)
        {
            console.Out.WriteLine("Persisted:");
            console.Out.WriteLine($"{"GUID",-36}  DEVICE");
            foreach (var device in allDevices.Where(d => !d.BusId.HasValue && d.PersistedGuid.HasValue).OrderBy(d => d.PersistedGuid.GetValueOrDefault()))
            {
                Debug.Assert(device.PersistedGuid.HasValue);
                console.Out.Write($"{device.PersistedGuid.Value,-36:D}  ");
                console.WriteTruncated(GetDescription(device, usbIds), 60, false);
                console.Out.WriteLine();
            }
            console.Out.WriteLine();
        }

        _ = console.CheckAndReportServerRunning(false);
        if (needsReview)
        {
            console.ReportWarning("Devices that need a review or a bind cannot be attached: 'usbipd bind --review' confirms shared devices after " +
                "a service start; a quarantined device (its bind was dropped when it was removed) needs 'usbipd bind' or 'usbipd unbind'.");
        }
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    Task<ExitCode> ICommandHandlers.Unbind(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        // A device that still has the VBoxUSB driver without a bind is quarantined (its bind was dropped); unbind releases it.
        if (device.PersistedGuid is null && !device.IsForced)
        {
            // Not an error, just let the user know they just executed a no-op.
            console.ReportInfo($"Device with busid '{busId}' was already not shared.");
            return Task.FromResult(ExitCode.Success);
        }
        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }
        if (device.PersistedGuid is not null)
        {
            Data.BindStore.StopSharingDevice(device.PersistedGuid.Value);
        }
        if (WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice))
        {
            if (DriverTools.UnforceVBoxDriver(windowsDevice))
            {
                console.ReportRebootRequired();
            }
        }
        return Task.FromResult(ExitCode.Success);
    }

    static ExitCode Unbind(IEnumerable<Device> devices, IConsole console)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        // Unbind acts as a cleanup and has to support partially failed binds.

        var deviceList = devices.ToList();
        if (deviceList.Count == 0)
        {
            // This would result in a no-op, which may not be what the user intended.
            return ExitCode.Failure;
        }
        if (!CheckWriteAccess(console))
        {
            // We don't actually know if there is anything to clean up, but if there is
            // then administrator privileges are required.
            return ExitCode.AccessDenied;
        }
        var reboot = false;
        var driverError = false;
        foreach (var device in deviceList)
        {
            if (device.PersistedGuid is not null)
            {
                Data.BindStore.StopSharingDevice(device.PersistedGuid.Value);
                ReportUsersOfUnboundDevice(device.InstanceId, console);
            }
            if (WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice))
            {
                try
                {
                    if (DriverTools.UnforceVBoxDriver(windowsDevice))
                    {
                        reboot = true;
                    }
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch
#pragma warning restore CA1031 // Do not catch general exception types
                {
                    driverError = true;
                }
            }
        }
        if (driverError)
        {
            console.ReportError("Not all drivers could be restored.");
        }
        if (reboot)
        {
            console.ReportRebootRequired();
        }
        return ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.Unbind(Guid guid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var device = Data.BindStore.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid.HasValue && d.PersistedGuid.Value == guid);
        if (device is null)
        {
            console.ReportError($"There is no device with guid '{guid:D}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Unbind([device], console));
    }

    Task<ExitCode> ICommandHandlers.Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        return Task.FromResult(Unbind(GetDevicesByHardwareId(vidPid, false, console), console));
    }

    Task<ExitCode> ICommandHandlers.UnbindAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        // UnbindAll() is even more special. It will also delete corrupt registry entries and
        // also removes stub drivers for devices that are neither shared nor connected.
        // Therefore, UnbindAll() cannot use the generic Unbind() helper.

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }
        // Also run by the uninstaller (as SYSTEM): restore the drivers even if the database cannot be changed.
        try
        {
            Data.BindStore.StopSharingAllDevices();
            if (Data.UserStore.TryGetUsersOfDevice(null).Count > 0)
            {
                console.ReportWarning("User mappings are kept; attach fails for those users until their devices are shared again. Use 'usbipd user list'.");
            }
        }
        catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
        {
            console.ReportWarning($"The binds could not be removed from the database ({ex.Message}); they are kept. The drivers are restored anyway.");
        }
        var reboot = false;
        var driverError = false;
        foreach (var device in WindowsDevice.GetAll(DriverDetails.Instance.ClassGuid, false)
            .Where(d => d.HasVBoxDriver && !d.IsStub))
        {
            try
            {
                if (DriverTools.UnforceVBoxDriver(device))
                {
                    reboot = true;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                driverError = true;
            }
        }
        if (driverError)
        {
            console.ReportError("Not all drivers could be restored.");
        }
        if (reboot)
        {
            console.ReportRebootRequired();
        }
        return Task.FromResult(ExitCode.Success);
    }

    static ExitCode Detach(IEnumerable<Device> devices, IConsole console)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        var deviceList = devices.ToList();
        if (deviceList.Any(d => d.PersistedGuid.HasValue && d.ClientIPAddress is not null) && !CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        var error = false;
        foreach (var device in deviceList)
        {
            if (!device.PersistedGuid.HasValue || device.ClientIPAddress is null)
            {
                // Not an error, just let the user know they just executed a no-op.
                console.ReportInfo($"Device with busid '{device.BusId}' was already not attached.");
                continue;
            }
            try
            {
                // The service detaches the device when it sees the record is gone (within a few seconds).
                if (Data.BindStore.SetDeviceAsDetached(device.PersistedGuid.Value))
                {
                    console.ReportInfo($"Detach requested for device with busid '{device.BusId}'.");
                }
            }
            catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
            {
                console.ReportError($"Failed to detach device with busid '{device.BusId}': {ex.Message}");
                error = true;
            }
        }
        return error ? ExitCode.Failure : ExitCode.Success;
    }

    Task<ExitCode> ICommandHandlers.Detach(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach([device], console));
    }

    Task<ExitCode> ICommandHandlers.Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        var devices = GetDevicesByHardwareId(vidPid, true, console);
        if (devices.Count == 0)
        {
            // This would result in a no-op, which may not be what the user intended.
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(Detach(devices, console));
    }

    Task<ExitCode> ICommandHandlers.DetachAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        try
        {
            // The service detaches the devices when it sees the records are gone (within a few seconds).
            Data.BindStore.SetAllDevicesAsDetached();
        }
        catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
        {
            console.ReportError($"Failed to detach the devices: {ex.Message}");
            return Task.FromResult(ExitCode.Failure);
        }
        return Task.FromResult(ExitCode.Success);
    }

    async Task<ExitCode> ICommandHandlers.State(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }

        console.SetError(TextWriter.Null);

        var state = new State()
        {
            Devices = DeviceExtensions.GetAll().OrderBy(d => d.InstanceId).ToList(),
        };

        var context = new StateSerializerContext(new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
        });
        var json = JsonSerializer.Serialize(state, context.State);

        await console.Out.WriteAsync(json);
        return ExitCode.Success;
    }
}
