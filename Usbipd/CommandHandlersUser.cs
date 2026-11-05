// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Usbipd.Automation;
using static Usbipd.ConsoleTools;

namespace Usbipd;

sealed partial class CommandHandlers : ICommandHandlers
{
    Task<ExitCode> ICommandHandlers.UserAdd(string userName, Guid? guid, BusId? busId, bool force, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        Device? device;
        if (guid.HasValue)
        {
            device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.PersistedGuid == guid.Value);
            if (device is null)
            {
                console.ReportError($"There is no shared device with guid '{guid.Value:D}'; run 'usbipd bind' as administrator first.");
                return Task.FromResult(ExitCode.Failure);
            }
        }
        else if (busId.HasValue)
        {
            device = UsbipdRegistry.Instance.GetBoundDevices().SingleOrDefault(d => d.BusId.HasValue && d.BusId.Value == busId.Value);
            if (device is null)
            {
                console.ReportError($"There is no shared device with busid '{busId.Value}'; run 'usbipd bind --busid {busId.Value}' as administrator first.");
                return Task.FromResult(ExitCode.Failure);
            }
        }
        else
        {
            throw new UnexpectedResultException("Either guid or busid is required.");
        }

        var deviceGuid = device.PersistedGuid.GetValueOrDefault();
        var existing = UsbipdRegistry.Instance.GetUserDevice(userName);
        if (existing.HasValue && !force)
        {
            console.ReportError($"User '{userName}' is already mapped to guid '{existing.Value:D}'; use '--force' to replace the mapping.");
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        UsbipdRegistry.Instance.SetUserDevice(userName, deviceGuid);
        console.ReportInfo($"User '{userName}' mapped to '{device.Description}' with guid '{deviceGuid:D}'.");
        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.UserList(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

#pragma warning disable CA1849 // Call async methods when in an async method
        var users = UsbipdRegistry.Instance.GetUserDevices();
        var boundDevices = UsbipdRegistry.Instance.GetBoundDevices().ToList();
        console.Out.WriteLine("Users:");
        console.Out.WriteLine($"{"USER",-24}  {"GUID",-36}  {"BUSID",-5}  {"STATE",-22}  DEVICE");
        foreach (var (userName, deviceGuid) in users)
        {
            var device = boundDevices.SingleOrDefault(d => d.PersistedGuid == deviceGuid);
            var state = device is null ? "Bind missing"
                : !device.BusId.HasValue ? "Not present"
                : device.ClientIPAddress is not null ? $"Attached ({device.ClientIPAddress})"
                : "Shared";
            var busIdText = device is not null && device.BusId.HasValue ? device.BusId.Value.ToString() : "-";
            console.Out.Write($"{userName,-24}  ");
            console.Out.Write($"{deviceGuid,-36:D}  ");
            console.Out.Write($"{busIdText,-5}  ");
            console.Out.Write($"{state,-22}  ");
            console.Out.WriteLine(device?.Description ?? "-");
        }
        console.Out.WriteLine();
        if (!users.ContainsKey(HardcodedUserAuthenticator.UserName))
        {
            // TEMPORARY: see RegistryUserDeviceMap.FallbackDeviceGuid.
            console.ReportInfo($"Built-in test user '{HardcodedUserAuthenticator.UserName}' uses guid '{RegistryUserDeviceMap.FallbackDeviceGuid:D}' unless mapped above.");
        }
        return Task.FromResult(ExitCode.Success);
#pragma warning restore CA1849 // Call async methods when in an async method
    }

    Task<ExitCode> ICommandHandlers.UserRemove(string userName, IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!UsbipdRegistry.Instance.GetUserDevice(userName).HasValue)
        {
            console.ReportError($"There is no user '{userName}'.");
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        _ = UsbipdRegistry.Instance.RemoveUser(userName);
        return Task.FromResult(ExitCode.Success);
    }

    Task<ExitCode> ICommandHandlers.UserRemoveAll(IConsole console, CancellationToken cancellationToken)
    {
        if (!CheckInstalled(console))
        {
            return Task.FromResult(ExitCode.Failure);
        }

        if (!CheckWriteAccess(console))
        {
            return Task.FromResult(ExitCode.AccessDenied);
        }

        UsbipdRegistry.Instance.RemoveAllUsers();
        return Task.FromResult(ExitCode.Success);
    }

    /// <summary>
    /// Mappings are kept on unbind (so unbind/rebind does not lose users), but the user should know.
    /// </summary>
    static void ReportUsersOfUnboundDevice(Guid deviceGuid, IConsole console)
    {
        foreach (var userName in UsbipdRegistry.Instance.GetUserDevices().Where(u => u.Value == deviceGuid).Select(u => u.Key))
        {
            console.ReportWarning($"User '{userName}' is still mapped to this device; attach fails until it is shared again, or run 'usbipd user remove --name {userName}'.");
        }
    }
}
