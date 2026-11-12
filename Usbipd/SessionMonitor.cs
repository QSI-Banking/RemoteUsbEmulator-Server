// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Revokes active sessions whose user was removed or mapped to another device ('usbipd user remove', 'usbipd user add --force').
/// </summary>
sealed class SessionMonitor(SessionRegistry sessionRegistry, IUserDeviceMap userDeviceMap, ILogger<SessionMonitor> logger) : BackgroundService
{
    readonly SessionRegistry SessionRegistry = sessionRegistry;
    readonly IUserDeviceMap UserDeviceMap = userDeviceMap;
    readonly ILogger Logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SessionRegistry.Settings.RecheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CheckSessions();
        }
    }

    internal void CheckSessions()
    {
        foreach (var session in SessionRegistry.Snapshot())
        {
            if (session.EndReason is not null)
            {
                continue;
            }
            Guid? current;
            try
            {
                current = UserDeviceMap.GetDeviceGuid(session.UserName);
            }
            catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
            {
                // Keep running sessions while the database is unreachable; the time limit still applies.
                Logger.SessionCheckFailed(ex.Message);
                return;
            }
            if (current is null)
            {
                session.Revoke("the user was removed or the device is no longer shared");
            }
            else if (current.Value != session.DeviceGuid)
            {
                session.Revoke("the user's device mapping changed");
            }
        }
    }
}
