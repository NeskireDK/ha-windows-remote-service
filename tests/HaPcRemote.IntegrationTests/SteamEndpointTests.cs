using System.Net;
using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class SteamEndpointTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_Returns200Or500()
    {
        // 200 when Steam is installed, 500 when it is not
        var response = await GetRawAsync("/api/steam/games");

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.InternalServerError);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_WhenSuccessful_ReturnsValidList()
    {
        var response = await GetRawAsync("/api/steam/games");
        if (response.StatusCode != HttpStatusCode.OK)
            return; // Steam not installed on test machine — skip

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldNotBeNull();

        foreach (var game in result.Data)
        {
            game.AppId.ShouldNotBe(0);
            game.Name.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetGames_WhenSuccessful_ReturnedListIsOrderedByLastPlayedDescending()
    {
        var response = await GetRawAsync("/api/steam/games");
        if (response.StatusCode != HttpStatusCode.OK)
            return;

        var result = await DeserializeAsync<ApiResponse<List<SteamGame>>>(response);
        result.ShouldNotBeNull();
        result.Data.ShouldNotBeNull();

        var steamGames = result.Data.Where(g => !g.IsShortcut).ToList();
        if (steamGames.Count < 2)
            return;

        var lastPlayed = steamGames.Select(g => g.LastPlayed).ToList();
        lastPlayed.ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetRunning_ReturnsSuccess()
    {
        var response = await GetRawAsync("/api/steam/running");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await DeserializeAsync<ApiResponse<SteamRunningGame>>(response);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        // Data may be null when no game is running
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetRunning_WhenGameRunning_HasValidFields()
    {
        var response = await GetRawAsync("/api/steam/running");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await DeserializeAsync<ApiResponse<SteamRunningGame>>(response);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();

        if (result.Data == null)
            return; // No game running — nothing to validate

        result.Data.AppId.ShouldBeGreaterThan(0);
        result.Data.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task Stop_WhenNoGameRunning_ReturnsOk()
    {
        var running = await GetRawAsync("/api/steam/running");
        running.StatusCode.ShouldBe(HttpStatusCode.OK);
        var runningResult = await DeserializeAsync<ApiResponse<SteamRunningGame>>(running);
        if (runningResult?.Data != null)
            return; // Skip — do not kill a running game

        var response = await PostRawAsync("/api/steam/stop");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await DeserializeAsync<ApiResponse>(response);
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }
}
