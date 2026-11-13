// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using static Usbipd.ConsoleTools;

namespace Usbipd;

/// <summary>
/// 'usbipd admin': sign in once, then run several commands (see <see cref="AdminAuthorization"/>).
/// </summary>
sealed partial class CommandHandlers : ICommandHandlers
{
    static readonly string[] CommandsNotInSession = ["admin", "server", "installer"];

    async Task<ExitCode> ICommandHandlers.Admin(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return ExitCode.Failure;
        }
        if (Console.IsInputRedirected)
        {
            console.ReportError("An admin session needs an interactive console.");
            return ExitCode.Failure;
        }

        AdminAuthorization.InSession = true;
        try
        {
            if (!CheckWriteAccess(console))
            {
                return ExitCode.AccessDenied;
            }
#pragma warning disable CA1849 // Call async methods when in an async method (console output, as the other commands)
            console.Out.WriteLine($"""
                Type usbipd commands without 'usbipd', for example: user add --name alice --busid 6-2
                'help' lists the commands, 'lock' signs out, 'exit' ends the session.
                The session locks after {(int)AdminAuthorization.SessionIdleTimeout.TotalMinutes} minutes without a command, and after {(int)AdminAuthorization.SessionMaxDuration.TotalMinutes} minutes in total; the next change then asks again.
                """);
            while (!cancellationToken.IsCancellationRequested)
            {
                console.Out.Write("usbipd> ");
                var line = await Console.In.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }
                var args = SplitCommandLine(line);
                if (args.Count > 0 && args[0].ToUpperInvariant() is "USBIPD" or "USBIPD.EXE")
                {
                    args.RemoveAt(0);
                }
                if (args.Count == 0)
                {
                    continue;
                }
                switch (args[0].ToUpperInvariant())
                {
                    case "EXIT":
                    case "QUIT":
                        return ExitCode.Success;
                    case "LOCK":
                        AdminAuthorization.SignOut();
                        console.ReportInfo("Signed out; the next change asks for credentials again.");
                        continue;
                    case "HELP":
                        args = ["--help"];
                        break;
                }
                if (CommandsNotInSession.Contains(args[0], StringComparer.OrdinalIgnoreCase))
                {
                    console.ReportError($"'{args[0]}' is not available in an admin session.");
                    continue;
                }
                AdminAuthorization.Touch();
                _ = await Program.Run(console, this, [.. args]);
            }
#pragma warning restore CA1849
            return ExitCode.Success;
        }
        finally
        {
            AdminAuthorization.InSession = false;
            AdminAuthorization.SignOut();
        }
    }

    /// <summary>
    /// Splits a typed command line: spaces separate arguments, double quotes group them ("Lab PC 42").
    /// Backslashes are literal, so Windows paths need no escaping.
    /// </summary>
    internal static List<string> SplitCommandLine(string line)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasArgument = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasArgument = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasArgument)
                {
                    args.Add(current.ToString());
                    _ = current.Clear();
                    hasArgument = false;
                }
            }
            else
            {
                _ = current.Append(c);
                hasArgument = true;
            }
        }
        if (hasArgument)
        {
            args.Add(current.ToString());
        }
        return args;
    }
}
