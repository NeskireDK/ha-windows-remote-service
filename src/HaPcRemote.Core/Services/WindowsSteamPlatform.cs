using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsSteamPlatform(ILogger<WindowsSteamPlatform> logger) : ISteamPlatform
{
    public string? GetSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }

    public string? GetSteamUserId()
    {
        var steamPath = GetSteamPath();
        return steamPath != null ? SteamUserIdResolver.Resolve(steamPath) : null;
    }

    public int GetRunningAppId()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
        return key?.GetValue("RunningAppID") is int appId ? appId : 0;
    }

    public bool IsSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

    public void LaunchSteamUrl(string url)
    {
        logger.LogInformation("Launching Steam URL: {Url}", url);
        using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        if (process is null)
        {
            logger.LogWarning(
                "Steam URL launch returned null process handle — Steam may not be installed or the steam:// protocol is not registered: {Url}",
                url);
        }
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
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE ExecutablePath IS NOT NULL");
            using var results = searcher.Get();
            foreach (var obj in results.OfType<ManagementObject>())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var path = obj["ExecutablePath"] as string;
                var cmdLine = obj["CommandLine"] as string;
                if (path != null)
                    processes.Add(new RunningProcess(pid, path, cmdLine));
                obj.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WMI process query failed, falling back to Process API");
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (path != null)
                        processes.Add(new RunningProcess(proc.Id, path, null));
                }
                catch (Exception innerEx)
                {
                    logger.LogWarning(innerEx, "Failed to read process path for {ProcessName} during fallback", proc.ProcessName);
                }
                finally { proc.Dispose(); }
            }
        }
        return processes;
    }
}
