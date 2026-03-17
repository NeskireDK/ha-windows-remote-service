$S = "C:/Users/aer/RiderProjects/ha-pc-remote/ha-pc-remote-service"

# Delete model files
Remove-Item -Force "$S/src/HaPcRemote.Core/Models/MonitorProfile.cs" -ErrorAction SilentlyContinue
Remove-Item -Force "$S/src/HaPcRemote.Core/Models/LinuxMonitorProfileData.cs" -ErrorAction SilentlyContinue
Remove-Item -Force "$S/tests/HaPcRemote.IntegrationTests/Models/MonitorProfile.cs" -ErrorAction SilentlyContinue

# IMonitorService
$content = @"
using HaPcRemote.Service.Models;

namespace HaPcRemote.Service.Services;

public interface IMonitorService
{
    Task<List<MonitorInfo>> GetMonitorsAsync();
    Task EnableMonitorAsync(string id);
    Task DisableMonitorAsync(string id);
    Task SetPrimaryAsync(string id);
    Task SoloMonitorAsync(string id);
}
"@
[IO.File]::WriteAllText("$S/src/HaPcRemote.Core/Services/IMonitorService.cs", $content)
Write-Host "IMonitorService done"

# PcRemoteOptions
$f = "$S/src/HaPcRemote.Core/Configuration/PcRemoteOptions.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '    public string ProfilesPath \{ get; set; \} = "\./monitor-profiles";\r?\n', ''
$c = $c -replace '    public string\? MonitorProfile \{ get; set; \}\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "PcRemoteOptions done"

# SystemState
$f = "$S/src/HaPcRemote.Core/Models/SystemState.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '    public List<string>\? MonitorProfiles \{ get; init; \}\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "SystemState done"

# WindowsMonitorService
$f = "$S/src/HaPcRemote.Core/Services/WindowsMonitorService.cs"
$c = [IO.File]::ReadAllText($f)
$pattern = '(?s)    // ── Profiles \(not supported\) ──.*?public Task SaveProfileAsync\(string profileName\) =>\r?\n        throw new NotSupportedException\("Monitor profiles are not supported with the native display API\."\);\r?\n\r?\n    // ── Helpers'
$c = $c -replace $pattern, '    // ── Helpers'
[IO.File]::WriteAllText($f, $c)
Write-Host "WindowsMonitorService done"

# ModeService
$f = "$S/src/HaPcRemote.Core/Services/ModeService.cs"
$c = [IO.File]::ReadAllText($f)
$pattern = '(?s)        if \(config\.SoloMonitor is not null\)\r?\n            await monitorService\.SoloMonitorAsync\(config\.SoloMonitor\);\r?\n        else if \(config\.MonitorProfile is not null\)\r?\n        \{\r?\n            try\r?\n            \{\r?\n                await monitorService\.ApplyProfileAsync\(config\.MonitorProfile\);\r?\n            \}\r?\n            catch \(NotSupportedException ex\)\r?\n            \{\r?\n                logger\.LogWarning\(ex.*?config\.MonitorProfile\);\r?\n            \}\r?\n        \}'
$replacement = '        if (config.SoloMonitor is not null)
            await monitorService.SoloMonitorAsync(config.SoloMonitor);'
$c = $c -replace $pattern, $replacement
[IO.File]::WriteAllText($f, $c)
Write-Host "ModeService done"

# AppJsonContext
$f = "$S/src/HaPcRemote.Core/AppJsonContext.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '\[JsonSerializable\(typeof\(MonitorProfile\)\)\]\r?\n\[JsonSerializable\(typeof\(List<MonitorProfile>\)\)\]\r?\n\[JsonSerializable\(typeof\(ApiResponse<List<MonitorProfile>>\)\)\]\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "AppJsonContext done"

# SystemStateEndpoints
$f = "$S/src/HaPcRemote.Core/Endpoints/SystemStateEndpoints.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '            var profilesTask = GetProfileNamesAsync\(monitorService\);\r?\n', ''
$c = $c -replace 'audioTask, monitorsTask, profilesTask, steamGamesTask', 'audioTask, monitorsTask, steamGamesTask'
$c = $c -replace '(?s)            List<string>\? monitorProfiles = null;\r?\n            try \{ monitorProfiles = await profilesTask; \}\r?\n            catch \(Exception ex\) \{ logger\.LogWarning\(ex, "Failed to get monitor profiles"\); \}\r?\n\r?\n', ''
$c = $c -replace '                MonitorProfiles = monitorProfiles,\r?\n', ''
$c = $c -replace '(?s)\r?\n    private static async Task<List<string>> GetProfileNamesAsync\(IMonitorService monitorService\)\r?\n    \{[^}]+\}\r?\n', "`n"
[IO.File]::WriteAllText($f, $c)
Write-Host "SystemStateEndpoints done"

