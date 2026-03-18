using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Restart implementation that stops the host via <see cref="IHostApplicationLifetime"/>.
/// Used by the headless (Linux/systemd) host where the process manager handles the actual restart.
/// </summary>
public sealed class HostLifetimeRestartService(
    IHostApplicationLifetime lifetime,
    ILogger<HostLifetimeRestartService> logger) : IRestartService
{
    public void ScheduleRestart()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(500, lifetime.ApplicationStopping);
            if (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                logger.LogInformation("Stopping host for restart");
                lifetime.StopApplication();
            }
        }, lifetime.ApplicationStopping);
    }
}
