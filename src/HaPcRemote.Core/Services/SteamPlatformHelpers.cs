using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

internal static class SteamPlatformHelpers
{
    /// <summary>
    /// Kills all processes whose main module path starts with <paramref name="directory"/>.
    /// Access-denied and already-exited processes are logged at Warning and skipped.
    /// </summary>
    internal static void KillProcessesInDirectory(string directory, ILogger logger)
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (path != null && path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to kill process {ProcessName} ({Pid}) in directory {Directory}",
                    proc.ProcessName, proc.Id, directory);
            }
            finally
            {
                proc.Dispose();
            }
        }
    }
}
