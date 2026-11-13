// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.EntityFrameworkCore;
using Usbipd.Data;

namespace Usbipd;

/// <summary>
/// Checks a user's one-time password against the TOTP secret in SQL Server ('usbipd user otp reset').
/// Each code is accepted once: the accepted time step is recorded, and only later steps are accepted after it.
/// </summary>
sealed class SqlUserAuthenticator(IConfiguration configuration, TimeProvider timeProvider) : IUserAuthenticator
{
    readonly IConfiguration Configuration = configuration;
    readonly TimeProvider TimeProvider = timeProvider;

    /// <exception cref="Microsoft.Data.SqlClient.SqlException">The database cannot be reached.</exception>
    public bool Authenticate(string userName, string otp)
    {
        using var db = Database.CreateContext(Database.GetConnectionString(Configuration));
        var entry = db.OtpSecrets.AsNoTracking()
            .Where(o => o.User!.Name == userName)
            .Select(o => new { o.UserId, o.Secret, o.LastAcceptedStep })
            .SingleOrDefault();
        if (entry is null)
        {
            // No such user, or no secret yet.
            return false;
        }
        var step = Totp.Verify(entry.Secret, otp, Totp.GetStep(TimeProvider.GetUtcNow()), entry.LastAcceptedStep);
        if (step is not { } acceptedStep)
        {
            return false;
        }
        // Single use: of two connections with the same code, only one moves the step forward.
        return db.OtpSecrets
            .Where(o => o.UserId == entry.UserId && o.LastAcceptedStep < acceptedStep)
            .ExecuteUpdate(s => s.SetProperty(o => o.LastAcceptedStep, acceptedStep)) == 1;
    }
}
