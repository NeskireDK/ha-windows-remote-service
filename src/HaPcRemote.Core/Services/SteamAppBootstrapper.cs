using HaPcRemote.Service.Configuration;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Writes default "steam" and "steam-bigpicture" app entries to config on every startup
/// if Steam is installed and either entry is absent. Windows-only; no-op on other platforms.
/// </summary>
public static class SteamAppBootstrapper
{
    public static void BootstrapIfNeeded(
        ISteamPlatform platform,
        IConfigurationWriter writer,
        PcRemoteOptions currentOptions,
        ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var needsSteam = !currentOptions.Apps.ContainsKey("steam");

        // Always overwrite steam-bigpicture — it's a fixed URI, not user-configurable,
        // and existing installs may have the old steam.exe -bigpicture entry.
        if (!needsSteam && currentOptions.Apps.TryGetValue("steam-bigpicture", out var existing)
            && existing.ExePath.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
            return;

        var steamPath = platform.GetSteamPath();
        if (string.IsNullOrEmpty(steamPath))
            return;

        var exePath = Path.Combine(steamPath, "steam.exe");
        if (!File.Exists(exePath))
            return;

        if (needsSteam)
        {
            writer.SaveApp("steam", new AppDefinitionOptions
            {
                DisplayName = "Steam",
                ExePath = exePath,
                Arguments = "",
                ProcessName = "steam",
                UseShellExecute = false
            });

            logger.LogInformation("Auto-registered Steam app entry: {ExePath}", exePath);
        }

        {
            writer.SaveApp("steam-bigpicture", new AppDefinitionOptions
            {
                DisplayName = "Steam Big Picture",
                ExePath = "steam://open/bigpicture",
                Arguments = null,
                ProcessName = "steam",
                UseShellExecute = true
            });

            logger.LogInformation("Auto-registered Steam Big Picture app entry via steam:// URI");
        }
    }
}
