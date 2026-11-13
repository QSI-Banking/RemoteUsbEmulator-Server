// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using static Usbipd.AdminAuthorization.UnsafeNativeMethods;

namespace Usbipd;

/// <summary>
/// Every change asks for an administrator's Windows credentials, so that an elevated window that was left open cannot be
/// used by someone else. The check is valid for one command, or for an admin session ('usbipd admin') until it was idle for
/// <see cref="SessionIdleTimeout"/> or lasted <see cref="SessionMaxDuration"/>; a timer then discards the sign-in token from memory.
/// Database connections of the CLI are made as the checked account
/// (see <see cref="Data.ImpersonatingConnectionInterceptor"/>), so the audit names the person who made the change.
/// </summary>
static partial class AdminAuthorization
{
    public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan SessionMaxDuration = TimeSpan.FromMinutes(60);

    const int MaxAttempts = 3;
    static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly Lock Lock = new();

    static WindowsIdentity? Current;
    static long LastUsedTimestamp;
    static long SignedInTimestamp;
    static Timer? ExpiryTimer;
    static string? LockReason;

    /// <summary>
    /// Set by 'usbipd admin' while its session runs; otherwise a check is valid for the current command only.
    /// </summary>
    public static bool InSession { get; set; }

    /// <summary>
    /// Runs <paramref name="action"/> as the checked account, or as the process account if there is none.
    /// Holds the lock, so the expiry timer cannot discard the token while it is in use.
    /// </summary>
    public static void RunAsAdministrator(Action action)
    {
        lock (Lock)
        {
            if (IsValid())
            {
                WindowsIdentity.RunImpersonated(Current!.AccessToken, action);
                return;
            }
        }
        action();
    }

    /// <summary>
    /// The Windows account recorded as the author of a change.
    /// </summary>
    public static string AccountName
    {
        get
        {
            lock (Lock)
            {
                if (IsValid())
                {
                    return Current!.Name;
                }
            }
            using var processIdentity = WindowsIdentity.GetCurrent();
            return processIdentity.Name;
        }
    }

