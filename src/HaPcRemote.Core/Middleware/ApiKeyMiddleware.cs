using System.Security.Cryptography;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Middleware;

public sealed class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context, IOptionsMonitor<PcRemoteOptions> options)
    {
        var config = options.CurrentValue;

        if (!config.Auth.Enabled)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Only require auth for /api/ paths; skip non-API requests (favicon.ico, etc.)
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            IsExemptPath(path))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey) ||
            string.IsNullOrEmpty(providedKey))
        {
            logger.LogWarning("Request to {Path} missing API key", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("Missing API key"), AppJsonContext.Default.ApiResponse);
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(providedKey.ToString()),
                System.Text.Encoding.UTF8.GetBytes(config.Auth.ApiKey)))
        {
            logger.LogWarning("Request to {Path} with invalid API key", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                ApiResponse.Fail("Invalid API key"), AppJsonContext.Default.ApiResponse);
            return;
        }

        await next(context);
    }

    private static bool IsExemptPath(string path) =>
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        IsArtworkImagePath(path);

    private static bool IsArtworkImagePath(string path)
    {
        const string prefix = "/api/steam/artwork/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var segment = path[prefix.Length..];
        return !segment.Contains('/') && int.TryParse(segment, out _);
    }
}
