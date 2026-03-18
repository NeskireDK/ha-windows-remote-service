using System.Collections.Concurrent;
using System.Text.Json;
using HaPcRemote.Shared.Configuration;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

public sealed class EmulatorTracker : IEmulatorTracker
{
    private readonly ILogger<EmulatorTracker> _logger;
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, EmulatorLaunchRecord> _launches = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _loaded;
    private readonly object _loadLock = new();

    public EmulatorTracker(ILogger<EmulatorTracker> logger)
        : this(logger, Path.Combine(ConfigPaths.GetWritableConfigDir(), "emulator-launches.json"))
    {
    }

    internal EmulatorTracker(ILogger<EmulatorTracker> logger, string filePath)
    {
        _logger = logger;
        _filePath = filePath;
    }

    public void TrackLaunch(string exePath, int appId, string gameName)
    {
        EnsureLoaded();

        var normalized = NormalizePath(exePath);
        _launches[normalized] = new EmulatorLaunchRecord(appId, gameName, DateTimeOffset.UtcNow);

        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null)
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_launches, EmulatorTrackerJsonContext.Default.ConcurrentDictionaryStringEmulatorLaunchRecord);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EmulatorTracker: failed to persist to {Path}", _filePath);
        }
    }

    public (int AppId, string Name)? GetLastLaunched(string exePath)
    {
        EnsureLoaded();

        var normalized = NormalizePath(exePath);
        if (_launches.TryGetValue(normalized, out var record))
            return (record.AppId, record.Name);

        return null;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;

        lock (_loadLock)
        {
            if (_loaded) return;

            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var data = JsonSerializer.Deserialize(json, EmulatorTrackerJsonContext.Default.DictionaryStringEmulatorLaunchRecord);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                            _launches[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EmulatorTracker: failed to load from {Path}", _filePath);
            }

            _loaded = true;
        }
    }

    private static string NormalizePath(string path) =>
        OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path;
}

public sealed record EmulatorLaunchRecord(int AppId, string Name, DateTimeOffset Timestamp);
