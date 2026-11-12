// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Revokes active sessions whose user was removed or mapped to another device ('usbipd user remove', 'usbipd user add --force'),
/// whose attachment record was deleted ('usbipd detach', 'usbipd unbind'), or whose client certificate was disabled or removed
/// ('usbipd client disable', 'usbipd client remove').
/// </summary>
sealed class SessionMonitor(SessionRegistry sessionRegistry, IUserDeviceMap userDeviceMap, IClientAllowList allowList,
    ILogger<SessionMonitor> logger) : BackgroundService
{
    readonly SessionRegistry SessionRegistry = sessionRegistry;
    readonly IUserDeviceMap UserDeviceMap = userDeviceMap;
    readonly IClientAllowList AllowList = allowList;
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
        // Take the sessions before reading the records: a session that is recorded now has its record written already.
        var sessions = SessionRegistry.Snapshot();
        if (sessions.Length == 0)
        {
            return;
        }
        var recorded = sessions.Where(s => s.IsRecorded).ToList();
        IReadOnlySet<Guid> attached;
        try
        {
            attached = recorded.Count == 0 ? new HashSet<Guid>() : UserDeviceMap.GetAttachedDevices();
            AllowList.Refresh();
        }
        catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
        {
            Logger.SessionCheckFailed(ex.Message);
            return;
        }
        foreach (var session in recorded)
        {
            if (!attached.Contains(session.DeviceGuid))
            {
                session.Revoke("an administrator detached or unbound the device");
            }
        }

        foreach (var session in sessions)
        {
            if (session.EndReason is not null)
            {
                continue;
            }
            bool clientAllowed;
            try
            {
                clientAllowed = AllowList.IsAllowed(session.ClientThumbprint);
            }
            catch (Exception ex) when (Data.UserStore.IsDatabaseError(ex))
            {
                Logger.SessionCheckFailed(ex.Message);
                return;
            }
            if (!clientAllowed)
            {
                session.Revoke("the client certificate was disabled or removed");
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
