// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel.DataAnnotations;

namespace Usbipd;

/// <summary>
/// Session limits from the "qsiusbd:Session" configuration section (appsettings.json next to qsiusbd.exe).
/// </summary>
sealed class SessionSettings
{
    public const string SectionName = "qsiusbd:Session";

    /// <summary>
    /// An attached device is detached this long after the attach, whatever the activity (no warning).
    /// </summary>
    [Range(1, 1440)]
    public int MaxDurationMinutes { get; set; } = 10;

    /// <summary>
    /// The sign-in must follow the TLS handshake within this time, and the import must follow the sign-in within it.
    /// </summary>
    [Range(1, 300)]
    public int ImportTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How often active sessions are checked against the current user mappings.
    /// </summary>
    [Range(1, 60)]
    public int RecheckIntervalSeconds { get; set; } = 2;

    public TimeSpan MaxDuration => TimeSpan.FromMinutes(MaxDurationMinutes);
    public TimeSpan ImportTimeout => TimeSpan.FromSeconds(ImportTimeoutSeconds);
    public TimeSpan RecheckInterval => TimeSpan.FromSeconds(RecheckIntervalSeconds);

    /// <exception cref="InvalidOperationException">A value is invalid.</exception>
    public static SessionSettings Read(IConfiguration configuration) => SettingsReader.Read(configuration, SectionName, new SessionSettings());

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
}
