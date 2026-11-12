// SPDX-FileCopyrightText: 2024 Frans van Dorsselaer
//
// SPDX-License-Identifier: GPL-3.0-only

using System.Net;
using Usbipd.Automation;

namespace Usbipd;

interface ICommandHandlers
{
    // CLI
    Task<ExitCode> AttachWsl(BusId busId, bool autoAttach, bool unplugged, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> AttachWsl(VidPid vidPid, bool autoAttach, string? distribution, IPAddress? hostAddress,
        IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Bind(BusId busId, bool force, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Bind(VidPid vidPid, bool force, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Detach(BusId busId, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Detach(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> DetachAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> License(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> List(bool usbIds, bool connectedOnly, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyAdd(PolicyRule rule, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyList(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyRemove(Guid guid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> PolicyRemoveAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> State(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(BusId busId, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(Guid guid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> Unbind(VidPid vidPid, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UnbindAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UserAdd(string userName, Guid? guid, BusId? busId, bool force, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UserList(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UserRemove(string userName, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> UserRemoveAll(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ImportRegistry(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ClientAdd(string? thumbprint, string? certificateFile, string name, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ClientList(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ClientEnable(string thumbprint, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ClientDisable(string thumbprint, IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> ClientRemove(string thumbprint, IConsole console, CancellationToken cancellationToken);

    // Server
    Task<ExitCode> Server(string[] args, IConsole console, CancellationToken cancellationToken);

    // Installer
    Task<ExitCode> InstallerDelay(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerInstallDriver(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerUpdateDrivers(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerInstallMonitor(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerUninstallDriver(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerUninstallOldDrivers(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerUninstallStubs(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerDisableForced(IConsole console, CancellationToken cancellationToken);
    Task<ExitCode> InstallerEnableForced(IConsole console, CancellationToken cancellationToken);
}
