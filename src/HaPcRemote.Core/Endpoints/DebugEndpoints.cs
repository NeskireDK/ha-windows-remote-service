using System.Net;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Views;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Endpoints;

public static class DebugEndpoints
{
    private const string ApiKeyPlaceholder = "__API_KEY_PLACEHOLDER__";

    public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api-explorer", (HttpContext context, IOptionsMonitor<PcRemoteOptions> options) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !IPAddress.IsLoopback(remote))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Results.Text("Forbidden", "text/plain", statusCode: 403);
            }

            var apiKey = options.CurrentValue.Auth.ApiKey;
            var html = BuildHtml(apiKey);
            return Results.Content(html, "text/html");
        });

        return endpoints;
    }

    private static string BuildHtml(string apiKey)
    {
        var template = EmbeddedResourceHelper.LoadResource("api-explorer.html");
        return template.Replace(ApiKeyPlaceholder, apiKey);
    }
}
