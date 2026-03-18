using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
[Trait("Category", "ReadOnly")]
public class HealthEndpointTests : IntegrationTestBase
{
    [Fact]
    public async Task Health_ReturnsOk_WithMachineInfo()
    {
        var response = await GetAsync<HealthResponse>("/api/health");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        response.Data.MachineName.ShouldNotBeNullOrWhiteSpace();
        response.Data.Version.ShouldNotBeNullOrEmpty();
        response.Data.MacAddresses.ShouldNotBeNull();
        response.Data.MacAddresses.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Health_NoApiKey_StillWorks()
    {
        using var noAuthClient = CreateClientWithoutApiKey();
        var response = await GetRawAsync("/api/health", noAuthClient);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var body = await DeserializeAsync<ApiResponse<HealthResponse>>(response);
        body.ShouldNotBeNull();
        body.Success.ShouldBeTrue();
        body.Data.ShouldNotBeNull();
        body.Data.MachineName.ShouldNotBeNullOrWhiteSpace();
    }
}
