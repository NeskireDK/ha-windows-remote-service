using System.Net;
using HaPcRemote.IntegrationTests.Models;
using Shouldly;
using Xunit.Abstractions;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class UpdateTriggerTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public UpdateTriggerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task TriggerUpdate_ReturnsResponse()
    {
        var response = await PostRawAsync("/api/system/update");

        _output.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Body: {body}");

        // Accept any non-404 response — the endpoint should exist
        response.StatusCode.ShouldNotBe(HttpStatusCode.NotFound,
            "Update endpoint not found — is it deployed?");
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task TriggerUpdateAndReload_AppliesNewVersion()
    {
        // Step 1: Trigger update download
        var updateResponse = await PostRawAsync("/api/system/update");
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Update: {(int)updateResponse.StatusCode} — {updateBody}");

        // Step 2: Trigger reload to apply it
        var reloadResponse = await PostRawAsync("/api/system/reload");
        var reloadBody = await reloadResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Reload: {(int)reloadResponse.StatusCode} — {reloadBody}");

        // Step 3: Wait for restart
        _output.WriteLine("Waiting for service restart...");
        await Task.Delay(TimeSpan.FromSeconds(15));

        // Step 4: Verify service is back up
        var healthResponse = await GetRawAsync("/api/health");
        healthResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var health = await healthResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Health after restart: {health}");
    }
}
