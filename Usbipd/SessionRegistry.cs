// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;

namespace Usbipd;

/// <summary>
/// The devices that are being imported or are attached, at most one session per device.
/// A session ends when its connection ends, or earlier when it is revoked (time limit, mapping changed, user removed).
/// </summary>
sealed class SessionRegistry(IConfiguration configuration)
{
    readonly Lock Lock = new();
    readonly Dictionary<Guid, AttachSession> Sessions = [];

    public SessionSettings Settings { get; } = SessionSettings.ReadOrDefault(configuration);

    /// <summary>
    /// Claims the device for one import. This is the per-device lock: the claim is atomic, so of two
    /// connections that import the same device at the same time, exactly one gets it.
    /// </summary>
    /// <returns>The session (dispose it to release the device), or <see langword="null"/> if another connection holds the device.</returns>
    public AttachSession? TryClaim(Guid deviceGuid, string userName, IPAddress clientAddress, string clientThumbprint)
    {
        lock (Lock)
        {
            if (Sessions.ContainsKey(deviceGuid))
            {
                return null;
            }
            var session = new AttachSession(this, deviceGuid, userName, clientAddress, clientThumbprint);
            Sessions.Add(deviceGuid, session);
            return session;
        }
    }

    public AttachSession[] Snapshot()
    {
        lock (Lock)
        {
            return [.. Sessions.Values];
        }
    }

    internal void Release(AttachSession session)
    {
        lock (Lock)
        {
            if (Sessions.TryGetValue(session.DeviceGuid, out var current) && ReferenceEquals(current, session))
            {
                _ = Sessions.Remove(session.DeviceGuid);
            }
        }
    }
}

sealed class AttachSession : IDisposable
{
    internal AttachSession(SessionRegistry registry, Guid deviceGuid, string userName, IPAddress clientAddress, string clientThumbprint)
    {
        Registry = registry;
        DeviceGuid = deviceGuid;
        UserName = userName;
        ClientAddress = clientAddress;
        ClientThumbprint = clientThumbprint;
    }

    readonly SessionRegistry Registry;
    readonly CancellationTokenSource Revocation = new();
    Timer? ExpiryTimer;
    string? RevokedReason;
    int Disposed;
    volatile bool Recorded;

    public Guid DeviceGuid { get; }
    public string UserName { get; }
    public IPAddress ClientAddress { get; }

    /// <summary>
    /// The client certificate of the connection.
    /// </summary>
    public string ClientThumbprint { get; }

    /// <summary>
    /// Canceled when the session is revoked.
    /// </summary>
    public CancellationToken Token => Revocation.Token;

    /// <summary>
    /// Why the session was revoked, or <see langword="null"/> if it was not.
    /// </summary>
    public string? EndReason => Volatile.Read(ref RevokedReason);

    /// <summary>
    /// Whether the attachment is recorded in the database; from then on, deleting the record ('usbipd detach') ends the session.
    /// </summary>
    public bool IsRecorded => Recorded;

    /// <summary>
    /// Call after the attachment record was written.
    /// </summary>
    public void MarkRecorded()
    {
        Recorded = true;
    }

    /// <summary>
    /// Starts the hard time limit; call it when the device is attached.
    /// </summary>
    public void StartClock(TimeSpan maxDuration)
    {
        var minutes = (int)Math.Round(maxDuration.TotalMinutes);
        ExpiryTimer = new Timer(_ => Revoke($"the {minutes} minute session limit was reached"), null, maxDuration, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Ends the session; the first reason wins. Safe to call from any thread, also after <see cref="Dispose"/>.
    /// </summary>
    public void Revoke(string reason)
    {
        if (Interlocked.CompareExchange(ref RevokedReason, reason, null) is null)
        {
            try
            {
                Revocation.Cancel();
            }
            catch (ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref Disposed, 1) == 0)
        {
            ExpiryTimer?.Dispose();
            Registry.Release(this);
            Revocation.Dispose();
        }
    }
}
