// SPDX-FileCopyrightText: 2026 QSI Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel.DataAnnotations;

namespace Usbipd;

/// <summary>
/// Reads a configuration section into a settings object with the .NET configuration binder, then checks the
/// DataAnnotations attributes ([Range] and so on) of its properties.
/// </summary>
static class SettingsReader
{
    /// <exception cref="InvalidOperationException">A value has the wrong type (from the binder) or is out of range.</exception>
    public static T Read<T>(IConfiguration configuration, string sectionName, T settings) where T : class
    {
        configuration.GetSection(sectionName).Bind(settings);
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), errors, validateAllProperties: true))
        {
            throw new InvalidOperationException($"{sectionName}: {string.Join(" ", errors.Select(e => e.ErrorMessage))}");
        }
        return settings;
    }
}
