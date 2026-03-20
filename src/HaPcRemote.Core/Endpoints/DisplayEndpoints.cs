using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Endpoints;

public static class DisplayEndpoints
{
    public static RouteGroupBuilder MapDisplayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/system/display");
        group.AddEndpointFilter<EndpointExceptionFilter>();

        group.MapGet("/", (IOptionsMonitor<PcRemoteOptions> options) =>
        {
            return Results.Json(
                ApiResponse.Ok(new DisplayConfig { DisplayActionDelayMs = options.CurrentValue.DisplayActionDelayMs }),
                AppJsonContext.Default.ApiResponseDisplayConfig);
        });

        group.MapPut("/", (DisplayConfig body, IConfigurationWriter writer) =>
        {
            writer.SaveDisplayActionDelay(body.DisplayActionDelayMs);
            return Results.Json(ApiResponse.Ok("Display settings saved"), AppJsonContext.Default.ApiResponse);
        });

        return group;
    }
}
