// SPDX-FileCopyrightText: 2021 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace Usbipd;

static partial class LogEvents
{
    [LoggerMessage(1, LogLevel.Information, "Client {clientAddress} claimed device at {busId} ({instanceId}).")]
    public static partial void ClientAttach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(2, LogLevel.Information, "Client {clientAddress} released device at {busId} ({instanceId}).")]
    public static partial void ClientDetach(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(3, LogLevel.Error, "An exception occurred while communicating with the client.")]
    public static partial void ClientError(this ILogger logger, Exception ex);

    [LoggerMessage(4, LogLevel.Error, "An internal error occurred: {text}")]
    public static partial void InternalError(this ILogger logger, string text, Exception? ex = null);

    [LoggerMessage(5, LogLevel.Information, "Auto-bind of device at {busId} ({instanceId}) by client {clientAddress}.")]
    public static partial void AutoBind(this ILogger logger, IPAddress clientAddress, BusId busId, string instanceId);

    [LoggerMessage(6, LogLevel.Information, "Client {clientAddress} authenticated as user '{userName}'; device at {busId}.")]
    public static partial void ClientAuthenticated(this ILogger logger, IPAddress clientAddress, string userName, BusId busId);

    [LoggerMessage(7, LogLevel.Warning, "Client {clientAddress} failed to authenticate as user '{userName}'.")]
    public static partial void ClientAuthenticationFailed(this ILogger logger, IPAddress clientAddress, string userName);

    [LoggerMessage(8, LogLevel.Warning, "Client {clientAddress} authenticated as user '{userName}', but no device is available for that user (guid '{deviceGuid}').")]
    public static partial void ClientNoDeviceForUser(this ILogger logger, IPAddress clientAddress, string userName, Guid? deviceGuid);

    [LoggerMessage(9, LogLevel.Warning, "Client {clientAddress} sent {opCode} without authentication; refused.")]
    public static partial void ClientNotAuthenticated(this ILogger logger, IPAddress clientAddress, string opCode);

    [LoggerMessage(10, LogLevel.Warning, "Client {clientAddress} (user '{userName}') requested device at {busId}, which is not mapped to that user; refused.")]
    public static partial void ClientImportRefused(this ILogger logger, IPAddress clientAddress, string userName, BusId busId);

    [LoggerMessage(11, LogLevel.Warning, "TLS handshake with client {clientAddress} failed: {reason}")]
    public static partial void ClientTlsFailed(this ILogger logger, IPAddress clientAddress, string reason);

    [LoggerMessage(12, LogLevel.Information, "Client {clientAddress} connected with certificate '{subject}' ({thumbprint}).")]
    public static partial void ClientTlsAuthenticated(this ILogger logger, IPAddress clientAddress, string subject, string thumbprint);

    [LoggerMessage(13, LogLevel.Warning, "Client certificate '{subject}' ({thumbprint}) rejected: {reason}.")]
    public static partial void ClientCertificateRejected(this ILogger logger, string subject, string thumbprint, string reason);

    [LoggerMessage(14, LogLevel.Information, "Listening for TLS connections on {endPoint} with server certificate '{subject}' ({thumbprint}), valid until {notAfter}.")]
    public static partial void TlsListening(this ILogger logger, string endPoint, string subject, string thumbprint, DateTime notAfter);

    [LoggerMessage(15, LogLevel.Critical, "TLS configuration error: {text}")]
    public static partial void TlsConfigurationError(this ILogger logger, string text);

    [LoggerMessage(16, LogLevel.Information, "Session of user '{userName}' on device {busId} from client {clientAddress} ended: {reason}.")]
    public static partial void SessionRevoked(this ILogger logger, IPAddress clientAddress, string userName, BusId busId, string reason);

    [LoggerMessage(17, LogLevel.Warning, "Client {clientAddress} (user '{userName}') did not request the import within {seconds} s; connection closed.")]
    public static partial void ClientImportTimeout(this ILogger logger, IPAddress clientAddress, string userName, int seconds);

    [LoggerMessage(18, LogLevel.Warning, "Client {clientAddress} (user '{userName}') requested device at {busId}, which another connection is importing or using; refused.")]
    public static partial void ClientDeviceBusy(this ILogger logger, IPAddress clientAddress, string userName, BusId busId);

    [LoggerMessage(19, LogLevel.Critical, "Configuration error: {text}")]
    public static partial void ConfigurationError(this ILogger logger, string text);

    [LoggerMessage(20, LogLevel.Warning, "Active sessions cannot be checked against the user database: {reason}")]
    public static partial void SessionCheckFailed(this ILogger logger, string reason);

    [LoggerMessage(21, LogLevel.Warning, "Client {clientAddress} (user '{userName}'): the user database cannot be reached: {reason}")]
    public static partial void UserDatabaseUnavailable(this ILogger logger, IPAddress clientAddress, string userName, string reason);

    [LoggerMessage(22, LogLevel.Warning, "Device at {busId} ({instanceId}) could not be handed back to Windows after detach: {reason}. It may need to be replugged.")]
    public static partial void DeviceRestartFailed(this ILogger logger, BusId busId, string instanceId, string reason);

    [LoggerMessage(23, LogLevel.Warning, "The attachment record could not be removed from the database: {reason}")]
    public static partial void AttachmentRecordNotCleared(this ILogger logger, string reason);

    [LoggerMessage(24, LogLevel.Warning, "Attachment records of an earlier run could not be removed from the database: {reason}")]
    public static partial void StaleAttachmentsNotCleared(this ILogger logger, string reason);

    [LoggerMessage(1000, LogLevel.Debug, "{text}")]
    public static partial void Debug(this ILogger logger, string text);

    [LoggerMessage(1001, LogLevel.Trace, "{text}")]
    public static partial void Trace(this ILogger logger, string text);
}
