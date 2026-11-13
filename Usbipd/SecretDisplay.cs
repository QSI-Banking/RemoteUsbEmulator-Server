// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using static Usbipd.SecretDisplay.UnsafeNativeMethods;

namespace Usbipd;

/// <summary>
/// Shows a secret on the console's alternate screen buffer until Enter is pressed. Text on the alternate screen never
/// enters the scrollback, so the secret does not linger in the window after it was read.
/// </summary>
static partial class SecretDisplay
{
    const string EnterAlternateScreen = "\e[?1049h\e[H\e[2J";
    const string LeaveAlternateScreen = "\e[2J\e[?1049l";

    /// <summary>
    /// Call before creating a secret: a secret that cannot be shown should not replace one that works.
    /// </summary>
    /// <returns><see langword="false"/> if a secret cannot be shown here (reported).</returns>
    public static bool CheckCanShow(IConsole console)
    {
        if (Console.IsOutputRedirected || Console.IsInputRedirected || console.IsOutputRedirected)
        {
            console.ReportError("A secret cannot be shown because the console input or output is redirected (it could end up in a file); " +
                "use an interactive console.");
            return false;
        }
        if (!EnableVirtualTerminal())
        {
            console.ReportError("A secret cannot be shown because this console does not support an alternate screen; " +
                "use Windows Terminal or a current Windows console.");
            return false;
        }
        return true;
    }

    /// <returns><see langword="false"/> if the secret was not shown (reported).</returns>
    public static bool Show(IConsole console, string text)
    {
        if (!CheckCanShow(console))
        {
            return false;
        }

        Console.Out.Write(EnterAlternateScreen);
        try
        {
            Console.Out.WriteLine(text);
            Console.Out.WriteLine();
            Console.Out.Write("Press Enter when the secret is in the authenticator app; it is then removed from the screen.");
            Console.Out.Flush();
            while (Console.ReadKey(intercept: true).Key != ConsoleKey.Enter)
            {
            }
        }
        finally
        {
            Console.Out.Write(LeaveAlternateScreen);
            Console.Out.Flush();
        }
        return true;
    }

    static bool EnableVirtualTerminal()
    {
        var handle = GetStdHandle(STD_OUTPUT_HANDLE);
        if (handle == 0 || handle == -1 || !GetConsoleMode(handle, out var mode))
        {
            return false;
        }
        return (mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0
            || SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }

    internal static partial class UnsafeNativeMethods
    {
        internal const int STD_OUTPUT_HANDLE = -11;
        internal const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint GetStdHandle(int nStdHandle);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
    }
}
