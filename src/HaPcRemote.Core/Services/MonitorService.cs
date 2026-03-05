using System.Xml.Linq;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

public class MonitorService(IOptionsMonitor<PcRemoteOptions> options, ICliRunner cliRunner, ILogger<MonitorService> logger) : IMonitorService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    private List<MonitorInfo>? _cachedMonitors;
    private DateTime _cacheTime;

    // ── Profile methods ──────────────────────────────────────────────

    public Task<List<MonitorProfile>> GetProfilesAsync()
    {
        var profilesPath = options.CurrentValue.ProfilesPath;

        if (!Directory.Exists(profilesPath))
        {
            logger.LogWarning("Monitor profiles directory not found: {Path}", profilesPath);
            return Task.FromResult(new List<MonitorProfile>());
        }

        logger.LogDebug("Loading monitor profiles from: {Path}", profilesPath);

        var profiles = Directory.GetFiles(profilesPath, "*.cfg")
            .Select(f => new MonitorProfile
            {
                Name = Path.GetFileNameWithoutExtension(f)
            })
            .OrderBy(p => p.Name)
            .ToList();

        return Task.FromResult(profiles);
    }

    public async Task ApplyProfileAsync(string profileName)
    {
        if (profileName.Contains('/') || profileName.Contains('\\') || profileName.Contains(".."))
            throw new ArgumentException($"Invalid profile name: '{profileName}'");

        var config = options.CurrentValue;
        var profilePath = Path.Combine(config.ProfilesPath, $"{profileName}.cfg");

        if (!File.Exists(profilePath))
            throw new KeyNotFoundException($"Monitor profile '{profileName}' not found.");

        logger.LogInformation("Applying monitor profile '{Profile}' from {Path}", profileName, profilePath);

        // Enable all inactive monitors before loading config.
        // MultiMonitorTool /LoadConfig can fail to re-enable disabled monitors,
        // so we bring them all online first, then let the profile sort out
        // which ones stay active, primary, etc.
        await EnableAllInactiveMonitorsAsync();

        await cliRunner.RunAsync(GetExePath(), ["/LoadConfig", profilePath]);

        InvalidateCache();
        logger.LogInformation("Monitor profile '{Profile}' applied successfully", profileName);
    }

    internal async Task EnableAllInactiveMonitorsAsync()
    {
        var monitors = await GetMonitorsAsync();
        var inactive = monitors.Where(m => !m.IsActive).ToList();

        if (inactive.Count == 0)
        {
            logger.LogDebug("All monitors already active, no pre-enable needed");
            return;
        }

        foreach (var m in inactive)
        {
            logger.LogInformation("Pre-enabling inactive monitor {Name} ({Id}) before profile load",
                m.Name, m.MonitorId);
            await cliRunner.RunAsync(GetExePath(), ["/enable", m.Name]);
            await Task.Delay(500);
        }

        InvalidateCache();
    }

    // ── Monitor control methods ──────────────────────────────────────

    public async Task<List<MonitorInfo>> GetMonitorsAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedMonitors is not null && DateTime.UtcNow - _cacheTime < CacheDuration)
                return _cachedMonitors;

            var dir = ConfigPaths.GetWritableConfigDir();
            Directory.CreateDirectory(dir);
            var tempFile = Path.Combine(dir, $"mmt_{Guid.NewGuid():N}.xml");
            try
            {
                await cliRunner.RunAsync(GetExePath(), ["/sxml", tempFile]);
                var output = await File.ReadAllTextAsync(tempFile);
                _cachedMonitors = ParseXmlOutput(output);
                _cacheTime = DateTime.UtcNow;
                return _cachedMonitors;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    public void InvalidateCache()
    {
        _cachedMonitors = null;
    }

    public async Task EnableMonitorAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);
        await cliRunner.RunAsync(GetExePath(), ["/enable", monitor.Name]);
        InvalidateCache();
    }

    public async Task DisableMonitorAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);
        await cliRunner.RunAsync(GetExePath(), ["/disable", monitor.Name]);
        InvalidateCache();
    }

    public async Task SetPrimaryAsync(string id)
    {
        var monitor = await ResolveMonitorAsync(id);
        await cliRunner.RunAsync(GetExePath(), ["/SetPrimary", monitor.Name]);
        InvalidateCache();
    }

    public async Task SoloMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        var target = FindMonitor(monitors, id);

        // Step 1: enable the target so it is active before becoming primary
        if (!target.IsActive)
        {
            await cliRunner.RunAsync(GetExePath(), ["/enable", target.Name]);
            await Task.Delay(500);
        }

        // Step 2: set the target as primary (must be active first)
        await cliRunner.RunAsync(GetExePath(), ["/SetPrimary", target.Name]);
        await Task.Delay(500);

        // Step 3: disable all other monitors
        foreach (var m in monitors.Where(m => !MatchesId(m, id)))
        {
            if (m.IsActive)
            {
                await cliRunner.RunAsync(GetExePath(), ["/disable", m.Name]);
                await Task.Delay(500);
            }
        }

        InvalidateCache();
    }

    // ── XML parsing ──────────────────────────────────────────────────

    internal static List<MonitorInfo> ParseXmlOutput(string xmlContent)
    {
        var monitors = new List<MonitorInfo>();

        if (string.IsNullOrWhiteSpace(xmlContent))
            return monitors;

        var doc = XDocument.Parse(xmlContent);

        foreach (var item in doc.Descendants("item"))
        {
            var name = item.Element("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (string.Equals(item.Element("disconnected")?.Value?.Trim(), "Yes", StringComparison.OrdinalIgnoreCase))
                continue;

            ParseResolution(item.Element("resolution")?.Value, out var width, out var height);
            int.TryParse(item.Element("frequency")?.Value, out var displayFrequency);

            var isActive = string.Equals(
                item.Element("active")?.Value?.Trim(),
                "Yes",
                StringComparison.OrdinalIgnoreCase);

            var isPrimary = string.Equals(
                item.Element("primary")?.Value?.Trim(),
                "Yes",
                StringComparison.OrdinalIgnoreCase);

            var serialNumber = item.Element("monitor_serial_number")?.Value;

            monitors.Add(new MonitorInfo
            {
                Name = name,
                MonitorId = item.Element("short_monitor_id")?.Value ?? "",
                SerialNumber = string.IsNullOrWhiteSpace(serialNumber) ? null : serialNumber,
                MonitorName = item.Element("monitor_name")?.Value ?? "",
                Width = width,
                Height = height,
                DisplayFrequency = displayFrequency,
                IsActive = isActive,
                IsPrimary = isPrimary
            });
        }

        return monitors;
    }

    internal static void ParseResolution(string? resolution, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(resolution))
            return;

        // Format: "1920 X 1200"
        var parts = resolution.Split('X', StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            int.TryParse(parts[0], out width);
            int.TryParse(parts[1], out height);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private string GetExePath() =>
        Path.Combine(options.CurrentValue.ToolsPath, "MultiMonitorTool.exe");

    private async Task<MonitorInfo> ResolveMonitorAsync(string id)
    {
        var monitors = await GetMonitorsAsync();
        return FindMonitor(monitors, id);
    }

    internal static MonitorInfo FindMonitor(List<MonitorInfo> monitors, string id)
    {
        return monitors.Find(m => MatchesId(m, id))
            ?? throw new KeyNotFoundException($"Monitor '{id}' not found.");
    }

    private static bool MatchesId(MonitorInfo m, string id) =>
        string.Equals(m.Name, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.MonitorId, id, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(m.SerialNumber)
            && string.Equals(m.SerialNumber, id, StringComparison.OrdinalIgnoreCase));

}
