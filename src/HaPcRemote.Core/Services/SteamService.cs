using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ValveKeyValue;

namespace HaPcRemote.Service.Services;

public sealed class SteamService(
    ISteamPlatform platform,
    IModeService modeService,
    IOptionsMonitor<PcRemoteOptions> options,
    IHttpClientFactory httpClientFactory,
    IEmulatorTracker emulatorTracker,
    ILogger<SteamService> logger,
    Func<int, Task>? delay = null) : ISteamService
{
    private readonly Func<int, Task> _delay = delay ?? (ms => Task.Delay(ms));
    private List<SteamGame>? _cachedGames;
    private DateTime _cacheExpiry;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private IReadOnlyList<string>? _libraryFolders;

    public async Task<List<SteamGame>> GetGamesAsync()
    {
        if (_cachedGames != null && DateTime.UtcNow < _cacheExpiry)
            return _cachedGames;

        var steamPath = platform.GetSteamPath();
        if (steamPath == null)
        {
            if (_cachedGames != null)
                return _cachedGames;

            throw new InvalidOperationException("Steam is not installed.");
        }

        var games = await Task.Run(() => LoadInstalledGames(steamPath));
        _cachedGames = games;
        _cacheExpiry = DateTime.UtcNow + CacheDuration;

        var shortcuts = games.Where(g => g.IsShortcut).ToList();
        logger.LogDebug("Non-Steam shortcuts loaded: {Count} found", shortcuts.Count);
        foreach (var s in shortcuts)
            logger.LogDebug("  Shortcut [{AppId}] {Name}: ExePath={ExePath}", s.AppId, s.Name, s.ExePath ?? "(null)");

        return games;
    }

    public async Task<SteamRunningGame?> GetRunningGameAsync()
    {
        var appId = platform.GetRunningAppId();
        if (appId == 0)
        {
            // Process-based fallback for non-Steam shortcuts
            return await TryFindRunningShortcutAsync();
        }

        // Warm the cache if not yet populated
        await EnsureCacheWarmAsync();

        var name = _cachedGames?.Find(g => g.AppId == appId)?.Name;

        // Game is running but not in the top-20 list — look it up from its manifest or shortcuts
        if (name == null)
        {
            var steamPath = platform.GetSteamPath();
            if (steamPath != null)
            {
                name = IsShortcutAppId(appId)
                    ? FindShortcutName(steamPath, appId)
                    : FindGameNameFromManifest(steamPath, appId);
            }
        }

        name ??= $"Unknown ({appId})";
        return new SteamRunningGame { AppId = appId, Name = name };
    }

    public async Task<RunningGameDiagnostics> GetRunningGameDiagnosticsAsync()
    {
        var steamAppId = platform.GetRunningAppId();

        await EnsureCacheWarmAsync();

        var shortcuts = _cachedGames?.Where(g => g.IsShortcut && g.ExePath != null).ToList()
                        ?? [];
        var runningProcesses = platform.GetRunningProcesses().ToList();
        var processesByPath = runningProcesses
            .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var traces = new List<ShortcutDetectionTrace>();
        SteamRunningGame? result = null;

        foreach (var shortcut in shortcuts)
        {
            var exeName = Path.GetFileName(shortcut.ExePath!);
            var filenameMatches = runningProcesses
                .Where(p => Path.GetFileName(p.Path).Equals(exeName, StringComparison.OrdinalIgnoreCase))
                .Select(p => new ProcessMatch { Pid = p.Pid, Path = p.Path, CommandLine = p.CommandLine })
                .ToList();

            var exactPathMatch = processesByPath.TryGetValue(shortcut.ExePath!, out var matchingProcesses);
            var matched = false;
            int? matchedPid = null;
            string? matchReason = null;

            if (exactPathMatch && matchingProcesses!.Count > 0)
            {
                if (matchingProcesses.Count == 1 || string.IsNullOrEmpty(shortcut.LaunchOptions))
                {
                    matched = true;
                    matchedPid = matchingProcesses[0].Pid;
                    matchReason = "exe-path";
                }
                else
                {
                    var cmdMatch = matchingProcesses.FirstOrDefault(p =>
                        p.CommandLine != null &&
                        p.CommandLine.Contains(shortcut.LaunchOptions, StringComparison.OrdinalIgnoreCase));
                    if (cmdMatch != null)
                    {
                        matched = true;
                        matchedPid = cmdMatch.Pid;
                        matchReason = "command-line";
                    }
                }
            }

            traces.Add(new ShortcutDetectionTrace
            {
                AppId = shortcut.AppId,
                Name = shortcut.Name,
                ExePath = shortcut.ExePath,
                LaunchOptions = shortcut.LaunchOptions,
                FilenameMatches = filenameMatches,
                ExactPathMatch = exactPathMatch,
                Matched = matched,
                MatchedPid = matchedPid,
                MatchReason = matchReason
            });

            if (matched && result == null)
                result = new SteamRunningGame { AppId = shortcut.AppId, Name = shortcut.Name, ProcessId = matchedPid };
        }

        // If Steam reports a non-zero, non-shortcut appId, use that instead
        if (steamAppId != 0 && !IsShortcutAppId(steamAppId))
        {
            if (result != null)
                logger.LogDebug("Overriding shortcut diagnostic result with standard game {AppId}", steamAppId);
            var name = _cachedGames?.Find(g => g.AppId == steamAppId)?.Name ?? $"Unknown ({steamAppId})";
            result = new SteamRunningGame { AppId = steamAppId, Name = name };
        }

        return new RunningGameDiagnostics
        {
            SteamReportedAppId = steamAppId,
            SteamRunning = platform.IsSteamRunning(),
            ShortcutsChecked = shortcuts.Count,
            RunningProcessCount = runningProcesses.Count,
            Traces = traces,
            Result = result
        };
    }

    private async Task<SteamRunningGame?> TryFindRunningShortcutAsync()
    {
        // Warm the cache if needed
        await EnsureCacheWarmAsync();

        var shortcuts = _cachedGames?.Where(g => g.IsShortcut && g.ExePath != null).ToList();
        if (shortcuts == null || shortcuts.Count == 0)
        {
            logger.LogDebug("Non-Steam detection: no shortcuts with ExePath in cache");
            return null;
        }

        logger.LogDebug("Non-Steam detection: checking {Count} shortcut(s)", shortcuts.Count);
        foreach (var s in shortcuts)
            logger.LogDebug("  Shortcut [{AppId}] {Name}: ExePath={ExePath} LaunchOptions={LaunchOptions}",
                s.AppId, s.Name, s.ExePath, s.LaunchOptions ?? "(null)");

        var runningProcesses = platform.GetRunningProcesses().ToList();
        logger.LogDebug("Non-Steam detection: {Count} running processes", runningProcesses.Count);

        // Check for filename-level matches to surface path mismatches
        foreach (var s in shortcuts)
        {
            var exeName = Path.GetFileName(s.ExePath!);
            var candidates = runningProcesses
                .Where(p => Path.GetFileName(p.Path).Equals(exeName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var c in candidates)
                logger.LogDebug("  Filename match for '{Name}': running={Running} | shortcut={Shortcut}",
                    s.Name, c.Path, s.ExePath);
        }

        // Build lookup of processes by exe path (case-insensitive)
        var processesByPath = runningProcesses
            .GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var shortcut in shortcuts)
        {
            if (!processesByPath.TryGetValue(shortcut.ExePath!, out var matchingProcesses))
            {
                logger.LogDebug("Non-Steam detection: no process path match for [{AppId}] {Name} (ExePath={ExePath})",
                    shortcut.AppId, shortcut.Name, shortcut.ExePath);
                continue;
            }

            // Single match or no launch options needed — use exe path match directly
            if (matchingProcesses.Count == 1 || string.IsNullOrEmpty(shortcut.LaunchOptions))
            {
                var proc = matchingProcesses[0];
                logger.LogDebug("Non-Steam detection: matched [{AppId}] {Name} via exe path (pid={Pid})",
                    shortcut.AppId, shortcut.Name, proc.Pid);
                return new SteamRunningGame { AppId = shortcut.AppId, Name = shortcut.Name, ProcessId = proc.Pid };
            }

            // Multiple shortcuts share this exe (emulator case) — disambiguate via CommandLine
            var cmdMatch = matchingProcesses.FirstOrDefault(p =>
                p.CommandLine != null &&
                p.CommandLine.Contains(shortcut.LaunchOptions, StringComparison.OrdinalIgnoreCase));

            if (cmdMatch != null)
            {
                logger.LogDebug("Non-Steam detection: matched [{AppId}] {Name} via CommandLine containing LaunchOptions (pid={Pid})",
                    shortcut.AppId, shortcut.Name, cmdMatch.Pid);
                return new SteamRunningGame { AppId = shortcut.AppId, Name = shortcut.Name, ProcessId = cmdMatch.Pid };
            }
        }

        // Fallback: if an emulator exe is running but we couldn't disambiguate via CommandLine,
        // use the emulator tracker to return the last-launched game for that exe
        foreach (var shortcut in shortcuts)
        {
            if (shortcut.ExePath == null || !processesByPath.ContainsKey(shortcut.ExePath))
                continue;

            var tracked = emulatorTracker.GetLastLaunched(shortcut.ExePath);
            if (tracked == null)
                continue;

            var trackedShortcut = shortcuts.Find(s => s.AppId == tracked.Value.AppId);
            if (trackedShortcut != null)
            {
                var proc = processesByPath[shortcut.ExePath][0];
                logger.LogDebug(
                    "Non-Steam detection: matched [{AppId}] {Name} via emulator tracker (pid={Pid})",
                    trackedShortcut.AppId, trackedShortcut.Name, proc.Pid);
                return new SteamRunningGame
                {
                    AppId = trackedShortcut.AppId,
                    Name = trackedShortcut.Name,
                    ProcessId = proc.Pid
                };
            }
        }

        logger.LogDebug("Non-Steam detection: no exact path match found");
        return null;
    }

    public async Task<SteamRunningGame?> LaunchGameAsync(int appId)
    {
        var runningAppId = platform.GetRunningAppId();
        if (runningAppId == appId)
            return await GetRunningGameAsync();

        if (runningAppId != 0)
            await StopGameAsync();

        // Resolve and apply PC mode before launching the game
        var resolvedMode = ResolvePcMode(appId);
        if (!string.IsNullOrEmpty(resolvedMode))
        {
            try
            {
                await modeService.ApplyModeAsync(resolvedMode);
                logger.LogInformation("Applied PC mode '{Mode}' for game {AppId}", resolvedMode, appId);

                // If the mode launched an app (e.g. Big Picture), give it time to initialize
                // before firing the game URI — Steam ignores rungameid while BP is loading.
                if (options.CurrentValue.Modes.TryGetValue(resolvedMode, out var modeConfig)
                    && !string.IsNullOrEmpty(modeConfig.LaunchApp))
                {
                    var postLaunchDelay = modeConfig.PostLaunchDelayMs ?? 3000;
                    logger.LogDebug("Mode '{Mode}' launched '{App}', waiting {Delay}ms for it to initialize", resolvedMode, modeConfig.LaunchApp, postLaunchDelay);
                    await _delay(postLaunchDelay);
                }
            }
            catch (KeyNotFoundException)
            {
                logger.LogWarning("PC mode '{Mode}' not found, skipping mode switch for game {AppId}", resolvedMode, appId);
            }
        }

        // Non-Steam shortcuts use a shifted appid for the steam:// URI
        var launchId = IsShortcutAppId(appId)
            ? ((long)(uint)appId << 32) | 0x02000000
            : appId;

        // Track emulator launch for non-Steam shortcuts before launching
        if (IsShortcutAppId(appId))
        {
            var game = _cachedGames?.Find(g => g.AppId == appId);
            if (game?.ExePath != null)
            {
                emulatorTracker.TrackLaunch(game.ExePath, appId, game.Name);
                logger.LogDebug("Tracked emulator launch: [{AppId}] {Name} via {ExePath}", appId, game.Name, game.ExePath);
            }
        }

        platform.LaunchSteamUrl($"steam://rungameid/{launchId}");

        // Brief poll — Steam registers running state within seconds
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var running = platform.GetRunningAppId();
            if (running == appId)
                return await GetRunningGameAsync();
        }

        return null; // Steam didn't accept the launch
    }

    public async Task StopGameAsync()
    {
        var appId = platform.GetRunningAppId();
        logger.LogInformation("StopGame: Steam reports running appId={AppId}, isShortcut={IsShortcut}",
            appId, IsShortcutAppId(appId));

        // No Steam-tracked game running — try process-based shortcut detection
        if (appId == 0 || IsShortcutAppId(appId))
        {
            logger.LogInformation("StopGame: attempting process-based shortcut detection (appId={AppId})", appId);
            var runningShortcut = await TryFindRunningShortcutAsync();
            if (runningShortcut?.ProcessId != null)
            {
                logger.LogInformation("StopGame: killing non-Steam shortcut [{AppId}] {Name} (pid={Pid})",
                    runningShortcut.AppId, runningShortcut.Name, runningShortcut.ProcessId);
                platform.KillProcess(runningShortcut.ProcessId.Value);
                return;
            }

            logger.LogWarning("StopGame: shortcut detection returned no match (ProcessId=null, Data={HasData})",
                runningShortcut != null);

            if (appId == 0)
            {
                logger.LogWarning("StopGame: nothing to stop — appId=0 and no shortcut match");
                return;
            }
        }

        // Regular Steam game — kill processes in install directory
        var steamPath = platform.GetSteamPath();
        if (steamPath == null)
            return;

        var installDir = GetGameInstallDir(steamPath, appId);
        if (installDir != null)
            platform.KillProcessesInDirectory(installDir);
    }

    public async Task<string?> GetArtworkPathAsync(int appId)
    {
        var steamPath = platform.GetSteamPath();
        if (steamPath == null)
        {
            logger.LogWarning("Artwork: Steam path not found for appId={AppId}", appId);
            return null;
        }

        var gameName = _cachedGames?.FirstOrDefault(g => g.AppId == appId)?.Name;
        var result = FindArtworkPath(steamPath, platform.GetSteamUserId(), appId, logger);

        if (result == null && !IsShortcutAppId(appId))
        {
            result = await TryDownloadFromCdnAsync(steamPath, appId, gameName);
        }

        if (result == null)
            logger.LogError("Artwork: no cover found for appId={AppId} game={GameName}", appId, gameName ?? "unknown");
        else
            logger.LogDebug("Artwork: serving {Path} ({Size} KB) for appId={AppId} game={GameName}",
                result, new FileInfo(result).Length / 1024, appId, gameName ?? "unknown");
        return result;
    }

    private async Task<string?> TryDownloadFromCdnAsync(string steamPath, int appId, string? gameName)
    {
        var cdnUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg";
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache");
        var destPath = Path.Combine(cacheDir, $"{appId}_library_600x900.jpg");

        logger.LogWarning(
            "Artwork: [CDN FALLBACK] local cache empty for appId={AppId} game={GameName} — downloading {Url}",
            appId, gameName ?? "unknown", cdnUrl);

        try
        {
            Directory.CreateDirectory(cacheDir);
            var http = httpClientFactory.CreateClient();
            var bytes = await http.GetByteArrayAsync(cdnUrl);
            await File.WriteAllBytesAsync(destPath, bytes);
            logger.LogWarning(
                "Artwork: [CDN FALLBACK] saved {Size} KB to {Path} for appId={AppId}",
                bytes.Length / 1024, destPath, appId);
            return destPath;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Artwork: [CDN FALLBACK] failed for appId={AppId} game={GameName} url={Url}",
                appId, gameName ?? "unknown", cdnUrl);
            return null;
        }
    }

    public SteamBindings GetBindings()
    {
        var steam = options.CurrentValue.Steam;
        return new SteamBindings
        {
            DefaultPcMode = steam.DefaultPcMode,
            GamePcModeBindings = new Dictionary<string, string>(steam.GamePcModeBindings)
        };
    }

    public bool IsSteamRunning() => platform.IsSteamRunning();

    private async Task EnsureCacheWarmAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (_cachedGames is { Count: > 0 }) return;
            try { await GetGamesAsync(); }
            catch (InvalidOperationException) { /* Steam not installed */ }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Seeds the game cache directly. Intended for test use only.
    /// </summary>
    internal void SetCachedGamesForTest(List<SteamGame> games)
    {
        _cachedGames = games;
        _cacheExpiry = DateTime.UtcNow + CacheDuration;
    }

    /// <summary>
    /// Resolve which PC mode to apply for a given game.
    /// Per-game binding takes priority, then default, then none.
    /// Returns null/empty if no mode switch should happen.
    /// </summary>
    internal string? ResolvePcMode(int appId)
    {
        var steam = options.CurrentValue.Steam;
        var appIdStr = appId.ToString();

        if (steam.GamePcModeBindings.TryGetValue(appIdStr, out var perGame)
            && !string.IsNullOrEmpty(perGame)
            && !perGame.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return perGame;
        }

        // If there is a per-game binding set to "none", skip the default
        if (steam.GamePcModeBindings.TryGetValue(appIdStr, out var binding)
            && binding != null
            && binding.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(steam.DefaultPcMode)
            && !steam.DefaultPcMode.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return steam.DefaultPcMode;
        }

        return null;
    }

    /// <summary>
    /// Shortcut appids are generated by Steam from exe+appname and are always negative when
    /// stored as a signed 32-bit int (high bit set).
    /// </summary>
    internal static bool IsShortcutAppId(int appId) => appId < 0;

    // ── Static VDF parsers (testable without mocking) ────────────────

    internal static List<string> ParseLibraryFolders(string vdfContent)
    {
        var paths = new List<string>();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(vdfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);

        foreach (var folder in data)
        {
            var path = folder["path"]?.ToString();
            if (!string.IsNullOrEmpty(path))
                paths.Add(path.Replace(@"\\", @"\"));
        }

        return paths;
    }

    internal static SteamGame? ParseAppManifest(string acfContent)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(acfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);

        var appIdStr = data["appid"]?.ToString();
        var name = data["name"]?.ToString();
        var lastPlayedStr = data["LastPlayed"]?.ToString();
        var lastUpdatedStr = data["LastUpdated"]?.ToString();

        if (string.IsNullOrEmpty(appIdStr) || string.IsNullOrEmpty(name))
            return null;

        if (!int.TryParse(appIdStr, out var appId))
            return null;

        // Prefer LastPlayed (actual play history); fall back to LastUpdated (install/update time)
        if (!long.TryParse(lastPlayedStr, out var lastPlayed))
            long.TryParse(lastUpdatedStr, out lastPlayed);

        return new SteamGame { AppId = appId, Name = name, LastPlayed = lastPlayed };
    }

    internal static string? ParseInstallDir(string acfContent)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(acfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);
        return data["installdir"]?.ToString();
    }

    internal static List<SteamGame> ParseShortcuts(Stream stream, ILogger? logger = null)
    {
        var shortcuts = new List<SteamGame>();
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary);
        KVObject root;
        try
        {
            root = kv.Deserialize(stream);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Shortcut parsing: failed to deserialize shortcuts.vdf");
            return shortcuts;
        }

        foreach (var entry in root)
        {
            var appName = entry["AppName"]?.ToString()
                          ?? entry["appname"]?.ToString();
            if (string.IsNullOrEmpty(appName))
                continue;

            var rawExe = entry["Exe"]?.ToString() ?? entry["exe"]?.ToString();
            logger?.LogDebug("Shortcut parsing [{Name}]: raw Exe field = {RawExe}", appName, rawExe ?? "(null)");

            var (exe, exeArgs) = ParseExeField(rawExe);
            logger?.LogDebug("Shortcut parsing [{Name}]: parsed ExePath={ExePath}, ExeArgs={ExeArgs}",
                appName, exe ?? "(null)", exeArgs ?? "(null)");

            // Steam stores shortcut appid as a signed 32-bit int (high bit set).
            // Try appid first, then fallback to calculating from exe+appname.
            int appId;
            var appIdStr = entry["appid"]?.ToString();
            if (!string.IsNullOrEmpty(appIdStr) && int.TryParse(appIdStr, out var parsed) && parsed != 0)
            {
                appId = parsed;
            }
            else
            {
                // Fallback: generate the shortcut appid from exe + appname
                appId = GenerateShortcutAppId(exe ?? "", appName);
            }

            var launchOptions = entry["LaunchOptions"]?.ToString()
                                ?? entry["launchoptions"]?.ToString();

            // If the Exe field contained arguments and LaunchOptions is empty, use the extracted args
            if (string.IsNullOrEmpty(launchOptions) && !string.IsNullOrEmpty(exeArgs))
            {
                launchOptions = exeArgs;
                logger?.LogDebug("Shortcut parsing [{Name}]: promoted exe args to LaunchOptions={LaunchOptions}",
                    appName, launchOptions);
            }

            var lastPlayedStr = entry["LastPlayTime"]?.ToString();
            long.TryParse(lastPlayedStr, out var lastPlayed);

            logger?.LogDebug("Shortcut parsing [{Name}]: AppId={AppId}, ExePath={ExePath}, LaunchOptions={LaunchOptions}",
                appName, appId, exe ?? "(null)", launchOptions ?? "(null)");

            shortcuts.Add(new SteamGame
            {
                AppId = appId,
                Name = appName,
                LastPlayed = lastPlayed,
                IsShortcut = true,
                ExePath = string.IsNullOrEmpty(exe) ? null : exe,
                LaunchOptions = string.IsNullOrEmpty(launchOptions) ? null : launchOptions
            });
        }

        return shortcuts;
    }

    /// <summary>
    /// Parses the Exe field from shortcuts.vdf. Steam stores this as a quoted path
    /// optionally followed by arguments, e.g.: "D:\emulator\emu.exe" -g "D:\games\rom.bin"
    /// Returns (exePath, args) where args may be null.
    /// </summary>
    internal static (string? ExePath, string? Args) ParseExeField(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, null);

        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, null);

        // Case 1: Quoted path — extract path between first pair of quotes, rest is args
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var exePath = trimmed[1..closingQuote];
                var remainder = trimmed[(closingQuote + 1)..].Trim();
                return (exePath, string.IsNullOrEmpty(remainder) ? null : remainder);
            }

            // Malformed: opening quote but no closing — strip quotes and return as-is
            return (trimmed.Trim('"'), null);
        }

        // Case 2: Unquoted path — split on first space (if path doesn't contain spaces)
        // But prefer checking if the whole string is a valid file path first
        if (File.Exists(trimmed))
            return (trimmed, null);

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var candidate = trimmed[..spaceIdx];
            var remainder = trimmed[(spaceIdx + 1)..].Trim();
            return (candidate, string.IsNullOrEmpty(remainder) ? null : remainder);
        }

        return (trimmed, null);
    }

    /// <summary>
    /// Generates a non-Steam shortcut appid using the same CRC algorithm Steam uses.
    /// The result is always negative as a signed int32 (high bit set).
    /// </summary>
    internal static int GenerateShortcutAppId(string exe, string appName)
    {
        // Steam algorithm: CRC32(exe + appname) | 0x80000000
        var input = System.Text.Encoding.UTF8.GetBytes(exe + appName);
        var crc = Crc32(input);
        return (int)(crc | 0x80000000);
    }

    private static uint Crc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320;
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }

    private IReadOnlyList<string> GetLibraryFolders(string steamPath)
    {
        if (_libraryFolders is not null)
            return _libraryFolders;

        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            _libraryFolders = [];
            return _libraryFolders;
        }

        var vdfContent = File.ReadAllText(libraryFoldersPath);
        _libraryFolders = ParseLibraryFolders(vdfContent);
        return _libraryFolders;
    }

    private string? FindGameNameFromManifest(string steamPath, int appId)
    {
        var libraryPaths = GetLibraryFolders(steamPath);

        foreach (var libPath in libraryPaths)
        {
            var manifestPath = Path.Combine(libPath, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var content = File.ReadAllText(manifestPath);
                var game = ParseAppManifest(content);
                if (game != null)
                    return game.Name;
            }
            catch
            {
                // Skip corrupt manifest
            }
        }

        return null;
    }

    private static string? FindShortcutName(string steamPath, int appId)
    {
        var shortcuts = LoadShortcuts(steamPath);
        return shortcuts.Find(s => s.AppId == appId)?.Name;
    }

    private List<SteamGame> LoadInstalledGames(string steamPath)
    {
        var libraryPaths = GetLibraryFolders(steamPath);
        if (libraryPaths.Count == 0)
            return [];

        var games = new List<SteamGame>();
        foreach (var libPath in libraryPaths)
        {
            var steamAppsDir = Path.Combine(libPath, "steamapps");
            if (!Directory.Exists(steamAppsDir))
                continue;

            foreach (var acfFile in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(acfFile);
                    var game = ParseAppManifest(content);
                    if (game != null)
                        games.Add(game);
                }
                catch
                {
                    // Skip corrupt manifests
                }
            }
        }

        // Limit regular Steam games to top 20 by last played
        var topSteamGames = games
            .OrderByDescending(g => g.LastPlayed)
            .Take(20)
            .ToList();

        // Discover non-Steam shortcuts from all userdata profiles — keep all for detection
        var shortcuts = LoadShortcuts(steamPath);
        topSteamGames.AddRange(shortcuts);

        return topSteamGames;
    }

    private static List<SteamGame> LoadShortcuts(string steamPath)
    {
        var shortcuts = new List<SteamGame>();
        var userDataDir = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userDataDir))
            return shortcuts;

        foreach (var userDir in Directory.EnumerateDirectories(userDataDir))
        {
            var shortcutsPath = Path.Combine(userDir, "config", "shortcuts.vdf");
            if (!File.Exists(shortcutsPath))
                continue;

            try
            {
                using var stream = File.OpenRead(shortcutsPath);
                var parsed = ParseShortcuts(stream, null);
                shortcuts.AddRange(parsed);
            }
            catch
            {
                // Skip corrupt shortcuts files
            }
        }

        // Deduplicate by AppId (same shortcut may appear under multiple Steam user profiles)
        return shortcuts
            .GroupBy(s => s.AppId)
            .Select(g => g.OrderByDescending(s => s.LastPlayed).First())
            .ToList();
    }

    private string? GetGameInstallDir(string steamPath, int appId)
    {
        // Search all library folders, not just the main Steam path
        var libraryPaths = GetLibraryFolders(steamPath);

        foreach (var libPath in libraryPaths)
        {
            var steamAppsDir = Path.Combine(libPath, "steamapps");
            var manifestPath = Path.Combine(steamAppsDir, $"appmanifest_{appId}.acf");

            if (!File.Exists(manifestPath))
                continue;

            var content = File.ReadAllText(manifestPath);
            var installDir = ParseInstallDir(content);

            if (!string.IsNullOrEmpty(installDir))
                return Path.Combine(steamAppsDir, "common", installDir);
        }

        return null;
    }

    // ── Artwork resolution ────────────────────────────────────────────

    private static readonly string[] ArtworkExtensions = ["png", "jpg", "jpeg", "webp"];

    /// <summary>
    /// Finds the artwork file for a given appId. Priority:
    /// 1. Custom grid art: userdata/{steamid}/config/grid/{appId}p.{ext}
    /// 2. Library cache: appcache/librarycache/{appId}_library_600x900.{ext}
    /// 3. Library cache fallbacks: _library_hero, _header, _logo
    /// For official games only (appId > 0), callers may try CDN after this returns null.
    /// </summary>
    internal static string? FindArtworkPath(string steamPath, string? steamUserId, int appId, ILogger? logger = null)
    {
        // For non-Steam shortcuts, use unsigned representation for filenames
        var fileId = IsShortcutAppId(appId) ? ((uint)appId).ToString() : appId.ToString();
        logger?.LogInformation("Artwork: lookup appId={AppId} fileId={FileId} isShortcut={IsShortcut}",
            appId, fileId, IsShortcutAppId(appId));

        // Priority 1: Custom grid art (user-set posters)
        if (steamUserId != null)
        {
            var gridDir = Path.Combine(steamPath, "userdata", steamUserId, "config", "grid");
            if (Directory.Exists(gridDir))
            {
                foreach (var ext in ArtworkExtensions)
                {
                    var path = Path.Combine(gridDir, $"{fileId}p.{ext}");
                    if (File.Exists(path))
                    {
                        logger?.LogDebug("Artwork: found in custom grid {Path} ({Size} KB)", path, new FileInfo(path).Length / 1024);
                        return path;
                    }
                }
                logger?.LogInformation("Artwork: not found in custom grid {GridDir}", gridDir);
            }
            else
            {
                logger?.LogWarning("Artwork: custom grid dir missing {GridDir}", gridDir);
            }
        }
        else
        {
            logger?.LogInformation("Artwork: no steamUserId, skipping custom grid lookup");
        }

        // Priority 2: Steam local library cache (multiple filename variants)
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache");
        if (Directory.Exists(cacheDir))
        {
            // Steam caches artwork with these suffixes — try most useful first
            string[] libraryCacheSuffixes = ["_library_600x900", "_library_hero", "_header", "_logo"];
            foreach (var suffix in libraryCacheSuffixes)
            {
                foreach (var ext in ArtworkExtensions)
                {
                    var path = Path.Combine(cacheDir, $"{fileId}{suffix}.{ext}");
                    if (File.Exists(path))
                    {
                        logger?.LogDebug("Artwork: found in library cache {Path} ({Size} KB)", path, new FileInfo(path).Length / 1024);
                        return path;
                    }
                }
            }
            logger?.LogInformation("Artwork: not found in library cache {CacheDir} (checked {Count} suffix variants)",
                cacheDir, libraryCacheSuffixes.Length);
        }
        else
        {
            logger?.LogWarning("Artwork: library cache dir missing {CacheDir}", cacheDir);
        }

        return null;
    }

    /// <summary>
    /// Returns diagnostic info about all artwork paths checked for a given appId.
    /// Does not download from CDN — purely local file checks.
    /// </summary>
    internal static ArtworkDiagnostics GetArtworkDiagnostics(string steamPath, string? steamUserId, int appId, string gameName)
    {
        var fileId = IsShortcutAppId(appId) ? ((uint)appId).ToString() : appId.ToString();
        var paths = new List<ArtworkPathCheck>();
        string? resolvedPath = null;

        // Priority 1: Custom grid art
        if (steamUserId != null)
        {
            var gridDir = Path.Combine(steamPath, "userdata", steamUserId, "config", "grid");
            foreach (var ext in ArtworkExtensions)
            {
                var path = Path.Combine(gridDir, $"{fileId}p.{ext}");
                var exists = File.Exists(path);
                long? size = exists ? new FileInfo(path).Length : null;
                paths.Add(new ArtworkPathCheck { Path = path, Category = "Custom Grid", Exists = exists, SizeBytes = size });
                if (exists && resolvedPath == null) resolvedPath = path;
            }
        }

        // Priority 2: Library cache
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache");
        string[] libraryCacheSuffixes = ["_library_600x900", "_library_hero", "_header", "_logo"];
        foreach (var suffix in libraryCacheSuffixes)
        {
            foreach (var ext in ArtworkExtensions)
            {
                var path = Path.Combine(cacheDir, $"{fileId}{suffix}.{ext}");
                var exists = File.Exists(path);
                long? size = exists ? new FileInfo(path).Length : null;
                paths.Add(new ArtworkPathCheck { Path = path, Category = $"Library Cache ({suffix})", Exists = exists, SizeBytes = size });
                if (exists && resolvedPath == null) resolvedPath = path;
            }
        }

        var cdnUrl = IsShortcutAppId(appId)
            ? ""
            : $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg";

        return new ArtworkDiagnostics
        {
            AppId = appId,
            FileId = fileId,
            GameName = gameName,
            IsShortcut = IsShortcutAppId(appId),
            ResolvedPath = resolvedPath,
            CdnUrl = cdnUrl,
            PathsChecked = paths
        };
    }

    public async Task<ArtworkDiagnostics?> GetArtworkDiagnosticsAsync(int appId)
    {
        var steamPath = platform.GetSteamPath();
        if (steamPath == null) return null;

        // Warm cache if needed
        await EnsureCacheWarmAsync();

        var gameName = _cachedGames?.FirstOrDefault(g => g.AppId == appId)?.Name ?? $"Unknown ({appId})";
        return GetArtworkDiagnostics(steamPath, platform.GetSteamUserId(), appId, gameName);
    }

    public async Task<List<ArtworkDiagnostics>> GetAllArtworkDiagnosticsAsync()
    {
        var games = await GetGamesAsync();
        var steamPath = platform.GetSteamPath();
        if (steamPath == null) return [];

        var steamUserId = platform.GetSteamUserId();
        return games.Select(g => GetArtworkDiagnostics(steamPath, steamUserId, g.AppId, g.Name)).ToList();
    }
}
