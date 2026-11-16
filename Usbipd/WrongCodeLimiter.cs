// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

namespace Usbipd;

/// <summary>
/// Counts wrong one-time passwords per user, in memory. At <see cref="Limit"/> wrong codes within <see cref="Window"/>
/// the caller drops the bind of the user's device, so no further code can be tried until an administrator binds it again.
/// </summary>
sealed class WrongCodeLimiter(TimeProvider timeProvider)
{
    public const int Limit = 3;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    readonly TimeProvider TimeProvider = timeProvider;
    readonly Lock Lock = new();
    readonly Dictionary<string, Queue<long>> WrongCodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Call for every wrong code of a user that has a shared device (so only real users are remembered).
    /// </summary>
    /// <returns><see langword="true"/> if this was the <see cref="Limit"/>th wrong code within <see cref="Window"/>; the count then starts again.</returns>
    public bool RecordWrongCode(string userName)
    {
        var now = TimeProvider.GetTimestamp();
        lock (Lock)
        {
            if (!WrongCodes.TryGetValue(userName, out var times))
            {
                times = new();
                WrongCodes[userName] = times;
            }
            while (times.TryPeek(out var oldest) && TimeProvider.GetElapsedTime(oldest, now) >= Window)
            {
                _ = times.Dequeue();
            }
            times.Enqueue(now);
            if (times.Count < Limit)
            {
                return false;
            }
            _ = WrongCodes.Remove(userName);
            return true;
        }
    }
}
