// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Text;
using QRCoder;
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

    /// <param name="qrContent">Also shown as a QR code (for example an otpauth:// key URI), or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> if the secret was not shown (reported).</returns>
    public static bool Show(IConsole console, string text, string? qrContent = null)
    {
        if (!CheckCanShow(console))
        {
            return false;
        }

        var outputEncoding = Console.OutputEncoding;
        Console.OutputEncoding = Encoding.UTF8; // block characters of the QR code
        Console.Out.Write(EnterAlternateScreen);
        try
        {
            var textLines = text.Split('\n').Length + 2;
            if (qrContent is not null)
            {
                var qrLines = RenderQrCode(qrContent);
                if (qrLines.Count + textLines + 1 <= Console.WindowHeight)
                {
                    // All on one screen.
                    WritePage([.. qrLines, string.Empty, text], "Press Enter when the secret is in the authenticator app; it is then removed from the screen.");
                }
                else if (WaitForHeight(qrLines.Count + 2))
                {
                    // The window is too small for both: the QR code first, then the text.
                    WritePage(qrLines, "Press Enter to continue to the secret as text.");
                    WritePage([text], "Press Enter when the secret is in the authenticator app; it is then removed from the screen.");
                }
                else
                {
                    WritePage([text], "Press Enter when the secret is in the authenticator app; it is then removed from the screen.");
                }
            }
            else
            {
                WritePage([text], "Press Enter when the secret is in the authenticator app; it is then removed from the screen.");
            }
        }
        finally
        {
            Console.Out.Write(LeaveAlternateScreen);
            Console.Out.Flush();
            Console.OutputEncoding = outputEncoding;
        }
        return true;
    }

    static void WritePage(IEnumerable<string> lines, string prompt)
    {
        Console.Out.Write("\e[H\e[2J");
        foreach (var line in lines)
        {
            Console.Out.WriteLine(line);
        }
        Console.Out.WriteLine();
        Console.Out.Write(prompt);
        Console.Out.Flush();
        while (Console.ReadKey(intercept: true).Key != ConsoleKey.Enter)
        {
        }
    }

    /// <summary>
    /// The alternate screen has no scrollback, so the QR code must fit in the window; asks to make it taller if not.
    /// </summary>
    /// <returns><see langword="false"/> if the user skipped the QR code (Esc).</returns>
    static bool WaitForHeight(int lines)
    {
        while (Console.WindowHeight < lines)
        {
            Console.Out.Write("\e[H\e[2J");
            Console.Out.Write($"The QR code needs a window of at least {lines} lines (now {Console.WindowHeight}). " +
                "Make the window taller (or the font smaller) and press Enter, or press Esc to show the secret as text only.");
            Console.Out.Flush();
            ConsoleKey key;
            do
            {
                key = Console.ReadKey(intercept: true).Key;
            } while (key is not (ConsoleKey.Enter or ConsoleKey.Escape));
            if (key == ConsoleKey.Escape)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Dark modules on a light background (as phone cameras expect, also in a dark console), two module rows per text line,
    /// with a quiet zone of 2 modules (enough for phone cameras, and 2 lines shorter than the standard 4).
    /// </summary>
    static List<string> RenderQrCode(string content)
    {
        const int QuietZone = 2;
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.L);
        var matrix = data.ModuleMatrix;
        // The matrix includes the standard quiet zone of 4 modules.
        var first = 4 - QuietZone;
        var last = matrix.Count - 1 - first;
        var lines = new List<string>();
        var builder = new StringBuilder();
        for (var y = first; y <= last; y += 2)
        {
            _ = builder.Clear().Append("  \e[30;107m");
            for (var x = first; x <= last; ++x)
            {
                var top = matrix[y][x];
                var bottom = y + 1 <= last && matrix[y + 1][x];
                _ = builder.Append((top, bottom) switch
                {
                    (true, true) => '█',
                    (true, false) => '▀',
                    (false, true) => '▄',
                    _ => ' ',
                });
            }
            lines.Add(builder.Append("\e[0m").ToString());
        }
        return lines;
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
