using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using HaPcRemote.Shared.Configuration;

namespace HaPcRemote.Service;

public static class HostBootstrapExtensions
{
    /// <summary>
    /// Resolves relative ToolsPath and app ExePaths against the executable directory.
    /// </summary>
    public static IHostApplicationBuilder AddPcRemoteOptions(this IHostApplicationBuilder builder)
    {
        builder.Services.PostConfigure<PcRemoteOptions>(options =>
        {
            var baseDir = AppContext.BaseDirectory;
            if (!Path.IsPathRooted(options.ToolsPath))
                options.ToolsPath = Path.GetFullPath(options.ToolsPath, baseDir);
            foreach (var app in options.Apps.Values)
            {
                if (!string.IsNullOrEmpty(app.ExePath) && !Path.IsPathRooted(app.ExePath))
                    app.ExePath = Path.GetFullPath(app.ExePath, baseDir);
            }
        });
        return builder;
    }

    /// <summary>
    /// Generates and persists an API key if auth is enabled and no key is configured yet.
    /// Returns the generated key (or null if none was generated).
    /// </summary>
    public static string? BootstrapApiKey(
        IConfigurationManager configuration,
        PcRemoteOptions pcRemoteConfig)
    {
        if (!pcRemoteConfig.Auth.Enabled || !string.IsNullOrEmpty(pcRemoteConfig.Auth.ApiKey))
            return null;

        var writableConfigDir = ConfigPaths.GetWritableConfigDir();
        var writableConfigPath = ConfigPaths.GetWritableConfigPath();

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
        configuration.GetSection("PcRemote:Auth:ApiKey").Value = generatedKey;
        return generatedKey;
    }

    /// <summary>
    /// Adds global exception-handling middleware that returns a JSON ApiResponse on unhandled errors.
    /// </summary>
    public static WebApplication UseApiResponseExceptionHandler(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(nameof(HostBootstrapExtensions));
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
        return app;
    }
}
