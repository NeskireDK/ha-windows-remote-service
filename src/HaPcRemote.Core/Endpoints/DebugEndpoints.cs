using System.Net;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Views;
using HaPcRemote.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Endpoints;

public static class DebugEndpoints
{
    private const string ApiKeyPlaceholder = "__API_KEY_PLACEHOLDER__";
    private const int DefaultLineCount = 200;
    private const int MaxLineCount = 2000;

    public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api-explorer", (HttpContext context, IOptionsMonitor<PcRemoteOptions> options) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return Results.Text("Forbidden", "text/plain", statusCode: 403);

            var apiKey = options.CurrentValue.Auth.ApiKey;
            var html = BuildHtml(apiKey);
            return Results.Content(html, "text/html");
        });

        endpoints.MapGet("/api/debug/logs", (HttpContext context, int? lines, string? level, string? category) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
                return Results.Text("Forbidden", "text/plain", statusCode: 403);

            var logPath = ConfigPaths.GetLogFilePath();
            if (!File.Exists(logPath))
                return Results.Json(new { lines = Array.Empty<object>(), file = logPath, exists = false });

            var requestedLines = Math.Clamp(lines ?? DefaultLineCount, 1, MaxLineCount);
            var allLines = File.ReadAllLines(logPath);

            IEnumerable<string> filtered = allLines;

            if (!string.IsNullOrEmpty(level))
                filtered = filtered.Where(l => l.Contains($"|{level.ToUpperInvariant()}|", StringComparison.Ordinal));

            if (!string.IsNullOrEmpty(category))
                filtered = filtered.Where(l => l.Contains(category, StringComparison.OrdinalIgnoreCase));

            var result = filtered.TakeLast(requestedLines).Select(ParseLogLine).ToArray();

            return Results.Json(new { lines = result, file = logPath, total = allLines.Length, returned = result.Length });
        });

        return endpoints;
    }

    private static object ParseLogLine(string line)
    {
        var parts = line.Split('|', 4);
        if (parts.Length == 4)
            return new { timestamp = parts[0], level = parts[1], category = parts[2], message = parts[3] };
        return new { timestamp = "", level = "", category = "", message = line };
    }

    private static string BuildHtml(string apiKey)
    {
        var template = EmbeddedResourceHelper.LoadResource("api-explorer.html");
        return template.Replace(ApiKeyPlaceholder, apiKey);
    }
}
