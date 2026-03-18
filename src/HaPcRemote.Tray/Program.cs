using HaPcRemote.Tray;
using HaPcRemote.Tray.Logging;

internal static class Program
{
    // Volatile so the STA thread and the Task.Run restart lambda always see the latest reference.
    private static volatile WebApplication? _currentApp;

    [STAThread]
    private static void Main()
    {
        // Microsoft.NET.Sdk.Web may override [STAThread]. If the current thread
        // is not STA, re-launch on an explicit STA thread so WinForms, COM, OLE,
        // clipboard, and drag-drop all work correctly.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Thread staThread = new(RunApplication) { Name = "WinForms-STA" };
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();
            return;
        }

        RunApplication();
    }

    private static void RunApplication()
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
        _currentApp = webApp;
        var restartLock = new SemaphoreSlim(1, 1);

        restartService.RestartAsync = async newPort =>
        {
            await restartLock.WaitAsync();
            try
            {
                KestrelStatus.Reset();

                var oldApp = _currentApp!;
                await oldApp.StopAsync(TimeSpan.FromSeconds(5));
                await oldApp.DisposeAsync();

                var newApp = TrayWebHost.Build(logProvider, restartService);
                _currentApp = newApp;

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

        Application.Run(new TrayApplicationContext(() => _currentApp!.Services, webCts, logProvider));

        webCts.Cancel();
        try { _currentApp?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); } catch { }
    }
}
