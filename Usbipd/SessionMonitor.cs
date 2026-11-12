// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Revokes active sessions whose user was removed or mapped to another device ('usbipd user remove', 'usbipd user add --force').
/// </summary>
sealed class SessionMonitor(SessionRegistry sessionRegistry, IUserDeviceMap userDeviceMap) : BackgroundService
{
    readonly SessionRegistry SessionRegistry = sessionRegistry;
    readonly IUserDeviceMap UserDeviceMap = userDeviceMap;

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
            var current = UserDeviceMap.GetDeviceGuid(session.UserName);
            if (current is null)
            {
                session.Revoke("the user was removed");
            }
            else if (current.Value != session.DeviceGuid)
            {
                session.Revoke("the user's device mapping changed");
            }
        }
    }
}
