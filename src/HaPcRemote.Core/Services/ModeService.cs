using System.ComponentModel;
using HaPcRemote.Service.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

public sealed class ModeService(
    IOptionsMonitor<PcRemoteOptions> options,
    IAudioService audioService,
    IMonitorService monitorService,
    IAppService appService,
    ILogger<ModeService> logger) : IModeService
{
    private const int MaxRetries = 4;

    public IReadOnlyList<string> GetModeNames() =>
        options.CurrentValue.Modes.Keys.ToList();

    public async Task ApplyModeAsync(string modeName)
    {
        var modes = options.CurrentValue.Modes;
        if (!modes.TryGetValue(modeName, out var config))
            throw new KeyNotFoundException($"Mode '{modeName}' not found.");

        var baseDelay = options.CurrentValue.DisplayActionDelayMs;
        if (baseDelay <= 0)
        {
            await ApplyModeCoreAsync(config);
            return;
        }

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await ApplyModeCoreAsync(config);
                if (attempt > 0)
                    logger.LogInformation("Mode '{Mode}' applied on attempt {Attempt}", modeName, attempt + 1);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransientError(ex))
            {
                var delay = baseDelay * (1 << attempt);
                logger.LogWarning(
                    "Mode '{Mode}' failed on attempt {Attempt}/{Max}, retrying in {Delay}ms: {Error}",
                    modeName, attempt + 1, MaxRetries + 1, delay, ex.Message);
                await Task.Delay(delay);
            }
        }
    }

    /// <summary>
    /// Returns true for transient errors worth retrying at the mode level.
    /// Error 87 (ERROR_INVALID_PARAMETER) is a configuration error — WindowsMonitorService
    /// already retries internally. If it still fails, retrying at this level won't help. (#119)
    /// </summary>
    private static bool IsTransientError(Exception ex) =>
        ex is not Win32Exception { NativeErrorCode: 87 /* ERROR_INVALID_PARAMETER */ };

    private async Task ApplyModeCoreAsync(ModeConfig config)
    {
        // Monitor first — audio devices may only appear after the monitor is active
        if (config.SoloMonitor is not null)
            await monitorService.SoloMonitorAsync(config.SoloMonitor);

        // Audio after monitor — HDMI/DP audio is tied to the display
        if (config.AudioDevice is not null)
            await audioService.SetDefaultDeviceAsync(config.AudioDevice);

        if (config.Volume.HasValue)
            await audioService.SetVolumeAsync(config.Volume.Value);

        if (config.KillApp is not null)
            await appService.KillAsync(config.KillApp);

        if (config.KillApp is not null && config.LaunchApp is not null)
            await Task.Delay(config.KillToLaunchDelayMs ?? 1000);

        if (config.LaunchApp is not null)
            await appService.LaunchAsync(config.LaunchApp);
    }
}
