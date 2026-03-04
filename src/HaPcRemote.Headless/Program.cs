using System.Runtime.Versioning;
using HaPcRemote.Service;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Endpoints;
using HaPcRemote.Service.Logging;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Models;
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
var writableConfigDir = ConfigPaths.GetWritableConfigDir();
var writableConfigPath = ConfigPaths.GetWritableConfigPath();
builder.Configuration.AddJsonFile(writableConfigPath, optional: true, reloadOnChange: true);

// JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));

// Configuration binding
builder.Services.Configure<PcRemoteOptions>(
    builder.Configuration.GetSection(PcRemoteOptions.SectionName));

// Resolve relative paths against exe directory
builder.Services.PostConfigure<PcRemoteOptions>(options =>
{
    var baseDir = AppContext.BaseDirectory;
    options.ToolsPath = ResolveRelativePath(options.ToolsPath, baseDir);
    options.ProfilesPath = ResolveRelativePath(options.ProfilesPath, baseDir);
    foreach (var app in options.Apps.Values)
    {
        if (!string.IsNullOrEmpty(app.ExePath))
            app.ExePath = ResolveRelativePath(app.ExePath, baseDir);
    }
});

static string ResolveRelativePath(string path, string baseDir) =>
    Path.IsPathRooted(path) ? path : Path.GetFullPath(path, baseDir);

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
    catch (Exception ex)
    {
        Console.WriteLine($"[STARTUP] Failed to create config directory {writableConfigDir}: {ex.Message}");
        configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    }
    var generatedKey = ApiKeyService.GenerateApiKey();
    ApiKeyService.WriteApiKeyToConfig(configPath, generatedKey);
    builder.Configuration.GetSection("PcRemote:Auth:ApiKey").Value = generatedKey;
    Console.WriteLine($"[STARTUP] Generated API key: {generatedKey}");
    Console.WriteLine($"[STARTUP] Key saved to {configPath}");
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
builder.Services.AddSingleton<AppService>();
builder.Services.AddSingleton<IAudioService, LinuxAudioService>();
builder.Services.AddSingleton<IMonitorService, LinuxMonitorService>();
builder.Services.AddSingleton<IModeService, ModeService>();
builder.Services.AddHostedService<MdnsAdvertiserService>();
builder.Services.AddSingleton<IPowerService, LinuxPowerService>();
builder.Services.AddSingleton<IIdleService, LinuxIdleService>();
builder.Services.AddSingleton<ISteamPlatform, LinuxSteamPlatform>();
builder.Services.AddSingleton<ISteamService, SteamService>();
builder.Services.AddSingleton<IUpdateService, NoOpUpdateService>();
builder.Services.AddSingleton<IRestartService, HostLifetimeRestartService>();

var app = builder.Build();

// Global exception handler
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception");
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("Internal server error"),
                AppJsonContext.Default.Options,
                context.RequestAborted);
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

await app.RunAsync();
