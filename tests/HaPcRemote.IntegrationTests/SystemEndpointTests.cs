namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class SystemEndpointTests : IntegrationTestBase
{
    [Fact(Skip = "Would put machine to sleep")]
    [Trait("Category", "Mutating")]
    public async Task Sleep_Endpoint_Exists()
    {
        // DO NOT call this — it would put the machine to sleep.
        // This test exists only as documentation that the endpoint is present.
        var response = await PostRawAsync("/api/system/sleep");
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
