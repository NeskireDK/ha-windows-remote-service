using System.Runtime.Versioning;
using HaPcRemote.Service;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Endpoints;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Services;
using HaPcRemote.Shared.Configuration;

[assembly: SupportedOSPlatform("linux")]

var builder = WebApplication.CreateBuilder(args);

// Logging — console (captured by journalctl) + file
builder.Logging.ClearProviders();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider(ConfigPaths.GetLogFilePath()));

// Config
var writableConfigPath = ConfigPaths.GetWritableConfigPath();
builder.Configuration.AddJsonFile(writableConfigPath, optional: true, reloadOnChange: true);

// JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

// Configuration binding
builder.Services.Configure<PcRemoteOptions>(
    builder.Configuration.GetSection(PcRemoteOptions.SectionName));

// Resolve relative paths against exe directory
builder.AddPcRemoteOptions();

var pcRemoteConfig = builder.Configuration
    .GetSection(PcRemoteOptions.SectionName)
    .Get<PcRemoteOptions>() ?? new PcRemoteOptions();

// Generate API key if not configured
var generatedApiKey = HostBootstrapExtensions.BootstrapApiKey(builder.Configuration, pcRemoteConfig);
if (generatedApiKey != null)
{
    Console.WriteLine($"[STARTUP] Generated API key: {generatedApiKey}");
    Console.WriteLine($"[STARTUP] Key saved to {ConfigPaths.GetWritableConfigPath()}");
}

// Configure Kestrel port
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(pcRemoteConfig.Port);
});

// Services — Linux-native implementations
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICliRunner, CliRunner>();
builder.Services.AddSingleton<IAppLauncher, DirectAppLauncher>();
builder.Services.AddSingleton<IBigPictureTracker, BigPictureTracker>();
builder.Services.AddSingleton<AppService>();
builder.Services.AddSingleton<IAppService>(sp => sp.GetRequiredService<AppService>());
builder.Services.AddSingleton<IAudioService, LinuxAudioService>();
builder.Services.AddSingleton<IMonitorService, LinuxMonitorService>();
builder.Services.AddSingleton<IModeService, ModeService>();
builder.Services.AddHostedService<MdnsAdvertiserService>();
builder.Services.AddSingleton<IPowerService, LinuxPowerService>();
builder.Services.AddSingleton<IIdleService, LinuxIdleService>();
builder.Services.AddSingleton<ISteamPlatform, LinuxSteamPlatform>();
builder.Services.AddSingleton<IEmulatorTracker, EmulatorTracker>();
builder.Services.AddSingleton<ISteamService, SteamService>();
builder.Services.AddSingleton<IUpdateService, NoOpUpdateService>();
builder.Services.AddHostedService<AutoSleepService>();
builder.Services.AddSingleton<IRestartService, HostLifetimeRestartService>();

var app = builder.Build();

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

await app.RunAsync();
