// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd;

namespace UnitTests;

[TestClass]
sealed class WrongCodeLimiter_Tests
{
    sealed class TestTime : TimeProvider
    {
        long Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Ticks;

        public void Advance(TimeSpan time) => Ticks += time.Ticks;
    }

    [TestMethod]
    public void ThirdWrongCodeWithinAMinute_ReachesLimit()
    {
        var time = new TestTime();
        var limiter = new WrongCodeLimiter(time);
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        time.Advance(TimeSpan.FromSeconds(20));
        Assert.IsTrue(limiter.RecordWrongCode("alice"));
    }

    [TestMethod]
    public void OldWrongCodes_AreForgotten()
    {
        var time = new TestTime();
        var limiter = new WrongCodeLimiter(time);
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsTrue(limiter.RecordWrongCode("alice"));
    }

    [TestMethod]
    public void Users_AreCountedSeparately_IgnoringCase()
    {
        var limiter = new WrongCodeLimiter(new TestTime());
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsFalse(limiter.RecordWrongCode("bob"));
        Assert.IsFalse(limiter.RecordWrongCode("ALICE"));
        Assert.IsFalse(limiter.RecordWrongCode("bob"));
        Assert.IsTrue(limiter.RecordWrongCode("Alice"));
    }

    [TestMethod]
    public void CountStartsAgain_AfterTheLimit()
    {
        var limiter = new WrongCodeLimiter(new TestTime());
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
        Assert.IsTrue(limiter.RecordWrongCode("alice"));
        Assert.IsFalse(limiter.RecordWrongCode("alice"));
    }
}
