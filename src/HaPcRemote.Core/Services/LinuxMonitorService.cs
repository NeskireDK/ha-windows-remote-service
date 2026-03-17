using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("linux")]
public sealed partial class LinuxMonitorService(
    ICliRunner cliRunner,
    IOptionsMonitor<PcRemoteOptions> options,
    ILogger<LinuxMonitorService> logger) : IMonitorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

    // ── Profile methods ──────────────────────────────────────────────

    public Task<List<MonitorProfile>> GetProfilesAsync()
    {
        var profilesPath = options.CurrentValue.ProfilesPath;

        if (!Directory.Exists(profilesPath))
        {
            logger.LogDebug("Monitor profiles directory not found: {Path}", profilesPath);
            return Task.FromResult(new List<MonitorProfile>());
        }

        var profiles = Directory.GetFiles(profilesPath, "*.json")
            .Select(f => new MonitorProfile
            {
                Name = Path.GetFileNameWithoutExtension(f)
            })
            .OrderBy(p => p.Name)
            .ToList();

        return Task.FromResult(profiles);
    }

    public async Task SaveProfileAsync(string profileName)
    {
        ValidateProfileName(profileName);

        var output = await cliRunner.RunAsync("xrandr", ["--query"]);
        var profileData = ParseXrandrQuery(output);

        var profilesPath = options.CurrentValue.ProfilesPath;
        Directory.CreateDirectory(profilesPath);

        var filePath = Path.Combine(profilesPath, $"{profileName}.json");
        var json = JsonSerializer.Serialize(profileData, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        logger.LogInformation("Saved monitor profile '{Profile}' with {Count} outputs to {Path}",
            profileName, profileData.Outputs.Count, filePath);
    }

    public async Task ApplyProfileAsync(string profileName)
    {
        ValidateProfileName(profileName);

        var profilesPath = options.CurrentValue.ProfilesPath;
        var filePath = Path.Combine(profilesPath, $"{profileName}.json");

        if (!File.Exists(filePath))
            throw new KeyNotFoundException($"Monitor profile '{profileName}' not found.");

        var json = await File.ReadAllTextAsync(filePath);
        var profileData = JsonSerializer.Deserialize<LinuxMonitorProfileData>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize profile '{profileName}'.");

        logger.LogInformation("Applying monitor profile '{Profile}' from {Path}", profileName, filePath);

        var args = BuildXrandrApplyArgs(profileData);
        await cliRunner.RunAsync("xrandr", args);

        InvalidateCache();
        logger.LogInformation("Monitor profile '{Profile}' applied successfully", profileName);
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

    // ── xrandr --query parsing ───────────────────────────────────────

    // Output line: "DP-0 connected primary 2560x1440+0+0 (...) 597mm x 336mm"
    // or:          "HDMI-0 connected 2560x1440+2560+0 (...) 597mm x 336mm"
    // or:          "DP-1 disconnected (normal ...)"
    [GeneratedRegex(@"^(\S+)\s+connected\s+(primary\s+)?(\d+)x(\d+)\+(\d+)\+(\d+)\s")]
    private static partial Regex XrandrOutputRegex();

    // Mode line: "   2560x1440     59.95*+  143.97  "
    // Current mode has '*', preferred has '+'
    [GeneratedRegex(@"^\s+(\d+)x(\d+)\s+(.+)$")]
    private static partial Regex XrandrModeRegex();

    [GeneratedRegex(@"(\d+\.?\d*)\*")]
    private static partial Regex ActiveRefreshRegex();

    internal static LinuxMonitorProfileData ParseXrandrQuery(string output)
    {
        var data = new LinuxMonitorProfileData();
        LinuxMonitorOutputConfig? current = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var outputMatch = XrandrOutputRegex().Match(line);
            if (outputMatch.Success)
            {
                current = new LinuxMonitorOutputConfig
                {
                    Name = outputMatch.Groups[1].Value,
                    IsPrimary = outputMatch.Groups[2].Success && outputMatch.Groups[2].Value.Trim().Length > 0,
                    Width = int.Parse(outputMatch.Groups[3].Value),
                    Height = int.Parse(outputMatch.Groups[4].Value),
                    PositionX = int.Parse(outputMatch.Groups[5].Value),
                    PositionY = int.Parse(outputMatch.Groups[6].Value),
                    IsEnabled = true
                };
                data.Outputs.Add(current);
                continue;
            }

            // If we're inside a connected output block, look for the active mode line
            if (current is not null)
            {
                var modeMatch = XrandrModeRegex().Match(line);
                if (modeMatch.Success)
                {
                    var rates = modeMatch.Groups[3].Value;
                    var activeRate = ActiveRefreshRegex().Match(rates);
                    if (activeRate.Success && current.RefreshRate == 0)
                    {
                        // Parse refresh rate, round to nearest int
                        if (double.TryParse(activeRate.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var hz))
                        {
                            current.RefreshRate = (int)Math.Round(hz);
                        }
                    }
                }
                else if (!line.StartsWith(' ') && !line.StartsWith('\t'))
                {
                    // New non-indented line means we left the mode block
                    current = null;
                }
            }
        }

        return data;
    }

    internal static List<string> BuildXrandrApplyArgs(LinuxMonitorProfileData profile)
    {
        var args = new List<string>();

        foreach (var output in profile.Outputs)
        {
            args.Add("--output");
            args.Add(output.Name);

            if (!output.IsEnabled)
            {
                args.Add("--off");
                continue;
            }

            args.Add("--mode");
            args.Add($"{output.Width}x{output.Height}");
            args.Add("--pos");
            args.Add($"{output.PositionX}x{output.PositionY}");

            if (output.RefreshRate > 0)
            {
                args.Add("--rate");
                args.Add(output.RefreshRate.ToString());
            }

            if (output.IsPrimary)
                args.Add("--primary");
        }

        return args;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static void ValidateProfileName(string profileName)
    {
        if (profileName.Contains('/') || profileName.Contains('\\') || profileName.Contains(".."))
            throw new ArgumentException($"Invalid profile name: '{profileName}'");
    }

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
