// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Usbipd.Data;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace Usbipd;

/// <summary>
/// A bind is only valid while this service has watched the device without a break since an administrator confirmed it.
/// <list type="bullet">
/// <item>Every start of the service is a new run (<see cref="RunId"/>); binds confirmed in an earlier run need a review.</item>
/// <item>Windows reports every removal of a shared device (unplugged, moved, disabled, driver changed) as an event,
/// so a quick swap is not missed; the bind is dropped at once and its session ends.</item>
/// <item>Before every import the device is compared with what was recorded (<see cref="GetInvalidReasonAsync"/>).</item>
/// </list>
/// Nothing is ever shared again automatically: a dropped bind needs a new 'usbipd bind' by an administrator.
/// </summary>
sealed class BindWatcher(SessionRegistry sessionRegistry, ILogger<BindWatcher> logger) : BackgroundService
{
    static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Interface paths of removed devices, from the notification callback. One watcher per process.
    /// </summary>
    static readonly Channel<string> RemovedInterfaces = Channel.CreateUnbounded<string>();

    readonly SessionRegistry SessionRegistry = sessionRegistry;
    readonly ILogger Logger = logger;

    /// <summary>
    /// Removed devices (see <see cref="BindStore.GetInterfaceKey"/>) whose binds are not dropped from the database yet.
    /// </summary>
    readonly ConcurrentDictionary<string, bool> PendingDrops = new(StringComparer.Ordinal);
    volatile bool RunRecorded;

    /// <summary>
    /// This run of the service.
    /// </summary>
    public Guid RunId { get; } = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not hold up the start of the service while the database is slow or down.
        await Task.Yield();

        // Watch first, then tell the database about the run: nothing can be confirmed for this run before it is watched.
        using var notification = RegisterForRemovals();
        while (!stoppingToken.IsCancellationRequested)
        {
            ProcessEvents();
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                wait.CancelAfter(PendingDrops.IsEmpty && RunRecorded ? Timeout.InfiniteTimeSpan : RetryInterval);
                try
                {
                    _ = await RemovedInterfaces.Reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Time to retry the database.
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    void ProcessEvents()
    {
        while (RemovedInterfaces.Reader.TryRead(out var interfacePath))
        {
            if (GetInterfaceKey(interfacePath) is { } key)
            {
                // In memory first: the bind is invalid from now on, also while the database cannot be reached.
                PendingDrops[key] = true;
            }
        }
        try
        {
            if (!RunRecorded)
            {
                BindStore.StartServiceRun(RunId);
                RunRecorded = true;
                var count = BindStore.GetBindInfos().Count;
                if (count > 0)
                {
                    Logger.BindsNeedReview(count);
                }
            }
            foreach (var key in PendingDrops.Keys)
            {
                foreach (var (guid, instanceId) in BindStore.DropBindsOfRemovedDevice(key))
                {
                    Logger.BindDropped(instanceId, guid);
                    foreach (var session in SessionRegistry.Snapshot().Where(s => s.DeviceGuid == guid))
                    {
                        session.Revoke("the device was removed; its bind is dropped");
                    }
                }
                _ = PendingDrops.TryRemove(key, out _);
            }
        }
        catch (Exception ex) when (UserStore.IsDatabaseError(ex))
        {
            Logger.BindWatchFailed(ex.Message);
        }
    }

    /// <summary>
    /// Why the device may not be imported, or <see langword="null"/> if its bind is valid: confirmed in this run,
    /// not removed since, and the device present is the one that was recorded.
    /// </summary>
    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The database cannot be reached.</exception>
    public async Task<string?> GetInvalidReasonAsync(Guid bindGuid, string instanceId)
    {
        if (PendingDrops.ContainsKey(BindStore.GetInterfaceKey(instanceId)))
        {
            return "the device was removed; its bind is dropped";
        }
        var bind = BindStore.GetBindInfos().SingleOrDefault(b => b.Guid == bindGuid);
        if (bind is null)
        {
            return "the device is not shared";
        }
        if (!RunRecorded || bind.ConfirmedRunId != RunId)
        {
            return "the bind needs a review since the service started ('usbipd bind --review')";
        }
        if (!WindowsDevice.TryCreate(instanceId, out var device) || !device.IsPresent)
        {
            return "the device is not present";
        }
        if (!device.HasVBoxDriver)
        {
            return "the device no longer has the VBoxUSB driver; unbind it and bind it again";
        }
        try
        {
            var identity = await DeviceIdentity.ReadAsync(device);
            return identity.GetMismatch(bind.Fingerprint, bind.LocationPath);
        }
        catch (InvalidOperationException ex) when (ex is not DatabaseConfigurationException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// The part of a device interface path that names the device, as <see cref="BindStore.GetInterfaceKey"/>:
    /// \\?\USB#VID_046D&amp;PID_082D#A5BA06AF#{guid} gives USB#VID_046D&amp;PID_082D#A5BA06AF.
    /// </summary>
    internal static string? GetInterfaceKey(string interfacePath)
    {
        var start = interfacePath.StartsWith(@"\\?\", StringComparison.Ordinal) ? 4 : 0;
        var end = interfacePath.LastIndexOf("#{", StringComparison.Ordinal);
        return end > start ? interfacePath[start..end].ToUpperInvariant() : null;
    }

    /// <summary>
    /// Removal of the VBoxUSB device interface: the driver of a shared device stopped, for whatever reason.
    /// </summary>
    static CM_Unregister_NotificationSafeHandle RegisterForRemovals()
    {
        unsafe // DevSkim: ignore DS172412
        {
            CM_NOTIFY_FILTER filter = new()
            {
                cbSize = (uint)Unsafe.SizeOf<CM_NOTIFY_FILTER>(),
                FilterType = CM_NOTIFY_FILTER_TYPE.CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
                u = {
                    DeviceInterface =
                    {
                        ClassGuid = Interop.VBoxUsb.GUID_CLASS_VBOXUSB,
                    },
                },
            };

            [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
            static uint callback(HCMNOTIFICATION notify, void* context, CM_NOTIFY_ACTION action, CM_NOTIFY_EVENT_DATA* eventData, uint eventDataSize)
            {
                // CM_NOTIFY_EVENT_DATA: FilterType (4), Reserved (4), ClassGuid (16), then the NUL-terminated SymbolicLink.
                const int SymbolicLinkOffset = 24;
                if (action == CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL && eventDataSize > SymbolicLinkOffset)
                {
                    var chars = new ReadOnlySpan<char>((byte*)eventData + SymbolicLinkOffset, (int)(eventDataSize - SymbolicLinkOffset) / sizeof(char));
                    var length = chars.IndexOf('\0');
                    _ = RemovedInterfaces.Writer.TryWrite(new string(length < 0 ? chars : chars[..length]));
                }
                return (uint)WIN32_ERROR.ERROR_SUCCESS;
            }

            PInvoke.CM_Register_Notification(filter, null, &callback, out var notification)
                .ThrowOnError(nameof(PInvoke.CM_Register_Notification));
            return notification;
        }
    }
}
