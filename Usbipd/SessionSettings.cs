// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;

namespace Usbipd;

/// <summary>
/// Session limits from the "usbipd:Session" configuration section (appsettings.json next to usbipd.exe).
/// </summary>
sealed class SessionSettings
{
    public const string SectionName = "usbipd:Session";
    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultImportTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRecheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// An attached device is detached this long after the attach, whatever the activity (no warning).
    /// </summary>
    public TimeSpan MaxDuration { get; private init; } = DefaultMaxDuration;

    /// <summary>
    /// After a successful OP_REQ_AUTH, the client must send OP_REQ_IMPORT within this time.
    /// </summary>
    public TimeSpan ImportTimeout { get; private init; } = DefaultImportTimeout;

    /// <summary>
    /// How often active sessions are checked against the current user mappings.
    /// </summary>
    public TimeSpan RecheckInterval { get; private init; } = DefaultRecheckInterval;

    /// <exception cref="InvalidOperationException">A value is invalid.</exception>
    public static SessionSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new()
        {
            MaxDuration = ReadValue(section, "MaxDurationMinutes", 1, 1440, DefaultMaxDuration, TimeSpan.FromMinutes),
            ImportTimeout = ReadValue(section, "ImportTimeoutSeconds", 1, 300, DefaultImportTimeout, TimeSpan.FromSeconds),
            RecheckInterval = ReadValue(section, "RecheckIntervalSeconds", 1, 60, DefaultRecheckInterval, TimeSpan.FromSeconds),
        };
    }

    /// <summary>
    /// Like <see cref="Read"/>, but falls back to the defaults. The service validates with <see cref="Read"/> at start.
    /// </summary>
    public static SessionSettings ReadOrDefault(IConfiguration configuration)
    {
        try
        {
            return Read(configuration);
        }
        catch (InvalidOperationException)
        {
            return new();
        }
    }

    static TimeSpan ReadValue(IConfigurationSection section, string key, int minimum, int maximum, TimeSpan defaultValue,
        Func<double, TimeSpan> toTimeSpan)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
        {
            throw new InvalidOperationException($"{SectionName}:{key} '{value}' must be a number from {minimum} to {maximum}.");
        }
        return toTimeSpan(number);
    }
}
