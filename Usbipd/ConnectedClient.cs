// SPDX-FileCopyrightText: 2020 Frans van Dorsselaer
// SPDX-FileCopyrightText: Microsoft Corporation
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

using static Usbipd.Interop.UsbIp;

namespace Usbipd;

sealed class ConnectedClient(ILogger<ConnectedClient> logger, ClientContext clientContext, IServiceProvider serviceProvider,
    IUserAuthenticator userAuthenticator, IUserDeviceMap userDeviceMap)
{
    readonly ILogger Logger = logger;
    readonly ClientContext ClientContext = clientContext;
    readonly IServiceProvider ServiceProvider = serviceProvider;
    readonly IUserAuthenticator UserAuthenticator = userAuthenticator;
    readonly IUserDeviceMap UserDeviceMap = userDeviceMap;
    readonly NetworkStream Stream = clientContext.TcpClient.GetStream();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var opCode = await ReceiveOpCodeAsync(cancellationToken);
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.Debug($"Received opcode: {opCode}");
            }
            switch (opCode)
            {
                case OpCode.OP_REQ_AUTH:
                    // Authenticate, then exactly one OP_REQ_IMPORT for the user's device.
                    if (!await HandleRequestAuthAsync(cancellationToken))
                    {
                        return;
                    }
                    opCode = await ReceiveOpCodeAsync(cancellationToken);
                    if (Logger.IsEnabled(LogLevel.Debug))
                    {
                        Logger.Debug($"Received opcode: {opCode}");
                    }
                    if (opCode != OpCode.OP_REQ_IMPORT)
                    {
                        throw new ProtocolViolationException($"unexpected opcode {opCode} after authentication");
                    }
                    await HandleRequestImportAsync(cancellationToken);
                    break;
                case OpCode.OP_REQ_DEVLIST:
                    // Device enumeration is disabled: clients attach by user name only.
                    Logger.ClientNotAuthenticated(ClientContext.ClientAddress, opCode.ToString());
                    await SendOpCodeAsync(OpCode.OP_REP_DEVLIST, Status.ST_NA);
                    break;
                case OpCode.OP_REQ_IMPORT:
                    // Import without prior authentication: consume the busid and refuse.
                    await Stream.ReadMessageAsync(new byte[SYSFS_BUS_ID_SIZE], cancellationToken);
                    Logger.ClientNotAuthenticated(ClientContext.ClientAddress, opCode.ToString());
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NA);
                    break;
                case OpCode.OP_REP_DEVLIST:
                case OpCode.OP_REP_IMPORT:
                case OpCode.OP_REP_AUTH:
                default:
                    throw new ProtocolViolationException($"unexpected opcode {opCode}");
            }
        }
        catch (Exception ex)
        {
            // EndOfStream is client hang ups and OperationCanceled is detachments;
            // neither are an error but part of normal functionality.
            if (ex is not (EndOfStreamException or OperationCanceledException))
            {
                Logger.ClientError(ex);
            }
            throw;
        }
    }

    /// <summary>
    /// Reads one length-prefixed (u8) UTF-8 string. The payload is always consumed, so the stream stays in sync.
    /// </summary>
    /// <returns><see langword="null"/> if the string is empty, too long, or not valid UTF-8.</returns>
    async Task<string?> ReceiveLengthPrefixedStringAsync(int maxLength, CancellationToken cancellationToken)
    {
        var length = new byte[1];
        await Stream.ReadMessageAsync(length, cancellationToken);
        var buf = new byte[length[0]];
        await Stream.ReadMessageAsync(buf, cancellationToken);
        if (buf.Length == 0 || buf.Length > maxLength)
        {
            return null;
        }
        try
        {
            return StrictUtf8.GetString(buf);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Request: u8 user_len, user, u8 otp_len, otp.
    /// Reply: OP_REP_AUTH + status; on ST_OK followed by char busid[SYSFS_BUS_ID_SIZE] of the user's device.
    /// </summary>
    /// <returns><see langword="true"/> if the client may continue with OP_REQ_IMPORT.</returns>
    async Task<bool> HandleRequestAuthAsync(CancellationToken cancellationToken)
    {
        var userName = await ReceiveLengthPrefixedStringAsync(USBIP_MAX_USER, cancellationToken);
        var otp = await ReceiveLengthPrefixedStringAsync(USBIP_MAX_OTP, cancellationToken);

        // Never log the one-time password; log the user name only if it is well-formed.
        if (userName is null || otp is null || !UserNames.IsValid(userName) || !UserAuthenticator.Authenticate(userName, otp))
        {
            Logger.ClientAuthenticationFailed(ClientContext.ClientAddress, UserNames.IsValid(userName) ? userName : "<invalid>");
            await SendOpCodeAsync(OpCode.OP_REP_AUTH, Status.ST_AUTH_FAILED);
            return false;
        }

        var guid = UserDeviceMap.GetDeviceGuid(userName);
        var device = guid.HasValue ? UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid == guid.Value) : null;
        if (device is null || !device.BusId.HasValue || device.BusId.Value.IsIncompatibleHub)
        {
            // Not mapped, bind removed, unplugged, or on an incompatible hub.
            Logger.ClientNoDeviceForUser(ClientContext.ClientAddress, userName, guid);
            await SendOpCodeAsync(OpCode.OP_REP_AUTH, Status.ST_NODEV);
            return false;
        }

        ClientContext.AuthenticatedUser = userName;
        ClientContext.AuthorizedGuid = guid;
        Logger.ClientAuthenticated(ClientContext.ClientAddress, userName, device.BusId.Value);

        await SendOpCodeAsync(OpCode.OP_REP_AUTH, Status.ST_OK);
        var buf = new byte[SYSFS_BUS_ID_SIZE];
        _ = Encoding.UTF8.GetBytes(device.BusId.Value.ToString(), buf);
        await Stream.WriteAsync(buf, cancellationToken);
        return true;
    }

    async Task HandleRequestImportAsync(CancellationToken cancellationToken)
    {
        BusId busId;
        {
            var buf = new byte[SYSFS_BUS_ID_SIZE];
            await Stream.ReadMessageAsync(buf, cancellationToken);
            if (!BusId.TryParse(Encoding.UTF8.GetString([.. buf.TakeWhile(b => b != 0)]), out busId))
            {
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                return;
            }
        }

        // whatever happens, try to report "something" to the client
        var status = Status.ST_ERROR;

        VBoxUsbMon? mon = null;
        try
        {
            // Only the device mapped to the authenticated user may be imported (no policy auto-bind).
            var device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId);
            if (ClientContext.AuthenticatedUser is null || device is null || device.PersistedGuid != ClientContext.AuthorizedGuid)
            {
                Logger.ClientImportRefused(ClientContext.ClientAddress, ClientContext.AuthenticatedUser ?? string.Empty, busId);
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                return;
            }
            Debug.Assert(device.BusId.HasValue);
            Debug.Assert(device.PersistedGuid.HasValue);

            if (device.ClientIPAddress is not null)
            {
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_DEV_BUSY);
                return;
            }

            var exportedDevice = await ExportedDevice.GetExportedDevice(device, cancellationToken);

            status = Status.ST_NA;

            ulong filterId = 0;
            try
            {
                if (!WindowsDevice.TryCreate(device.InstanceId, out var windowsDevice))
                {
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_NODEV);
                    return;
                }
                // We use the modern way to restart the device, which works much better than the obsolete VBoxUsbMon port cycling.
                using var restartingDevice = new RestartingDevice(windowsDevice);
                if (!windowsDevice.HasVBoxDriver)
                {
                    mon = new VBoxUsbMon();
                    var version = await mon.GetVersion();
                    if (!VBoxUsbMon.IsVersionSupported(version))
                    {
                        throw new NotSupportedException($"VBoxUsbMon version {version.major}.{version.minor} is not supported.");
                    }
                    filterId = await mon.AddFilter(exportedDevice);
                }
            }
            catch (ConfigurationManagerException ex) when (ex.ConfigRet == CONFIGRET.CR_REMOVE_VETOED)
            {
                // The host is actively using the device.
                status = Status.ST_DEV_BUSY;
                throw;
            }

            status = Status.ST_DEV_ERR;
            var sw = new Stopwatch();
            sw.Start();
            (var vboxDevice, ClientContext.AttachedDevice) = await VBoxUsb.ClaimDevice(device.BusId.Value);
            sw.Stop();
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.Debug($"Claiming took {sw.ElapsedMilliseconds} ms");
            }
            ClientContext.AttachedBusId = device.BusId;

            CM_Unregister_NotificationSafeHandle? notification = null;
            Logger.ClientAttach(ClientContext.ClientAddress, busId, device.InstanceId);
            try
            {
                status = Status.ST_OK;
                await SendOpCodeAsync(OpCode.OP_REP_IMPORT, Status.ST_OK);
                exportedDevice.Serialize(Stream, false);

                // setup token to free device
                using var attachedClientTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var cancelEvent = new AutoResetEvent(false);
                _ = ThreadPool.RegisterWaitForSingleObject(cancelEvent, (state, timedOut) =>
                {
                    Logger.Debug("Unbind or unplug while attached");
                    try
                    {
                        attachedClientTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException) { }
                }, null, Timeout.Infinite, true);

                // Detect unplug.
                unsafe // DevSkim: ignore DS172412
                {
                    CM_NOTIFY_FILTER filter = new()
                    {
                        cbSize = (uint)Unsafe.SizeOf<CM_NOTIFY_FILTER>(),
                        FilterType = CM_NOTIFY_FILTER_TYPE.CM_NOTIFY_FILTER_TYPE_DEVICEHANDLE,
                        u = {
                            DeviceHandle =
                            {
                                hTarget = ClientContext.AttachedDevice.DangerousGetHandle(),
                            },
                        },
                    };

                    [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvStdcall) })]
                    static uint callback(HCMNOTIFICATION notify, void* context, CM_NOTIFY_ACTION action, CM_NOTIFY_EVENT_DATA* eventData, uint eventDataSize)
                    {
                        if (action is CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVEPENDING or CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEREMOVECOMPLETE)
                        {
                            _ = PInvoke.SetEvent((HANDLE)(nint)context);
                        }
                        return (uint)WIN32_ERROR.ERROR_SUCCESS;
                    }

                    PInvoke.CM_Register_Notification(filter, (void*)cancelEvent.SafeWaitHandle.DangerousGetHandle(), &callback, out notification)
                        .ThrowOnError(nameof(PInvoke.CM_Register_Notification));
                }

                // Detect unbind.
                using var attachedKey = UsbipdRegistry.Instance.SetDeviceAsAttached(device.PersistedGuid.Value, device.BusId.Value, ClientContext.ClientAddress,
                    vboxDevice.InstanceId);
                var result = PInvoke.RegNotifyChangeKeyValue(attachedKey.Handle, false,
                    Windows.Win32.System.Registry.REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC, cancelEvent.SafeWaitHandle, true);
                if (result != WIN32_ERROR.ERROR_SUCCESS)
                {
                    throw new Win32Exception((int)result, nameof(PInvoke.RegNotifyChangeKeyValue));
                }

                await ServiceProvider.GetRequiredService<AttachedClient>().RunAsync(attachedClientTokenSource.Token);
            }
            finally
            {
                notification?.Dispose();

                _ = UsbipdRegistry.Instance.SetDeviceAsDetached(device.PersistedGuid.Value);

                ClientContext.AttachedDevice.Dispose();

                Logger.ClientDetach(ClientContext.ClientAddress, busId, device.InstanceId);

                try
                {
                    // We use the modern way to restart the device, which works much better than the obsolete VBoxUsbMon port cycling.
                    using var restartingDevice = new RestartingDevice(vboxDevice);
                    if (mon is not null)
                    {
                        await mon.RemoveFilter(filterId);
                    }
                }
                catch (ConfigurationManagerException) { }
            }
        }
        catch
        {
            if (status != Status.ST_OK)
            {
                try
                {
                    // We'll do our best to report back to the client, but if that
                    // fails we'll throw the *original* exception.
                    await SendOpCodeAsync(OpCode.OP_REP_IMPORT, status);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch { }
#pragma warning restore CA1031 // Do not catch general exception types
            }
            throw;
        }
        finally
        {
            mon?.Dispose();
        }
    }

    async Task<OpCode> ReceiveOpCodeAsync(CancellationToken cancellationToken)
    {
        var buf = new byte[8];
        await Stream.ReadMessageAsync(buf, cancellationToken);

        // marshal and validate
        var version = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(0));
        if (version != USBIP_VERSION)
        {
            throw new ProtocolViolationException(
                $"USB/IP protocol version mismatch: expected {USBIP_VERSION.UsbIpVersionToVersion()}, got {version.UsbIpVersionToVersion()}");
        }

        var opCode = (OpCode)BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2));
        if (!Enum.IsDefined(opCode))
        {
            throw new ProtocolViolationException($"illegal opcode: {(ushort)opCode}");
        }

        var status = (Status)BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(4));
        return Enum.IsDefined(status) ? status == Status.ST_OK ? opCode
            : throw new ProtocolViolationException($"error status at peer: {status}")
            : throw new ProtocolViolationException($"illegal status: {status}");
    }

    async Task SendOpCodeAsync(OpCode opCode, Status status)
    {
        var buf = new byte[8];

        // marshal
        BinaryPrimitives.WriteUInt16BigEndian(buf, USBIP_VERSION);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), (ushort)opCode);
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(4), (uint)status);

        await Stream.WriteAsync(buf);
    }
}
