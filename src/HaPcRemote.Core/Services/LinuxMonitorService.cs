using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("linux")]
public sealed partial class LinuxMonitorService(
    ICliRunner cliRunner,
    ILogger<LinuxMonitorService> logger) : IMonitorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private List<MonitorInfo>? _cachedMonitors;
    private DateTime _cacheTime;

    public async Task<List<MonitorInfo>> GetMonitorsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedMonitors is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
                return _cachedMonitors;

            var output = await cliRunner.RunAsync("xrandr", ["--listmonitors"]);
            _cachedMonitors = ParseListMonitors(output);
            _cacheTime = DateTime.UtcNow;
            return _cachedMonitors;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // ── Monitor control methods ──────────────────────────────────────

    public async Task EnableMonitorAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);

        if (monitor.IsActive)
        {
            logger.LogInformation("Monitor '{Id}' is already enabled, skipping", id);
            return;
        }

        await cliRunner.RunAsync("xrandr", ["--output", monitor.Name, "--auto"]);
        InvalidateCache();
    }

    public async Task DisableMonitorAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);

        if (!monitor.IsActive)
        {
            logger.LogInformation("Monitor '{Id}' is already disabled, skipping", id);
            return;
        }

        await cliRunner.RunAsync("xrandr", ["--output", monitor.Name, "--off"]);
        InvalidateCache();
    }

    public async Task SetPrimaryAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);
        await cliRunner.RunAsync("xrandr", ["--output", monitor.Name, "--primary"]);
        InvalidateCache();
    }

    public async Task SoloMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = MonitorMatchHelper.FindMonitor(monitors, id);

        // Enable the target
        await cliRunner.RunAsync("xrandr", ["--output", target.Name, "--auto", "--primary"]);
        await Task.Delay(500);

        // Disable all others
        foreach (var m in monitors.Where(m => !MonitorMatchHelper.MatchesId(m, id)))
        {
            if (m.IsActive)
            {
                await cliRunner.RunAsync("xrandr", ["--output", m.Name, "--off"]);
                await Task.Delay(500);
            }
        }

        InvalidateCache();
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void InvalidateCache() => _cachedMonitors = null;

    private async Task<MonitorInfo> ResolveMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        return MonitorMatchHelper.FindMonitor(monitors, id);
    }

    // xrandr --listmonitors output example:
    // Monitors: 2
    //  0: +*eDP-1 1920/310x1080/170+0+0  eDP-1
    //  1: +HDMI-1 2560/597x1440/336+1920+0  HDMI-1
    // Format: <index>: [+][*]<name> <WmmxHmm+X+Y>  <output>
    [GeneratedRegex(@"^\s*(\d+):\s+(\+?\*?)(\S+)\s+(\d+)/(\d+)x(\d+)/(\d+)\+(\d+)\+(\d+)\s*")]
    private static partial Regex MonitorLineRegex();

    internal static List<MonitorInfo> ParseListMonitors(string output)
    {
        var monitors = new List<MonitorInfo>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');
            var match = MonitorLineRegex().Match(trimmed);
            if (!match.Success)
                continue;

            var index = match.Groups[1].Value;
            var flags = match.Groups[2].Value;   // e.g. "+*", "+", ""
            var name = match.Groups[3].Value;
            int.TryParse(match.Groups[4].Value, out var widthPx);
            int.TryParse(match.Groups[6].Value, out var heightPx);

            var isPrimary = flags.Contains('*');

            monitors.Add(new MonitorInfo
            {
                Name = name,
                MonitorId = index,
                SerialNumber = null,
                MonitorName = name,
                Width = widthPx,
                Height = heightPx,
                DisplayFrequency = 0, // not available from --listmonitors
                IsActive = true,      // listed monitors are active
                IsPrimary = isPrimary
            });
        }

        return monitors;
    }
}
