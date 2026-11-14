// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;
using Usbipd.Data;
using static Usbipd.ConsoleTools;

namespace Usbipd;

/// <summary>
/// 'usbipd bind' and 'usbipd bind --review'.
/// <para>
/// A bind always puts the device on the VBoxUSB driver (the server itself can then no longer use it), records what the
/// device is (see <see cref="DeviceIdentity"/>), and is confirmed for the current run of the service. The service drops
/// the bind when the device is removed, and every start of the service needs a review (see <see cref="BindWatcher"/>).
/// </para>
/// </summary>
sealed partial class CommandHandlers : ICommandHandlers
{
    static readonly TimeSpan DriverStartTimeout = TimeSpan.FromSeconds(10);

    static async Task<ExitCode> BindAsync(BusId busId, IConsole console, CancellationToken cancellationToken)
    {
        var device = DeviceExtensions.GetAll().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
        if (device is null)
        {
            console.ReportError($"There is no device with busid '{busId}'.");
            return ExitCode.Failure;
        }
        if (!WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice) || !windowsDevice.IsPresent)
        {
            console.ReportError($"The device with busid '{busId}' is not present.");
            return ExitCode.Failure;
        }

        // Without a running service nothing watches the device: the bind is recorded, but needs a review after the service starts.
        var runId = Server.IsRunning() ? BindStore.GetServiceRunId() : null;
        var existing = device.PersistedGuid is Guid existingGuid
            ? BindStore.GetBindInfos().SingleOrDefault(b => b.Guid == existingGuid) : null;
        if (existing is not null && windowsDevice.HasVBoxDriver && runId is not null && existing.ConfirmedRunId == runId)
        {
            // Not an error, just let the user know they just executed a no-op.
            console.ReportInfo($"Device with busid '{busId}' was already shared.");
            return ExitCode.Success;
        }
        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        if (!windowsDevice.HasVBoxDriver)
        {
            if (DriverTools.ForceVBoxDriver(windowsDevice))
            {
                console.ReportRebootRequired();
            }
            if (!await WaitForVBoxDriverAsync(windowsDevice, cancellationToken))
            {
                console.ReportError("The VBoxUSB driver did not start for the device; a reboot may be required. Then run 'usbipd bind' again.");
                return ExitCode.Failure;
            }
        }

        // The row exists before the device is identified: if the device is removed from here on, the service drops the row
        // and the confirmation below fails. The description is read from the device itself (its real product name).
        var description = windowsDevice.Description;
        var guid = BindStore.Persist(device.InstanceId, description);
        DeviceIdentity identity;
        try
        {
            identity = await DeviceIdentity.ReadAsync(windowsDevice);
        }
        catch (InvalidOperationException ex) when (ex is not DatabaseConfigurationException)
        {
            console.ReportError($"The device cannot be identified: {ex.Message}. It is not shared.");
            if (existing is null)
            {
                BindStore.StopSharingDevice(guid);
            }
            return ExitCode.Failure;
        }
        if (existing?.Fingerprint is not null && identity.GetMismatch(existing.Fingerprint, existing.LocationPath) is { } mismatch)
        {
            console.ReportError($"Device with busid '{busId}' is not confirmed: {mismatch}. " +
                $"To share the device that is there now, run 'usbipd unbind --guid {guid:D}' and bind it again.");
            return ExitCode.Failure;
        }
        if (!BindStore.Confirm(guid, identity, runId, review: existing is not null))
        {
            console.ReportError("The device was removed while it was being bound; it is not shared.");
            return ExitCode.Failure;
        }

