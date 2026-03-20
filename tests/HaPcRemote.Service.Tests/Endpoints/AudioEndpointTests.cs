using System.Net;
using System.Net.Http.Json;
using FakeItEasy;
using HaPcRemote.Service.Models;
using Shouldly;

namespace HaPcRemote.Service.Tests.Endpoints;

public class AudioEndpointTests : EndpointTestBase
{
    private static readonly List<AudioDevice> TwoDevices =
    [
        new AudioDevice { Name = "Speakers", IsDefault = true, Volume = 50, IsConnected = true },
        new AudioDevice { Name = "Headphones", IsDefault = false, Volume = 75, IsConnected = true }
    ];

    [Fact]
    public async Task GetDevices_ReturnsDeviceList()
    {
        A.CallTo(() => AudioService.GetDevicesAsync()).Returns(TwoDevices);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/audio/devices");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<List<AudioDevice>>>(
            AppJsonContext.Default.ApiResponseListAudioDevice);
        json.ShouldNotBeNull();
        json.Success.ShouldBeTrue();
        json.Data!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetCurrent_ReturnsDefaultDevice()
    {
        A.CallTo(() => AudioService.GetCurrentDeviceAsync())
            .Returns(TwoDevices[0]);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/audio/current");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<AudioDevice>>(
            AppJsonContext.Default.ApiResponseAudioDevice);
        json.ShouldNotBeNull();
        json.Data!.Name.ShouldBe("Speakers");
    }

    [Fact]
    public async Task GetCurrent_NoDefault_Returns404()
    {
        A.CallTo(() => AudioService.GetCurrentDeviceAsync())
            .Returns((AudioDevice?)null);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/audio/current");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetVolume_ValidRange_ReturnsOk()
    {
        A.CallTo(() => AudioService.SetVolumeAsync(50)).Returns(Task.CompletedTask);
        using var client = CreateClient();

        var response = await client.PostAsync("/api/audio/volume/50", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetVolume_OutOfRange_Returns400(int level)
    {
        using var client = CreateClient();

        var response = await client.PostAsync($"/api/audio/volume/{level}", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse>(
            AppJsonContext.Default.ApiResponse);
        json.ShouldNotBeNull();
        json.Success.ShouldBeFalse();
        json.Message!.ShouldContain("between 0 and 100");
    }

    [Fact]
    public async Task SetDefault_ReturnsOk()
    {
        A.CallTo(() => AudioService.SetDefaultDeviceAsync("Headphones")).Returns(Task.CompletedTask);
        using var client = CreateClient();

        var response = await client.PostAsync("/api/audio/set/Headphones", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetDefault_InvalidDevice_Returns404()
    {
        A.CallTo(() => AudioService.SetDefaultDeviceAsync("NonExistent"))
            .Throws(new KeyNotFoundException("Audio device 'NonExistent' not found."));
        using var client = CreateClient();

        var response = await client.PostAsync("/api/audio/set/NonExistent", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse>(
            AppJsonContext.Default.ApiResponse);
        json.ShouldNotBeNull();
        json.Success.ShouldBeFalse();
        json.Message!.ShouldContain("not found");
    }
}
