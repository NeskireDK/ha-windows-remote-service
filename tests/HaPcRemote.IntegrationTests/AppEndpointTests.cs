using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class AppEndpointTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetStatuses_ReturnsList()
    {
        var response = await GetAsync<List<AppInfo>>("/api/app/status");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        // May be empty if no apps are configured
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetStatus_InvalidApp_Returns404()
    {
        var response = await GetRawAsync("/api/app/status/nonexistent123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task LaunchApp_InvalidApp_Returns404()
    {
        var response = await PostRawAsync("/api/app/launch/nonexistent123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task KillApp_InvalidApp_Returns404()
    {
        var response = await PostRawAsync("/api/app/kill/nonexistent123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetStatus_ValidApp_ReturnsAppInfo()
    {
        var all = await GetAsync<List<AppInfo>>("/api/app/status");
        all.Data.ShouldNotBeNull();

        if (all.Data.Count == 0)
        {
            // No apps configured — skip gracefully
            return;
        }

        var firstKey = all.Data.First().Key;
        var response = await GetAsync<AppInfo>($"/api/app/status/{firstKey}");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        response.Data.Key.ShouldBe(firstKey);
        response.Data.DisplayName.ShouldNotBeNullOrWhiteSpace();
    }
}
