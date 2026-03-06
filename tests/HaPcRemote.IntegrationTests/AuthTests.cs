using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
[Trait("Category", "ReadOnly")]
public class AuthTests : IntegrationTestBase
{
    [Fact]
    public async Task ProtectedEndpoint_NoApiKey_Returns401()
    {
        using var noAuthClient = CreateClientWithoutApiKey();
        var response = await GetRawAsync("/api/audio/devices", noAuthClient);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WrongApiKey_Returns401()
    {
        using var badKeyClient = CreateClientWithApiKey("totally-wrong-api-key");
        var response = await GetRawAsync("/api/audio/devices", badKeyClient);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_ValidApiKey_Returns200()
    {
        var response = await GetRawAsync("/api/audio/devices");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
