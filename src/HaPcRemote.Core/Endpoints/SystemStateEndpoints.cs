using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Endpoints;

public static class SystemStateEndpoints
{
    public static IEndpointRouteBuilder MapSystemStateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/system/state", async (
            IAudioService audioService,
            IMonitorService monitorService,
            ISteamService steamService,
            IModeService modeService,
            IIdleService idleService,
            IOptionsMonitor<PcRemoteOptions> options,
            ILogger<SystemState> logger) =>
        {
            // Fire all async calls concurrently
            var audioTask = GetAudioStateAsync(audioService);
            var monitorsTask = monitorService.GetMonitorsAsync();
            var steamGamesTask = steamService.GetGamesAsync();
            var runningGameTask = steamService.GetRunningGameAsync();

            try { await Task.WhenAll(audioTask, monitorsTask, steamGamesTask, runningGameTask); }
            catch (Exception ex) { logger.LogDebug(ex, "One or more state queries failed"); }

            // Extract results — each in its own try/catch for partial failure
            AudioState? audio = null;
            try { audio = await audioTask; }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get audio state"); }

            List<MonitorInfo>? monitors = null;
            try { monitors = await monitorsTask; }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get monitors"); }

            List<SteamGame>? steamGames = null;
            try { steamGames = await steamGamesTask; }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get Steam games"); }

            SteamRunningGame? runningGame = null;
            try { runningGame = await runningGameTask; }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get running Steam game"); }

            List<string>? modes = null;
            try { modes = modeService.GetModeNames().ToList(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get modes"); }

            int? idleSeconds = null;
            try { idleSeconds = idleService.GetIdleSeconds(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get idle duration"); }

            SteamBindings? steamBindings = null;
            try { steamBindings = steamService.GetBindings(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get steam bindings"); }

            bool? steamReady = null;
            try { steamReady = steamService.IsSteamRunning(); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to get Steam ready state"); }

            var state = new SystemState
            {
                Audio = audio,
                Monitors = monitors,
                SteamGames = steamGames,
                RunningGame = runningGame,
                Modes = modes,
                IdleSeconds = idleSeconds,
                SteamBindings = steamBindings,
                SteamReady = steamReady,
                AutoSleepAfterMinutes = options.CurrentValue.Power.AutoSleepAfterMinutes
            };

            return Results.Json(
                ApiResponse.Ok(state),
                AppJsonContext.Default.ApiResponseSystemState);
        });

        return endpoints;
    }

    private static async Task<AudioState> GetAudioStateAsync(IAudioService audioService)
    {
        var devices = await audioService.GetDevicesAsync();
        var current = devices.Find(d => d.IsDefault);
        return new AudioState
        {
            Devices = devices,
            Current = current?.Name,
            Volume = current?.Volume
        };
    }

}
