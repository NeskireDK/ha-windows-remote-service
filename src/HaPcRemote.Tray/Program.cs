using HaPcRemote.Tray;
using HaPcRemote.Tray.Logging;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        using var mutex = new Mutex(false, @"Local\HaPcRemoteTray");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;

        var logProvider = new InMemoryLogProvider();
        var webCts = new CancellationTokenSource();

        // Build the initial web application.  KestrelRestartService is created inside Build()
        // on the first call; we retrieve it so we can wire the RestartAsync delegate and pass
        // the same instance into subsequent builds (so tabs resolve a stable singleton).
        var webApp = TrayWebHost.Build(logProvider);
        var restartService = webApp.Services.GetRequiredService<KestrelRestartService>();

        // Active web application reference — swapped on each restart.
        WebApplication currentApp = webApp;
        var restartLock = new SemaphoreSlim(1, 1);

        restartService.RestartAsync = async newPort =>
        {
            await restartLock.WaitAsync();
            try
            {
                KestrelStatus.Reset();

                await currentApp.StopAsync(TimeSpan.FromSeconds(5));
                await currentApp.DisposeAsync();

                var newApp = TrayWebHost.Build(logProvider, restartService);
                currentApp = newApp;

                await newApp.StartAsync();
                KestrelStatus.SetRunning();
            }
            catch (Exception ex)
            {
                KestrelStatus.SetFailed(ex.InnerException?.Message ?? ex.Message);
                throw;
            }
            finally
            {
                restartLock.Release();
            }
        };

        _ = Task.Run(async () =>
        {
            try
            {
                await webApp.StartAsync(webCts.Token);
                KestrelStatus.SetRunning();
            }
            catch (Exception ex)
            {
                KestrelStatus.SetFailed(ex.InnerException?.Message ?? ex.Message);
            }
        });

        Application.Run(new TrayApplicationContext(() => currentApp.Services, webCts, logProvider));

        webCts.Cancel();
        try { currentApp.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); } catch { }
    }
}