# MonitorEndpoints - remove profile endpoints
$f = "$S/src/HaPcRemote.Core/Endpoints/MonitorEndpoints.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '(?s)\r?\n        // ── Profile endpoints.*?AppJsonContext\.Default\.ApiResponse\);\r?\n        \}\);', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "MonitorEndpoints done"

# TrayApplicationContext
$f = "$S/src/HaPcRemote.Tray/TrayApplicationContext.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '    private readonly string _profilesPath;\r?\n', ''
$c = $c -replace '        _profilesPath = options\.ProfilesPath;\r?\n', ''
$c = $c -replace '        _logger\.LogInformation\("Profiles path: \{ProfilesPath\}", _profilesPath\);\r?\n', ''
$c = $c -replace '        menu\.Items\.Add\("Open Profiles Folder", null, OnOpenProfilesFolder\);\r?\n', ''
$c = $c -replace '(?s)    private void OnOpenProfilesFolder\(object\? sender, EventArgs e\)\r?\n    \{[^}]+\{[^}]+\}[^}]+\}\r?\n\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "TrayApplicationContext done"

# ModesTab
$f = "$S/src/HaPcRemote.Tray/Forms/ModesTab.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '    private readonly ComboBox _monitorProfileCombo;\r?\n', ''
$c = $c -replace "(?s)        // Monitor profile\r?\n        _monitorProfileCombo = new ComboBox.*?1, row\+\+\);\r?\n\r?\n        // Solo monitor", '        // Solo monitor'
$c = $c -replace "(?s)            _monitorProfileCombo\.Items\.Clear\(\);\r?\n            _monitorProfileCombo\.Items\.Add\(.Don't change.\);\r?\n            var profiles = await _monitorService\.GetProfilesAsync\(\);\r?\n            foreach \(var p in profiles\)\r?\n                _monitorProfileCombo\.Items\.Add\(p\.Name\);\r?\n\r?\n", ''
$c = $c -replace "            _monitorProfileCombo\.SelectedItem = mode\.MonitorProfile \?\? ..\(Don't change\).;\r?\n", ''
$c = $c -replace "(?s)            MonitorProfile = _monitorProfileCombo\.SelectedItem\?\.ToString\(\) is ..\(Don't change\).. \? null : _monitorProfileCombo\.SelectedItem\?\.ToString\(\),\r?\n", ''
$c = $c -replace '        if \(_monitorProfileCombo\.Items\.Count > 0\) _monitorProfileCombo\.SelectedIndex = 0;\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "ModesTab done"

# TrayWebHost
$f = "$S/src/HaPcRemote.Tray/TrayWebHost.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '            if \(!Path\.IsPathRooted\(options\.ProfilesPath\)\)\r?\n                options\.ProfilesPath = Path\.GetFullPath\(options\.ProfilesPath, ConfigPaths\.GetWritableConfigDir\(\)\);\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "TrayWebHost done"

# LinuxMonitorService - complete rewrite (too many changes)
$content = @"
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
"@
[IO.File]::WriteAllText("$S/src/HaPcRemote.Core/Services/LinuxMonitorService.cs", $content)
Write-Host "LinuxMonitorService done"

# Headless Program.cs - remove ProfilesPath reference
$f = "$S/src/HaPcRemote.Headless/Program.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '    options\.ProfilesPath = ResolveRelativePath\(options\.ProfilesPath, baseDir\);\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "Headless Program done"

# ConfigurationWriterTests - remove MonitorProfile from mode config
$f = "$S/tests/HaPcRemote.Service.Tests/Services/ConfigurationWriterTests.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace '            MonitorProfile = "tv-only",\r?\n', ''
$c = $c -replace '        result\.MonitorProfile\.ShouldBe\("tv-only"\);\r?\n', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "ConfigurationWriterTests done"

# SteamServiceTests - remove MonitorProfile from test data
$f = "$S/tests/HaPcRemote.Service.Tests/Services/SteamServiceTests.cs"
$c = [IO.File]::ReadAllText($f)
$c = $c -replace ', MonitorProfile = "tv"', ''
$c = $c -replace ', MonitorProfile = "dual"', ''
[IO.File]::WriteAllText($f, $c)
Write-Host "SteamServiceTests done"

# WindowsMonitorServiceTests - remove profile tests
$f = "$S/tests/HaPcRemote.Service.Tests/Services/WindowsMonitorServiceTests.cs"
$c = [IO.File]::ReadAllText($f)
$pattern = '(?s)    // ── Profiles \(not supported\) ──.*?await Should\.ThrowAsync<NotSupportedException>\(\(\) => service\.SaveProfileAsync\("any"\)\);\r?\n    \}\r?\n\r?\n    // ── FormatEdidId'
$c = $c -replace $pattern, '    // ── FormatEdidId'
[IO.File]::WriteAllText($f, $c)
Write-Host "WindowsMonitorServiceTests done"

Write-Host "`nAll files processed!"
