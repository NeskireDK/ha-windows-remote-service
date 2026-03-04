using HaPcRemote.Service;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Endpoints;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using HaPcRemote.Shared.Configuration;
using HaPcRemote.Tray.Logging;
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
        var writableConfigDir = ConfigPaths.GetWritableConfigDir();
        var writableConfigPath = ConfigPaths.GetWritableConfigPath();
        builder.Configuration.AddJsonFile(writableConfigPath, optional: true, reloadOnChange: true);

        // JSON serialization
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

        // Configuration binding
        builder.Services.Configure<PcRemoteOptions>(
            builder.Configuration.GetSection(PcRemoteOptions.SectionName));

        // Resolve relative paths: tools against exe dir, profiles against user config dir
        builder.Services.PostConfigure<PcRemoteOptions>(options =>
        {
            var baseDir = AppContext.BaseDirectory;
            if (!Path.IsPathRooted(options.ToolsPath))
                options.ToolsPath = Path.GetFullPath(options.ToolsPath, baseDir);
            if (!Path.IsPathRooted(options.ProfilesPath))
                options.ProfilesPath = Path.GetFullPath(options.ProfilesPath, ConfigPaths.GetWritableConfigDir());
            foreach (var app in options.Apps.Values)
            {
                if (!string.IsNullOrEmpty(app.ExePath) && !Path.IsPathRooted(app.ExePath))
                    app.ExePath = Path.GetFullPath(app.ExePath, baseDir);
            }
        });

        var pcRemoteConfig = builder.Configuration
            .GetSection(PcRemoteOptions.SectionName)
            .Get<PcRemoteOptions>() ?? new PcRemoteOptions();

        // Generate API key if not configured
        if (pcRemoteConfig.Auth.Enabled && string.IsNullOrEmpty(pcRemoteConfig.Auth.ApiKey))
        {
            string configPath;
            try
            {
                Directory.CreateDirectory(writableConfigDir);
                configPath = writableConfigPath;
            }
            catch
            {
                configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            }
            var generatedKey = ApiKeyService.GenerateApiKey();
            ApiKeyService.WriteApiKeyToConfig(configPath, generatedKey);
            builder.Configuration.GetSection("PcRemote:Auth:ApiKey").Value = generatedKey;
        }

        // Configure Kestrel port
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(pcRemoteConfig.Port);
        });

        // Services — direct, no IPC (tray runs in user session)
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ICliRunner, CliRunner>();
        builder.Services.AddSingleton<IAppLauncher, DirectAppLauncher>();
        builder.Services.AddSingleton<AppService>();
        builder.Services.AddSingleton<IAudioService, AudioService>();
        builder.Services.AddSingleton<IMonitorService, MonitorService>();
        builder.Services.AddSingleton<IModeService, ModeService>();
        builder.Services.AddHostedService<MdnsAdvertiserService>();
        builder.Services.AddHostedService<AutoSleepService>();
        builder.Services.AddSingleton<IPowerService, WindowsPowerService>();
        builder.Services.AddSingleton<IIdleService, WindowsIdleService>();
        builder.Services.AddSingleton<ISteamPlatform, WindowsSteamPlatform>();
        builder.Services.AddSingleton<ISteamService, SteamService>();
        builder.Services.AddSingleton<IUpdateService, UpdateService>();
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
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TrayWebHost));
                logger.LogError(ex, "Unhandled exception");
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(
                        ApiResponse.Fail("Internal server error"),
                        AppJsonContext.Default.ApiResponse);
                }
            }
        });

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