        console.ReportInfo($"Device with busid '{busId}' is shared: {description}");
        ReportIdentity(console, identity);
        if (identity.SerialNumber is null)
        {
            console.ReportWarning("This device has no unique serial number: Windows identifies it by its port. " +
                "Another device of the same kind in that port cannot be told apart by serial number; keep the port physically secured.");
        }
        if (runId is null)
        {
            console.ReportWarning("The service is not running; the device needs 'usbipd bind --review' after the service starts.");
        }
        return ExitCode.Success;
    }

    static void ReportIdentity(IConsole console, DeviceIdentity identity)
    {
#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other commands)
        console.Out.WriteLine($"  VID:PID:      {identity.HardwareId}");
        console.Out.WriteLine($"  Serial:       {identity.SerialNumber ?? "none (identified by port)"}");
        console.Out.WriteLine($"  Port:         {identity.LocationPath}");
        console.Out.WriteLine($"  Interfaces:   {identity.Interfaces}");
        console.Out.WriteLine($"  Fingerprint:  {identity.Fingerprint}");
#pragma warning restore CA1849
    }

    static async Task<bool> WaitForVBoxDriverAsync(WindowsDevice device, CancellationToken cancellationToken)
    {
        var deadline = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(deadline) < DriverStartTimeout)
        {
            if (device.IsPresent && device.HasVBoxDriver
                && WindowsDevice.GetAll(Interop.VBoxUsb.GUID_CLASS_VBOXUSB).Any(d => d.InstanceId == device.InstanceId))
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        return false;
    }

    async Task<ExitCode> ICommandHandlers.Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        // Every bind is forced; '--force' is accepted for compatibility.
        _ = force;
        return CheckInstalled(console) ? await BindAsync(busId, console, cancellationToken) : ExitCode.Failure;
    }

    async Task<ExitCode> ICommandHandlers.Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken)
    {
        _ = force;
        return CheckInstalled(console) && GetBusIdByHardwareId(vidPid, console) is BusId busId
            ? await BindAsync(busId, console, cancellationToken)
            : ExitCode.Failure;
    }

    /// <summary>
    /// After every start of the service all binds need a review: each device is compared with what was recorded when it
    /// was bound, and an administrator confirms it. A device that differs, is missing or lost its driver is not confirmed.
    /// </summary>
    async Task<ExitCode> ICommandHandlers.BindReview(bool yes, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }
        if (!console.CheckAndReportServerRunning(true))
        {
            return ExitCode.Failure;
        }
        if (BindStore.GetServiceRunId() is not Guid runId)
        {
            console.ReportError("The service did not register with the database yet; try again in a few seconds.");
            return ExitCode.Failure;
        }
        var pending = BindStore.GetBindInfos().Where(b => b.ConfirmedRunId != runId).OrderBy(b => b.Description).ThenBy(b => b.Guid).ToList();
        if (pending.Count == 0)
        {
            console.ReportInfo("No shared device needs a review.");
            return ExitCode.Success;
        }
        if (!yes && Console.IsInputRedirected)
        {
            console.ReportError("The review asks for a confirmation per device and needs an interactive console; or use '--yes' to confirm every device that matches.");
            return ExitCode.Failure;
        }
        if (!CheckWriteAccess(console))
        {
            return ExitCode.AccessDenied;
        }

        var confirmed = 0;
        var refused = 0;
        var skipped = 0;
#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other commands)
        foreach (var bind in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            console.Out.WriteLine();
            console.Out.WriteLine($"{bind.Description}");
            console.Out.WriteLine($"  Guid:         {bind.Guid:D}");
            console.Out.WriteLine($"  Instance:     {bind.InstanceId}");

            string? problem = null;
            DeviceIdentity? identity = null;
            if (!WindowsDevice.TryCreate(bind.InstanceId, out var windowsDevice) || !windowsDevice.IsPresent)
            {
                problem = "the device is not present";
            }
            else if (!windowsDevice.HasVBoxDriver)
            {
                problem = "the device no longer has the VBoxUSB driver";
            }
            else
            {
                try
                {
                    identity = await DeviceIdentity.ReadAsync(windowsDevice);
                    if (bind.Fingerprint is not null)
                    {
                        problem = identity.GetMismatch(bind.Fingerprint, bind.LocationPath);
                    }
                }
                catch (InvalidOperationException ex) when (ex is not DatabaseConfigurationException)
                {
                    problem = ex.Message;
                }
            }
            if (identity is not null)
            {
                ReportIdentity(console, identity);
            }
            if (problem is not null)
            {
                if (bind.Fingerprint is not null)
                {
                    console.Out.WriteLine($"  Recorded:     {bind.HardwareId}, port {bind.LocationPath}, interfaces {bind.Interfaces}");
                    console.Out.WriteLine($"                fingerprint {bind.Fingerprint}");
                }
                console.ReportError($"Not confirmed: {problem}. Use 'usbipd unbind --guid {bind.Guid:D}'; to share what is there now, bind it again.");
                ++refused;
                continue;
            }
            if (bind.Fingerprint is null)
            {
                console.ReportWarning("No identity was recorded for this bind (it is from an earlier version); confirming records the device that is present now.");
            }
            else
            {
                console.Out.WriteLine("  Result:       the same port and the same USB descriptors as when it was bound");
            }
            if (!yes)
            {
                console.Out.Write("Share this device again? [y/N] ");
                var answer = (await Console.In.ReadLineAsync(cancellationToken))?.Trim();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    console.ReportInfo("Not confirmed; the device stays unavailable.");
                    ++skipped;
                    continue;
                }
            }
            if (BindStore.Confirm(bind.Guid, identity!, runId, review: true))
            {
                console.ReportInfo($"Confirmed: {bind.Description}");
                ++confirmed;
            }
            else
            {
                console.ReportError("The device was removed during the review; its bind is dropped.");
                ++refused;
            }
        }
        console.Out.WriteLine();
#pragma warning restore CA1849
        console.ReportInfo($"Review: {confirmed} confirmed, {refused} refused, {skipped} left for later.");
        return refused == 0 ? ExitCode.Success : ExitCode.Failure;
    }
}
