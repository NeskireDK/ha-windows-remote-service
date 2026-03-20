using System.Net;
using System.Net.Http.Json;
using FakeItEasy;
using HaPcRemote.Service.Models;
using Shouldly;

namespace HaPcRemote.Service.Tests.Endpoints;

public class SystemStateEndpointTests : EndpointTestBase
{
    private static readonly List<AudioDevice> SpeakersDefault =
    [
        new AudioDevice { Name = "Speakers", IsDefault = true, Volume = 50, IsConnected = true }
    ];

    private void SetupDefaults()
    {
        A.CallTo(() => AudioService.GetDevicesAsync()).Returns(SpeakersDefault);
        A.CallTo(() => SteamPlatform.GetRunningAppId()).Returns(0);
        A.CallTo(() => SteamPlatform.GetSteamPath()).Returns((string?)null);
    }

    [Fact]
    public async Task GetState_ReturnsOk()
    {
        SetupDefaults();
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json.ShouldNotBeNull();
        json.Success.ShouldBeTrue();
        json.Data.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetState_AudioAvailable_PopulatesAudioField()
    {
        SetupDefaults();
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Data!.Audio.ShouldNotBeNull();
        json.Data.Audio.Current.ShouldBe("Speakers");
        json.Data.Audio.Volume.ShouldBe(50);
    }

    [Fact]
    public async Task GetState_AudioFails_ReturnsNullAudio()
    {
        A.CallTo(() => AudioService.GetDevicesAsync())
            .Throws(new InvalidOperationException("tool missing"));
        A.CallTo(() => SteamPlatform.GetRunningAppId()).Returns(0);
        A.CallTo(() => SteamPlatform.GetSteamPath()).Returns((string?)null);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Success.ShouldBeTrue();
        json.Data!.Audio.ShouldBeNull();
    }

    [Fact]
    public async Task GetState_NoRunningGame_RunningGameIsNull()
    {
        SetupDefaults();
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Data!.RunningGame.ShouldBeNull();
    }

    [Fact]
    public async Task GetState_SteamNotInstalled_SteamGamesIsNull()
    {
        SetupDefaults();
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Success.ShouldBeTrue();
        json.Data!.SteamGames.ShouldBeNull();
    }

    [Fact]
    public async Task GetState_BothAudioAndSteamFail_ReturnsPartialState()
    {
        A.CallTo(() => AudioService.GetDevicesAsync())
            .Throws(new InvalidOperationException("tool missing"));
        A.CallTo(() => SteamPlatform.GetSteamPath()).Returns((string?)null);
        A.CallTo(() => SteamPlatform.GetRunningAppId()).Returns(0);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Success.ShouldBeTrue();
        json.Data!.Audio.ShouldBeNull();
        json.Data.SteamGames.ShouldBeNull();
    }

    [Fact]
    public async Task GetState_RunningGamePresent_PopulatesRunningGame()
    {
        A.CallTo(() => AudioService.GetDevicesAsync()).Returns(SpeakersDefault);
        A.CallTo(() => SteamPlatform.GetRunningAppId()).Returns(730);
        A.CallTo(() => SteamPlatform.GetSteamPath()).Returns("C:\\FakeNonExistentSteamPath_12345");
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Data!.RunningGame.ShouldNotBeNull();
        json.Data.RunningGame.AppId.ShouldBe(730);
    }

    [Fact]
    public async Task GetState_IdleAvailable_PopulatesIdleSeconds()
    {
        SetupDefaults();
        A.CallTo(() => IdleService.GetIdleSeconds()).Returns(120);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Data!.IdleSeconds.ShouldBe(120);
    }

    [Fact]
    public async Task GetState_IdleFails_IdleSecondsIsNull()
    {
        SetupDefaults();
        A.CallTo(() => IdleService.GetIdleSeconds())
            .Throws(new InvalidOperationException("no idle"));
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Data!.IdleSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task GetState_AlwaysReturnsSuccessTrue()
    {
        A.CallTo(() => AudioService.GetDevicesAsync())
            .Throws(new Exception("all broken"));
        A.CallTo(() => SteamPlatform.GetSteamPath()).Returns((string?)null);
        A.CallTo(() => SteamPlatform.GetRunningAppId()).Throws(new Exception("steam broken"));
        using var client = CreateClient();

        var response = await client.GetAsync("/api/system/state");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<SystemState>>(
            AppJsonContext.Default.ApiResponseSystemState);
        json!.Success.ShouldBeTrue();
    }
}
