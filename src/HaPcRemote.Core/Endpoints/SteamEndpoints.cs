using HaPcRemote.Service.Configuration;
using Microsoft.AspNetCore.StaticFiles;
using HaPcRemote.Service.Middleware;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;

namespace HaPcRemote.Service.Endpoints;

public static class SteamEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static RouteGroupBuilder MapSteamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/steam");
        group.AddEndpointFilter<EndpointExceptionFilter>();

        group.MapGet("/games", async (ISteamService steamService) =>
        {
            var games = await steamService.GetGamesAsync();
            return Results.Json(
                ApiResponse.Ok<List<SteamGame>>(games),
                AppJsonContext.Default.ApiResponseListSteamGame);
        });

        group.MapGet("/running", async (ISteamService steamService) =>
        {
            var running = await steamService.GetRunningGameAsync();
            return Results.Json(
                ApiResponse.Ok(running),
                AppJsonContext.Default.ApiResponseSteamRunningGame);
        });

        group.MapPost("/run/{appId:int}", async (int appId, ISteamService steamService,
            ILogger<SteamService> logger) =>
        {
            logger.LogInformation("Launch Steam game requested: {AppId}", appId);
            var result = await steamService.LaunchGameAsync(appId);
            return Results.Json(
                ApiResponse.Ok(result),
                AppJsonContext.Default.ApiResponseSteamRunningGame);
        });

        group.MapPost("/stop", async (ISteamService steamService, ILogger<SteamService> logger) =>
        {
            logger.LogInformation("Stop Steam game requested");
            await steamService.StopGameAsync();
            return Results.Json(
                ApiResponse.Ok("Steam game stopped"),
                AppJsonContext.Default.ApiResponse);
        });

        group.MapGet("/artwork/{appId:int}", async (int appId, ISteamService steamService,
            ILogger<SteamService> logger) =>
        {
            logger.LogDebug("Artwork request: appId={AppId}", appId);
            var path = await steamService.GetArtworkPathAsync(appId);
            logger.LogDebug("Artwork resolved path: {Path}", path ?? "(null)");
            logger.LogDebug("Artwork file exists: {Exists}", path != null && File.Exists(path));

            if (path == null)
                return Results.NotFound();

            if (!ContentTypeProvider.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";

            logger.LogDebug("Artwork content-type: {ContentType}", contentType);
            return Results.File(path, contentType);
        });

        group.MapGet("/artwork/diagnostics", async (ISteamService steamService) =>
        {
            var diagnostics = await steamService.GetAllArtworkDiagnosticsAsync();
            return Results.Json(
                ApiResponse.Ok(diagnostics),
                AppJsonContext.Default.ApiResponseListArtworkDiagnostics);
        });

        group.MapGet("/artwork/{appId:int}/diagnostics", async (int appId, ISteamService steamService) =>
        {
            var diag = await steamService.GetArtworkDiagnosticsAsync(appId);
            if (diag == null)
                return Results.Json(
                    ApiResponse.Fail("Steam not available"),
                    AppJsonContext.Default.ApiResponse,
                    statusCode: 503);

            return Results.Json(
                ApiResponse.Ok(diag),
                AppJsonContext.Default.ApiResponseArtworkDiagnostics);
        });

        group.MapGet("/bindings", (ISteamService steamService) =>
        {
            var bindings = steamService.GetBindings();
            return Results.Json(
                ApiResponse.Ok(bindings),
                AppJsonContext.Default.ApiResponseSteamBindings);
        });

        group.MapPut("/bindings", async (SteamBindings bindings, IConfigurationWriter configWriter) =>
        {
            var steamConfig = new SteamConfig
            {
                DefaultPcMode = bindings.DefaultPcMode,
                GamePcModeBindings = bindings.GamePcModeBindings
            };
            await Task.Run(() => configWriter.SaveSteamBindings(steamConfig));
            return Results.Json(
                ApiResponse.Ok("Steam bindings saved"),
                AppJsonContext.Default.ApiResponse);
        });

        return group;
    }
}
