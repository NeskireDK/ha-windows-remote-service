using System.Diagnostics;
using System.Runtime.Versioning;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;
using ValveKeyValue;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxSteamPlatform(ILogger<LinuxSteamPlatform> logger) : ISteamPlatform
{
    private static readonly string[] KnownSteamPaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "steam"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Steam"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".var", "app",
            "com.valvesoftware.Steam", "data", "Steam"),
    ];

    public string? GetSteamPath()
    {
        foreach (var path in KnownSteamPaths)
        {
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }

    public string? GetSteamUserId()
    {
        var steamPath = GetSteamPath();
        return steamPath != null ? SteamUserIdResolver.Resolve(steamPath) : null;
    }

    public int GetRunningAppId()
    {
        var steamPath = GetSteamPath();
        if (steamPath is null) return 0;

        // Steam writes RunningAppID to registry.vdf when a game is running
        var registryVdf = Path.Combine(steamPath, "registry.vdf");
        if (!File.Exists(registryVdf)) return 0;

        try
        {
            using var stream = File.OpenRead(registryVdf);
            var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var root = kv.Deserialize(stream);

            // Path: Registry/HKCU/Software/Valve/Steam/RunningAppID
            var steamNode = FindChild(FindChild(FindChild(FindChild(root, "HKCU"), "Software"), "Valve"), "Steam");
            if (steamNode is null) return 0;

            var value = steamNode["RunningAppID"]?.ToString();
            if (value is not null && int.TryParse(value, out var appId))
                return appId;
        }
        catch (Exception)
        {
            // VDF parsing can fail if the file is being written to by Steam
        }

        return 0;
    }

    private static KVObject? FindChild(KVObject? parent, string name)
    {
        if (parent is null) return null;
        foreach (var child in parent)
        {
            if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    public bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

    public void LaunchSteamUrl(string url)
    {
        // UseShellExecute on Linux delegates to xdg-open, which handles steam:// URIs
        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void KillProcessesInDirectory(string directory) =>
        SteamPlatformHelpers.KillProcessesInDirectory(directory, logger);

    public void KillProcess(int processId)
    {
        try
        {
            using var proc = Process.GetProcessById(processId);
            proc.Kill(entireProcessTree: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to kill process {Pid}", processId);
        }
    }

    public IEnumerable<string> GetRunningProcessPaths()
    {
        var paths = new List<string>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (path != null)
                    paths.Add(path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read process path for {ProcessName}", proc.ProcessName);
            }
            finally { proc.Dispose(); }
        }
        return paths;
    }

    public IEnumerable<RunningProcess> GetRunningProcesses()
    {
        var processes = new List<RunningProcess>();
        if (!Directory.Exists("/proc")) return processes;

        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!int.TryParse(name, out var pid)) continue;

            try
            {
                // Resolve /proc/<pid>/exe symlink for the executable path
                var exeLink = Path.Combine(dir, "exe");
                var path = File.ResolveLinkTarget(exeLink, returnFinalTarget: true)?.FullName;
                if (path is null) continue;

                // Read /proc/<pid>/cmdline (null-delimited)
                var cmdlineFile = Path.Combine(dir, "cmdline");
                string? cmdLine = null;
                if (File.Exists(cmdlineFile))
                {
                    var raw = File.ReadAllText(cmdlineFile);
                    if (!string.IsNullOrEmpty(raw))
                        cmdLine = raw.Replace('\0', ' ').TrimEnd();
                }

                processes.Add(new RunningProcess(pid, path, cmdLine));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read process info for pid {Pid}", pid);
            }
        }

        return processes;
    }
}
