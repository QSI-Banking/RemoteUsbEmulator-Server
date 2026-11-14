// SPDX-FileCopyrightText: 2022 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using Usbipd.Automation;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using static Usbipd.Interop.VBoxUsb;

namespace Usbipd;

static partial class ConsoleTools
{
    public static IEnumerable<string> Wrap(string text, int width)
    {
        var lineBuilder = new StringBuilder(Math.Min(text.Length, width) + 2);

        void FirstWord(string word)
        {
            _ = lineBuilder.Append(word);
            if (word.Length == width)
            {
                // If the first word on a line is exactly the original width, add an extra space
                // so Windows Terminal does not glue the next word to it on resize.
                _ = lineBuilder.Append(' ');
            }
        }

        string Flush()
        {
            var result = lineBuilder.ToString();
            _ = lineBuilder.Clear();
            return result;
        }

        foreach (var line in text.Split('\n'))
        {
            foreach (var word in line.Split(' '))
            {
                if (lineBuilder.Length == 0)
                {
                    FirstWord(word);
                }
                else if (lineBuilder.Length + 1 + word.Length >= width)
                {
                    // Need to stay *under* the width so Windows Terminal does not automatically
                    // glue the first word of the next line to this line without whitespace on resize.
                    yield return Flush();
                    FirstWord(word);
                }
                else
                {
                    _ = lineBuilder.Append(' ').Append(word);
                }
            }
            yield return Flush();
        }
    }

    public static string Unwrap(this string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\\\n", "", StringComparison.Ordinal);
    }

    public static void WriteTruncated(this IConsole console, string text, int width, bool fill)
    {
        // Console: depending on terminal / font / etc. international characters can take more than 1 cell.
        // Redirected: just assume every character has width 1.
        var measureConsole = !console.IsOutputRedirected;
        if (measureConsole)
        {
            // We need at least 2 extra characters to be able to measure the console output:
            // international characters may take up 2 cells, and the cursor should not wrap around yet.
            if (console.CursorLeft + width + 2 >= console.WindowWidth)
            {
                // The console is not wide enough; we cannot measure across line wrapping.
                measureConsole = false;
            }
        }
        if (measureConsole)
        {
            var start = console.CursorLeft;
            foreach (var c in text)
            {
                console.Out.Write($"{c}");
                if (console.CursorLeft - start > width)
                {
                    console.CursorLeft = start + width - 3;
                    console.Out.Write("...");
                    break;
                }
            }
            if (fill)
            {
                console.Out.Write(new string(' ', width - (console.CursorLeft - start)));
            }
        }
        else
        {
            if (text.Length > width)
            {
                console.Out.Write(text[..(width - 3)]);
                console.Out.Write("...");
            }
            else
            {
                console.Out.Write(text);
                if (fill)
                {
                    console.Out.Write(new string(' ', width - text.Length));
                }
            }
        }
    }

    /// <summary>
    /// <para>Some "old style" errors don't have a terminating period.</para>
    /// <para>Some WinAPI errors (originating from FormatMessage) have a terminating newline.</para>
    /// This function normalizes all errors to
    /// <list type="bullet">
    /// <item>end with a period (.)</item>
    /// <item>not end with a newline</item>
    /// </list>
    /// </summary>
    static string EnforceFinalPeriod(this string s)
    {
        s = s.TrimEnd();
        return s.EndsWith('.') ? s : s + '.';
    }

    /// <summary>
    /// Errors go to <see cref="Console.Error"/>. Information and warnings are not failures, so they go to
    /// <see cref="Console.Out"/>: PowerShell shows anything on the error stream as an error record.
    /// When the output is redirected (piped or captured, for example 'usbipd state' into a JSON parser),
    /// they go to <see cref="Console.Error"/> instead, so that the output stays clean data.
    /// </summary>
    static void ReportText(this IConsole console, string level, string text, bool toOutput)
    {
        var writer = toOutput ? console.Out : console.Error;
        writer.WriteLine($"{AppDomain.CurrentDomain.FriendlyName}: {level}: {EnforceFinalPeriod(text)}");
    }

    /// <summary>
    /// Information and warnings: the output, unless that is redirected (see <see cref="ReportText"/>).
    /// </summary>
    static bool ReportsToOutput(IConsole console)
    {
        return !console.IsOutputRedirected;
    }

    sealed partial class TemporaryColor
        : IDisposable
    {
        readonly bool NeedReset;

        public TemporaryColor(IConsole console, ConsoleColor color, bool toOutput)
        {
            if (toOutput ? console.IsOutputRedirected : console.IsErrorRedirected)
            {
                return;
            }
            Console.ForegroundColor = color;
            NeedReset = true;
        }

        public void Dispose()
        {
            if (NeedReset)
            {
                Console.ResetColor();
            }
        }
    }

