using System.Diagnostics;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

public sealed class AppService(
    IOptionsMonitor<PcRemoteOptions> options,
    IAppLauncher appLauncher,
    IBigPictureTracker bigPictureTracker) : IAppService
{
    internal const string BigPictureKey = "steam-bigpicture";

    public Task<List<AppInfo>> GetAllStatusesAsync()
    {
        var apps = options.CurrentValue.Apps;
        if (apps is null || apps.Count == 0)
            return Task.FromResult(new List<AppInfo>());

        var runningProcesses = Process.GetProcesses()
            .Select(p => { var name = p.ProcessName; p.Dispose(); return name; })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = apps.Select(kvp => new AppInfo
        {
            Key = kvp.Key,
            DisplayName = kvp.Value.DisplayName,
            IsRunning = IsBigPicture(kvp.Key)
                ? bigPictureTracker.IsRunning
                : runningProcesses.Contains(kvp.Value.ProcessName)
        }).ToList();

        return Task.FromResult(result);
    }

    public async Task LaunchAsync(string appKey)
    {
        var definition = GetDefinition(appKey);
        await appLauncher.LaunchAsync(definition.ExePath, definition.Arguments, definition.UseShellExecute);

        if (IsBigPicture(appKey))
            bigPictureTracker.MarkStarted();
    }

    public async Task KillAsync(string appKey)
    {
        var definition = GetDefinition(appKey);

        if (IsBigPicture(appKey))
        {
            bigPictureTracker.MarkStopped();
            // Send the close URI instead of killing steam.exe.
            // steam://close/bigpicture exits Big Picture without closing Steam.
            await appLauncher.LaunchAsync("steam://close/bigpicture", null, true);
            return;
        }

        var processes = Process.GetProcessesByName(definition.ProcessName);
        foreach (var process in processes)
        {
            using (process)
                process.Kill(entireProcessTree: true);
        }
    }

    public Task<AppInfo> GetStatusAsync(string appKey)
    {
        var definition = GetDefinition(appKey);

        var info = new AppInfo
        {
            Key = appKey,
            DisplayName = definition.DisplayName,
            IsRunning = IsBigPicture(appKey)
                ? bigPictureTracker.IsRunning
                : IsProcessRunning(definition.ProcessName)
        };

        return Task.FromResult(info);
    }

    private AppDefinitionOptions GetDefinition(string appKey)
    {
        var apps = options.CurrentValue.Apps;
        if (!apps.TryGetValue(appKey, out var definition))
            throw new KeyNotFoundException($"App '{appKey}' is not configured.");

        return definition;
    }

    private static bool IsBigPicture(string appKey) =>
        string.Equals(appKey, BigPictureKey, StringComparison.Ordinal);

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        var running = processes.Length > 0;
        foreach (var p in processes) p.Dispose();
        return running;
    }
}
