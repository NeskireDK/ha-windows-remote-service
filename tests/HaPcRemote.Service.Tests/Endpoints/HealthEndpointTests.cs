using System.Net;
using System.Net.Http.Json;
using HaPcRemote.Service.Models;
using Shouldly;

namespace HaPcRemote.Service.Tests.Endpoints;

public class HealthEndpointTests : EndpointTestBase
{
    [Fact]
    public async Task Health_ReturnsOkWithMachineName()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<HealthResponse>>(
            AppJsonContext.Default.ApiResponseHealthResponse);
        json.ShouldNotBeNull();
        json.Success.ShouldBeTrue();
        json.Data.ShouldNotBeNull();
        json.Data.Status.ShouldBe("ok");
        json.Data.MachineName.ShouldNotBeNullOrEmpty();
        json.Data.Version.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Health_SkipsApiKeyAuth()
    {
        using var client = CreateClient(new HaPcRemote.Service.Configuration.PcRemoteOptions
        {
            Auth = new HaPcRemote.Service.Configuration.AuthOptions
            {
                Enabled = true,
                ApiKey = "secret-key"
            }
        });

        // No X-Api-Key header, but health should still work
        var response = await client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
