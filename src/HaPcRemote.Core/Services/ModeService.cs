using HaPcRemote.Service.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

public class ModeService(
    IOptionsMonitor<PcRemoteOptions> options,
    IAudioService audioService,
    IMonitorService monitorService,
    IAppService appService,
    ILogger<ModeService> logger) : IModeService
{
    public IReadOnlyList<string> GetModeNames() =>
        options.CurrentValue.Modes.Keys.ToList();

    public async Task ApplyModeAsync(string modeName)
    {
        var modes = options.CurrentValue.Modes;
        if (!modes.TryGetValue(modeName, out var config))
            throw new KeyNotFoundException($"Mode '{modeName}' not found.");

        if (config.AudioDevice is not null)
            await audioService.SetDefaultDeviceAsync(config.AudioDevice);

        if (config.SoloMonitor is not null)
            await monitorService.SoloMonitorAsync(config.SoloMonitor);

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
