using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;

namespace HaPcRemote.Service.Endpoints;

public static class MonitorEndpoints
{
    public static RouteGroupBuilder MapMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/monitor");
        group.AddEndpointFilter<EndpointExceptionFilter>();

        // ── Monitor control endpoints ────────────────────────────────

        group.MapGet("/list", async (IMonitorService monitorService) =>
        {
            var monitors = await monitorService.GetMonitorsAsync();
            return Results.Json(
                ApiResponse.Ok<List<MonitorInfo>>(monitors),
                AppJsonContext.Default.ApiResponseListMonitorInfo);
        });

        group.MapPost("/solo/{id}", async (string id, IMonitorService monitorService,
            ILogger<IMonitorService> logger) =>
        {
            logger.LogInformation("Solo monitor '{Id}' requested", id);
            await monitorService.SoloMonitorAsync(id);
            return Results.Json(
                ApiResponse.Ok($"Solo monitor '{id}' applied"),
                AppJsonContext.Default.ApiResponse);
        });

        group.MapPost("/enable/{id}", async (string id, IMonitorService monitorService,
            ILogger<IMonitorService> logger) =>
        {
            logger.LogInformation("Enable monitor '{Id}' requested", id);
            await monitorService.EnableMonitorAsync(id);
            return Results.Json(
                ApiResponse.Ok($"Monitor '{id}' enabled"),
                AppJsonContext.Default.ApiResponse);
        });

        group.MapPost("/disable/{id}", async (string id, IMonitorService monitorService,
            ILogger<IMonitorService> logger) =>
        {
            logger.LogInformation("Disable monitor '{Id}' requested", id);
            await monitorService.DisableMonitorAsync(id);
            return Results.Json(
                ApiResponse.Ok($"Monitor '{id}' disabled"),
                AppJsonContext.Default.ApiResponse);
        });

        group.MapPost("/primary/{id}", async (string id, IMonitorService monitorService,
            ILogger<IMonitorService> logger) =>
        {
            logger.LogInformation("Set primary monitor '{Id}' requested", id);
            await monitorService.SetPrimaryAsync(id);
            return Results.Json(
                ApiResponse.Ok($"Monitor '{id}' set as primary"),
                AppJsonContext.Default.ApiResponse);
        });

        return group;
    }
}
