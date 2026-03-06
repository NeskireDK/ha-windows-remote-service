using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class AudioEndpointTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetDevices_ReturnsDeviceList()
    {
        var response = await GetAsync<List<AudioDevice>>("/api/audio/devices");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        response.Data.ShouldNotBeEmpty();

        var first = response.Data.First();
        first.Name.ShouldNotBeNullOrWhiteSpace();
        first.Volume.ShouldBeInRange(0, 100);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetDevices_HasDefaultDevice()
    {
        var response = await GetAsync<List<AudioDevice>>("/api/audio/devices");

        response.Data.ShouldNotBeNull();
        response.Data.ShouldContain(d => d.IsDefault);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetCurrent_ReturnsDefaultDevice()
    {
        var response = await GetAsync<AudioDevice>("/api/audio/current");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        response.Data.Name.ShouldNotBeNullOrWhiteSpace();
        response.Data.Volume.ShouldBeInRange(0, 100);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetVolume_ValidLevel_ReturnsOk()
    {
        var response = await PostAsync("/api/audio/volume/50");

        response.Success.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetVolume_TooHigh_Returns400()
    {
        var response = await PostRawAsync("/api/audio/volume/101");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetVolume_TooLow_Returns400()
    {
        var response = await PostRawAsync("/api/audio/volume/-1");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetVolume_PreservesLevel()
    {
        // Set volume to 42
        var setResponse = await PostAsync("/api/audio/volume/42");
        setResponse.Success.ShouldBeTrue();

        // Read it back
        var current = await GetAsync<AudioDevice>("/api/audio/current");
        current.Data.ShouldNotBeNull();
        current.Data.Volume.ShouldBe(42);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetDefaultDevice_ValidDevice_ReturnsOk()
    {
        // Get list of devices, pick one
        var devices = await GetAsync<List<AudioDevice>>("/api/audio/devices");
        devices.Data.ShouldNotBeNull();
        devices.Data.ShouldNotBeEmpty();

        var device = devices.Data.First();
        var encodedName = Uri.EscapeDataString(device.Name);

        var response = await PostAsync($"/api/audio/set/{encodedName}");
        response.Success.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetDefaultDevice_InvalidDevice_Returns404()
    {
        var response = await PostRawAsync("/api/audio/set/NonexistentDevice123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
