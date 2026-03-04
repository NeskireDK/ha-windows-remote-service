using System.Net;
using HaPcRemote.IntegrationTests.Models;
using Shouldly;
using Xunit.Abstractions;

namespace HaPcRemote.IntegrationTests;

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
}