    public static void ReportError(this IConsole console, string text)
    {
        using var color = new TemporaryColor(console, ConsoleColor.Red, false);
        console.ReportText("error", text, false);
    }

    public static void ReportWarning(this IConsole console, string text)
    {
        var toOutput = ReportsToOutput(console);
        using var color = new TemporaryColor(console, ConsoleColor.Yellow, toOutput);
        console.ReportText("warning", text, toOutput);
    }

    public static void ReportInfo(this IConsole console, string text)
    {
        var toOutput = ReportsToOutput(console);
        using var color = new TemporaryColor(console, ConsoleColor.DarkGray, toOutput);
        console.ReportText("info", text, toOutput);
    }

    public static void ReportWin32Error(this IConsole console, string function, WIN32_ERROR error)
    {
        console.ReportError($"{function}: {error} ({(int)error}): {Marshal.GetPInvokeErrorMessage((int)error)}");
    }

    public static void ReportConfigRet(this IConsole console, string function, CONFIGRET configRet)
    {
        var error = PInvoke.CM_MapCrToWin32Err(configRet, (uint)WIN32_ERROR.ERROR_CAN_NOT_COMPLETE);
        console.ReportError($"{function}: {configRet} ({(int)configRet}): {error} ({(int)error}): {Marshal.GetPInvokeErrorMessage((int)error)}");
    }

    public static void ReportLastWin32Error(this IConsole console, string function)
    {
        ReportWin32Error(console, function, (WIN32_ERROR)Marshal.GetLastPInvokeError());
    }

    /// <summary>
    /// Helper to display an error or warning if something is not in order.
    ///
    /// <para>
    /// For example, 'list or 'bind' will work fine. But actually attaching is not going to work.
    /// Therefore, such commands will give a warning telling the user that the current action
    /// is fine, but it will fail later on unless they resolve the situation.
    /// </para>
    ///
    /// <para>
    /// For example, 'attach --wsl' will not work at all.
    /// Therefore, such commands will give an error and the current action fails.
    /// </para>
    /// </summary>
    public static bool CheckAndReportServerRunning(this IConsole console, bool error)
    {
        void Report(string text)
        {
            if (error)
            {
                console.ReportError(text);
            }
            else
            {
                console.ReportWarning(text);
            }
        }
        if (!VBoxUsbMon.IsServiceInstalled())
        {
            Report("The VBoxUsbMon driver is not correctly installed; a repair or re-install of this software should fix that.");
            return false;
        }
        if (!Server.IsRunning())
        {
            Report("The service is currently not running; a reboot should fix that.");
            return false;
        }
        if (VBoxUsbMon.GetRunningVersion() is not UsbSupVersion version)
        {
            // The usbipd service has a dependency on VBoxUsbMon, but we can still get here when running the server from the command line.
            Report("The VBoxUsbMon driver is currently not running; a reboot should fix that.");
            return false;
        }
        if (!VBoxUsbMon.IsVersionSupported(version))
        {
            // This may happen if a full installation of (a rather old version of) VirtualBox interferes.
            Report($"VBoxUsbMon version {version.major}.{version.minor} is not supported; a repair or re-install of this software may fix that.");
            return false;
        }
        return true;
    }

    public static void ReportRebootRequired(this IConsole console)
    {
        console.ReportWarning("A reboot may be required before the changes take effect.");
    }

    public static bool CheckInstalled(IConsole console)
    {
        if (UsbipdRegistry.Instance.InstallationFolder is null)
        {
            console.ReportError("The application appears not to be installed; a repair or re-install of this software may fix that.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// For every change: the process must be elevated (driver changes), and an administrator must sign in
    /// (see <see cref="AdminAuthorization"/>), also in an elevated window.
    /// </summary>
    public static bool CheckWriteAccess(IConsole console)
    {
        return CheckElevated(console) && AdminAuthorization.Authorize(console);
    }

    /// <summary>
    /// The process must run elevated; no sign-in (for example 'usbipd server' in a console).
    /// </summary>
    public static bool CheckElevated(IConsole console)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {
            console.ReportError("Access denied; this operation requires administrator privileges.");
            return false;
        }
        return true;
    }

    public static bool CheckNoStub(VidPid vidPid, IConsole console)
    {
        if (vidPid == DriverDetails.Instance.VidPid)
        {
            console.ReportError($"Manipulating the USBIP stub devices is not supported; use the original device VID:PID.");
            return false;
        }
        return true;
    }
}
