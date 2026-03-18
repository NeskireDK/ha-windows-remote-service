using HaPcRemote.Service;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Endpoints;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Native;
using HaPcRemote.Service.Services;
using HaPcRemote.Shared.Configuration;
using HaPcRemote.Tray.Logging;
using HaPcRemote.Tray.Models;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Tray;

internal static class TrayWebHost
{
    public static WebApplication Build(InMemoryLogProvider logProvider, KestrelRestartService? restartService = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Logging — file + in-memory (shared with tray log viewer)
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddProvider(new FileLoggerProvider(ConfigPaths.GetLogFilePath()));
        builder.Logging.AddProvider(logProvider);

        // Config
        var writableConfigPath = ConfigPaths.GetWritableConfigPath();
        builder.Configuration.AddJsonFile(writableConfigPath, optional: true, reloadOnChange: true);

        // JSON serialization
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

        // Configuration binding
        builder.Services.Configure<PcRemoteOptions>(
            builder.Configuration.GetSection(PcRemoteOptions.SectionName));

        // Resolve relative paths: tools against exe dir, profiles against user config dir
        builder.AddPcRemoteOptions();

        var pcRemoteConfig = builder.Configuration
            .GetSection(PcRemoteOptions.SectionName)
            .Get<PcRemoteOptions>() ?? new PcRemoteOptions();

        // Generate API key if not configured
        HostBootstrapExtensions.BootstrapApiKey(builder.Configuration, pcRemoteConfig);

        // Configure Kestrel port
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(pcRemoteConfig.Port);
        });

        // Services — direct, no IPC (tray runs in user session)
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ICliRunner, CliRunner>();
        builder.Services.AddSingleton<IAppLauncher, DirectAppLauncher>();
        builder.Services.AddSingleton<IBigPictureTracker, BigPictureTracker>();
        builder.Services.AddSingleton<AppService>();
        builder.Services.AddSingleton<IAppService>(sp => sp.GetRequiredService<AppService>());
        builder.Services.AddSingleton<IAudioService, AudioService>();
        builder.Services.AddSingleton<IDisplayConfigApi, DisplayConfigHelper>();
        builder.Services.AddSingleton<IMonitorService, WindowsMonitorService>();
        builder.Services.AddSingleton<IModeService, ModeService>();
        builder.Services.AddHostedService<MdnsAdvertiserService>();
        builder.Services.AddHostedService<AutoSleepService>();
        builder.Services.AddSingleton<IPowerService, WindowsPowerService>();
        builder.Services.AddSingleton<IIdleService, WindowsIdleService>();
        builder.Services.AddSingleton<ISteamPlatform, WindowsSteamPlatform>();
        builder.Services.AddSingleton<IEmulatorTracker, EmulatorTracker>();
        builder.Services.AddSingleton<ISteamService, SteamService>();
        builder.Services.AddSingleton<IUpdateService>(sp => new UpdateService(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<UpdateService>>(),
            includePrereleases: () => TraySettings.Load().IncludePrereleases));
        builder.Services.AddSingleton<IConfigurationWriter>(
            new ConfigurationWriter(writableConfigPath));

        // KestrelRestartService is a singleton registered in DI so GeneralTab can resolve it.
        // Program.cs sets the RestartAsync delegate on it after the first app is built.
        // On subsequent builds (after a restart) the same instance is passed in so the delegate persists.
        var restart = restartService ?? new KestrelRestartService();
        builder.Services.AddSingleton(restart);
        builder.Services.AddSingleton<IRestartService, TrayRestartService>();

        var app = builder.Build();

        // Auto-register Steam app entry on first run
        SteamAppBootstrapper.BootstrapIfNeeded(
            app.Services.GetRequiredService<ISteamPlatform>(),
            app.Services.GetRequiredService<IConfigurationWriter>(),
            pcRemoteConfig,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SteamAppBootstrapper)));

        // Global exception handler
        app.UseApiResponseExceptionHandler();

        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapDebugEndpoints();
        app.MapHealthEndpoints();
        app.MapSystemEndpoints();
        app.MapModeEndpoints();
        app.MapSystemStateEndpoints();
        app.MapAppEndpoints();
        app.MapAudioEndpoints();
        app.MapMonitorEndpoints();
        app.MapSteamEndpoints();
        app.MapArtworkDebugEndpoints();
        app.MapPowerEndpoints();

        return app;
    }
}