    /// <summary>
    /// Asks for administrator credentials unless they were checked recently in this admin session.
    /// The installer runs as LocalSystem, which is not asked.
    /// </summary>
    /// <returns><see langword="false"/> if the credentials were not given or not valid (reported).</returns>
    public static bool Authorize(IConsole console)
    {
        using (var processIdentity = WindowsIdentity.GetCurrent())
        {
            if (processIdentity.IsSystem)
            {
                return true;
            }
        }
        lock (Lock)
        {
            if (IsValid())
            {
                LastUsedTimestamp = Stopwatch.GetTimestamp();
                ScheduleExpiry();
                return true;
            }
            if (Current is not null)
            {
                LockReason ??= ExpiredReason();
            }
            DiscardToken();
            if (LockReason is not null)
            {
                console.ReportInfo($"The admin session was locked: {LockReason}.");
                LockReason = null;
            }
        }

        var message = InSession
            ? "Enter the credentials of a local administrator for this usbipd admin session."
            : "Enter the credentials of a local administrator to make this change.";
        uint authError = 0;
        for (var attempt = 0; attempt < MaxAttempts; ++attempt)
        {
            if (!PromptForCredentials(console, message, authError, out var userName, out var domain, out var password))
            {
                return false;
            }
            try
            {
                if (!LogonUser(userName, domain, password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, out var token))
                {
                    authError = (uint)Marshal.GetLastPInvokeError();
                    token.Dispose();
                    console.ReportError($"Sign in failed: {new Win32Exception((int)authError).Message}");
                    continue;
                }
                using (token)
                {
                    if (!IsAdministrator(token))
                    {
                        console.ReportError($"'{domain}\\{userName}' is not a member of the local Administrators group.");
                        authError = ERROR_ACCESS_DENIED;
                        continue;
                    }
                    string name;
                    lock (Lock)
                    {
                        DiscardToken();
                        Current = new WindowsIdentity(token.DangerousGetHandle());
                        name = Current.Name;
                        SignedInTimestamp = LastUsedTimestamp = Stopwatch.GetTimestamp();
                        ScheduleExpiry();
                    }
                    if (InSession)
                    {
                        console.ReportInfo($"Signed in as {name}; the session locks after {(int)SessionIdleTimeout.TotalMinutes} idle minutes, " +
                            $"and after {(int)SessionMaxDuration.TotalMinutes} minutes in total.");
                    }
                    return true;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));
            }
        }
        console.ReportError("Too many failed attempts.");
        return false;
    }

    /// <summary>
    /// Called for every command in an admin session: the idle time counts from the last command.
    /// </summary>
    public static void Touch()
    {
        lock (Lock)
        {
            if (IsValid())
            {
                LastUsedTimestamp = Stopwatch.GetTimestamp();
                ScheduleExpiry();
            }
        }
    }

    /// <summary>
    /// Forgets the checked account ('exit' or 'lock' in an admin session).
    /// </summary>
    public static void SignOut()
    {
        lock (Lock)
        {
            DiscardToken();
            LockReason = null;
        }
    }

    static bool IsValid()
    {
        return Current is not null && (!InSession
            || (Stopwatch.GetElapsedTime(LastUsedTimestamp) < SessionIdleTimeout
                && Stopwatch.GetElapsedTime(SignedInTimestamp) < SessionMaxDuration));
    }

    static string ExpiredReason()
    {
        return Stopwatch.GetElapsedTime(SignedInTimestamp) >= SessionMaxDuration
            ? $"the {(int)SessionMaxDuration.TotalMinutes} minute session limit was reached"
            : $"no command for {(int)SessionIdleTimeout.TotalMinutes} minutes";
    }

    /// <summary>
    /// Call with the lock held. In an admin session, arms the timer for the earlier of the idle and the total limit.
    /// </summary>
    static void ScheduleExpiry()
    {
        if (!InSession)
        {
            return;
        }
        var idleLeft = SessionIdleTimeout - Stopwatch.GetElapsedTime(LastUsedTimestamp);
        var totalLeft = SessionMaxDuration - Stopwatch.GetElapsedTime(SignedInTimestamp);
        var due = idleLeft < totalLeft ? idleLeft : totalLeft;
        if (due < TimeSpan.Zero)
        {
            due = TimeSpan.Zero;
        }
        ExpiryTimer ??= new Timer(_ => OnExpiryTimer());
        _ = ExpiryTimer.Change(due + TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
    }

    static void OnExpiryTimer()
    {
        lock (Lock)
        {
            if (Current is null)
            {
                return;
            }
            if (IsValid())
            {
                // A command came in after the timer was armed.
                ScheduleExpiry();
                return;
            }
            // Discarded now; reported at the next change.
            LockReason = ExpiredReason();
            DiscardToken();
        }
    }

    /// <summary>
    /// Call with the lock held.
    /// </summary>
    static void DiscardToken()
    {
        _ = ExpiryTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        Current?.Dispose();
        Current = null;
    }

    /// <summary>
    /// With UAC, the token of an administrator has the Administrators group as deny-only; it is still a member.
    /// </summary>
    static unsafe bool IsAdministrator(SafeAccessTokenHandle token)
    {
        _ = GetTokenInformation(token, TokenGroups, null, 0, out var length);
        var buffer = new byte[length];
        fixed (byte* p = buffer)
        {
            if (!GetTokenInformation(token, TokenGroups, p, length, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), nameof(GetTokenInformation));
            }
            var count = *(uint*)p;
            // TOKEN_GROUPS: DWORD GroupCount, then SID_AND_ATTRIBUTES[] aligned to the pointer size.
            var groups = (SID_AND_ATTRIBUTES*)(p + IntPtr.Size);
            for (var i = 0; i < count; ++i)
            {
                if (new SecurityIdentifier(groups[i].Sid) == AdministratorsSid)
                {
                    return true;
                }
            }
        }
        return false;
    }

    static unsafe bool PromptForCredentials(IConsole console, string message, uint authError, out string userName, out string domain,
        out char[] password)
    {
        userName = string.Empty;
        domain = string.Empty;
        password = [];
        fixed (char* caption = "usbipd")
        fixed (char* text = message)
        {
            var info = new CREDUI_INFOW
            {
                cbSize = (uint)sizeof(CREDUI_INFOW),
                hwndParent = GetConsoleWindow(),
                pszMessageText = text,
                pszCaptionText = caption,
            };
            uint authPackage = 0;
            var save = 0;
            var result = CredUIPromptForWindowsCredentialsW(&info, authError, &authPackage, null, 0, out var outBuffer, out var outSize,
                &save, CREDUIWIN_GENERIC);
            if (result == ERROR_CANCELLED)
            {
                console.ReportError("Canceled; the change requires the credentials of a local administrator.");
                return false;
            }
            if (result != 0)
            {
                console.ReportError($"Cannot show the Windows sign-in dialog: {new Win32Exception((int)result).Message}. " +
                    "Changes require a desktop session (for example Remote Desktop).");
                return false;
            }
            try
            {
                var user = new char[514];
                var domainBuffer = new char[338];
                var passwordBuffer = new char[514];
                uint userLength = (uint)user.Length;
                uint domainLength = (uint)domainBuffer.Length;
                uint passwordLength = (uint)passwordBuffer.Length;
                fixed (char* pUser = user)
                fixed (char* pDomain = domainBuffer)
                fixed (char* pPassword = passwordBuffer)
                {
                    if (!CredUnPackAuthenticationBufferW(0, outBuffer, outSize, pUser, &userLength, pDomain, &domainLength,
                        pPassword, &passwordLength))
                    {
                        CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordBuffer.AsSpan()));
                        console.ReportError($"Cannot read the credentials: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");
                        return false;
                    }
                }
                var fullName = TerminatedString(user);
                domain = TerminatedString(domainBuffer);
                // LogonUserW needs the terminating null.
                var passwordChars = Array.IndexOf(passwordBuffer, '\0');
                password = new char[(passwordChars < 0 ? passwordBuffer.Length : passwordChars) + 1];
                Array.Copy(passwordBuffer, password, password.Length - 1);
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordBuffer.AsSpan()));
                (userName, domain) = SplitUserName(fullName, domain);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(new Span<byte>((void*)outBuffer, (int)outSize));
                Marshal.FreeCoTaskMem(outBuffer);
            }
        }
    }

    static string TerminatedString(char[] buffer)
    {
        var length = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, length < 0 ? buffer.Length : length);
    }

    /// <summary>
    /// DOMAIN\user, .\user (local), user@domain (UPN, no domain), or a bare user name (local).
    /// </summary>
    internal static (string UserName, string Domain) SplitUserName(string name, string domain)
    {
        if (!string.IsNullOrEmpty(domain))
        {
            return (name, domain);
        }
        var backslash = name.IndexOf('\\', StringComparison.Ordinal);
        if (backslash >= 0)
        {
            return (name[(backslash + 1)..], name[..backslash]);
        }
        return name.Contains('@', StringComparison.Ordinal) ? (name, string.Empty) : (name, ".");
    }

    static bool LogonUser(string userName, string domain, char[] password, uint logonType, uint provider, out SafeAccessTokenHandle token)
    {
        unsafe
        {
            fixed (char* pPassword = password)
            {
                return LogonUserW(userName, string.IsNullOrEmpty(domain) ? null : domain, pPassword, logonType, provider, out token);
            }
        }
    }

    internal static partial class UnsafeNativeMethods
    {
        internal const uint LOGON32_LOGON_INTERACTIVE = 2;
        internal const uint LOGON32_PROVIDER_DEFAULT = 0;
        internal const uint CREDUIWIN_GENERIC = 0x1;
        internal const uint ERROR_CANCELLED = 1223;
        internal const uint ERROR_ACCESS_DENIED = 5;
        internal const int TokenGroups = 2;

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct CREDUI_INFOW
        {
            public uint cbSize;
            public nint hwndParent;
            public char* pszMessageText;
            public char* pszCaptionText;
            public nint hbmBanner;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SID_AND_ATTRIBUTES
        {
            public nint Sid;
            public uint Attributes;
        }

        [LibraryImport("credui.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static unsafe partial uint CredUIPromptForWindowsCredentialsW(CREDUI_INFOW* pUiInfo, uint dwAuthError, uint* pulAuthPackage,
            void* pvInAuthBuffer, uint ulInAuthBufferSize, out nint ppvOutAuthBuffer, out uint pulOutAuthBufferSize, int* pfSave, uint dwFlags);

        [LibraryImport("credui.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool CredUnPackAuthenticationBufferW(uint dwFlags, nint pAuthBuffer, uint cbAuthBuffer, char* pszUserName,
            uint* pcchMaxUserName, char* pszDomainName, uint* pcchMaxDomainName, char* pszPassword, uint* pcchMaxPassword);

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool LogonUserW(string lpszUsername, string? lpszDomain, char* lpszPassword, uint dwLogonType,
            uint dwLogonProvider, out SafeAccessTokenHandle phToken);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool GetTokenInformation(SafeAccessTokenHandle tokenHandle, int tokenInformationClass, void* tokenInformation,
            uint tokenInformationLength, out uint returnLength);

        [LibraryImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static partial nint GetConsoleWindow();
    }
}
