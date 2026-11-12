// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace UnitTests;

[TestClass]
sealed class SessionRegistry_Tests
{
    static readonly Guid Device1 = new("11111111-1111-1111-1111-111111111111");
    static readonly Guid Device2 = new("22222222-2222-2222-2222-222222222222");

    static IConfiguration CreateConfiguration(params (string Key, string Value)[] values)
    {
        var data = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
        {
            data[key] = value;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    sealed class FakeUserDeviceMap : IUserDeviceMap
    {
        public Dictionary<string, Guid> Map { get; } = new(UserNames.Comparer);
        public Guid? GetDeviceGuid(string userName) => Map.TryGetValue(userName, out var guid) ? guid : null;
    }

    [TestMethod]
    public void Settings_Defaults()
    {
        var settings = SessionSettings.Read(CreateConfiguration());

        Assert.AreEqual(TimeSpan.FromMinutes(10), settings.MaxDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(10), settings.ImportTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(2), settings.RecheckInterval);
    }

    [TestMethod]
    public void Settings_Values()
    {
        var settings = SessionSettings.Read(CreateConfiguration(
            ("usbipd:Session:MaxDurationMinutes", "5"),
            ("usbipd:Session:ImportTimeoutSeconds", "20"),
            ("usbipd:Session:RecheckIntervalSeconds", "1")));

        Assert.AreEqual(TimeSpan.FromMinutes(5), settings.MaxDuration);
        Assert.AreEqual(TimeSpan.FromSeconds(20), settings.ImportTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(1), settings.RecheckInterval);
    }

    [TestMethod]
    [DataRow("MaxDurationMinutes", "0")]
    [DataRow("MaxDurationMinutes", "1441")]
    [DataRow("ImportTimeoutSeconds", "-1")]
    [DataRow("RecheckIntervalSeconds", "x")]
    public void Settings_Invalid(string key, string value)
    {
        var configuration = CreateConfiguration(($"usbipd:Session:{key}", value));

        _ = Assert.ThrowsExactly<InvalidOperationException>(() => SessionSettings.Read(configuration));
        Assert.AreEqual(SessionSettings.DefaultMaxDuration, SessionSettings.ReadOrDefault(configuration).MaxDuration);
    }

    [TestMethod]
    public void TryClaim_OneSessionPerDevice()
    {
        var registry = new SessionRegistry(CreateConfiguration());

        using var first = registry.TryClaim(Device1, "alice", IPAddress.Loopback);
        using var second = registry.TryClaim(Device1, "bob", IPAddress.Loopback);
        using var other = registry.TryClaim(Device2, "bob", IPAddress.Loopback);

        Assert.IsNotNull(first);
        Assert.IsNull(second);
        Assert.IsNotNull(other);
        Assert.HasCount(2, registry.Snapshot());
    }

    [TestMethod]
    public void TryClaim_Concurrent_ExactlyOneWins()
    {
        var registry = new SessionRegistry(CreateConfiguration());

        var sessions = Enumerable.Range(0, 64).AsParallel()
            .Select(i => registry.TryClaim(Device1, $"user{i}", IPAddress.Loopback)).ToList();

        Assert.AreEqual(1, sessions.Count(s => s is not null));
        foreach (var session in sessions)
        {
            session?.Dispose();
        }
    }

    [TestMethod]
    public void Dispose_ReleasesDevice()
    {
        var registry = new SessionRegistry(CreateConfiguration());

        var session = registry.TryClaim(Device1, "alice", IPAddress.Loopback);
        Assert.IsNotNull(session);
        session.Dispose();
        session.Dispose(); // twice is fine
        session.Revoke("after dispose"); // does not throw

        using var again = registry.TryClaim(Device1, "bob", IPAddress.Loopback);
        Assert.IsNotNull(again);
    }

    [TestMethod]
    public void Revoke_FirstReasonWins()
    {
        var registry = new SessionRegistry(CreateConfiguration());
        using var session = registry.TryClaim(Device1, "alice", IPAddress.Loopback);
        Assert.IsNotNull(session);

        Assert.IsNull(session.EndReason);
        Assert.IsFalse(session.Token.IsCancellationRequested);

        session.Revoke("first");
        session.Revoke("second");

        Assert.AreEqual("first", session.EndReason);
        Assert.IsTrue(session.Token.IsCancellationRequested);
    }

    [TestMethod]
    public async Task StartClock_RevokesAfterMaxDuration()
    {
        var registry = new SessionRegistry(CreateConfiguration());
        using var session = registry.TryClaim(Device1, "alice", IPAddress.Loopback);
        Assert.IsNotNull(session);

        session.StartClock(TimeSpan.FromMilliseconds(50));
        await Task.Delay(TimeSpan.FromSeconds(5), session.Token).ContinueWith(_ => { }, TaskScheduler.Default);

        Assert.IsTrue(session.Token.IsCancellationRequested);
        Assert.IsNotNull(session.EndReason);
        Assert.Contains("session limit", session.EndReason);
    }

    [TestMethod]
    public void Monitor_RevokesRemovedAndRemappedUsers()
    {
        var registry = new SessionRegistry(CreateConfiguration());
        var map = new FakeUserDeviceMap();
        map.Map["alice"] = Device1;
        map.Map["bob"] = Device2;
        map.Map["carol"] = Device2;

        using var alice = registry.TryClaim(Device1, "alice", IPAddress.Loopback);
        using var bob = registry.TryClaim(Device2, "bob", IPAddress.Loopback);
        Assert.IsNotNull(alice);
        Assert.IsNotNull(bob);

        using var monitor = new SessionMonitor(registry, map, NullLogger<SessionMonitor>.Instance);
        monitor.CheckSessions();
        Assert.IsNull(alice.EndReason);
        Assert.IsNull(bob.EndReason);

        _ = map.Map.Remove("alice");
        map.Map["bob"] = Device1;
        monitor.CheckSessions();

        Assert.AreEqual("the user was removed or the device is no longer shared", alice.EndReason);
        Assert.AreEqual("the user's device mapping changed", bob.EndReason);
    }
}
