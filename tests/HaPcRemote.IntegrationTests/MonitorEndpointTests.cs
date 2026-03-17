using HaPcRemote.IntegrationTests.Models;
using Shouldly;

namespace HaPcRemote.IntegrationTests;

[Collection("Service")]
public class MonitorEndpointTests : IntegrationTestBase
{
    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetMonitors_ReturnsList()
    {
        var response = await GetAsync<List<MonitorInfo>>("/api/monitor/list");

        response.Success.ShouldBeTrue();
        response.Data.ShouldNotBeNull();
        response.Data.ShouldNotBeEmpty();

        var first = response.Data.First();
        first.Name.ShouldNotBeNullOrWhiteSpace();
        first.MonitorId.ShouldNotBeNullOrWhiteSpace();
        first.Width.ShouldBeGreaterThan(0);
        first.Height.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetMonitors_HasActiveMonitor()
    {
        var response = await GetAsync<List<MonitorInfo>>("/api/monitor/list");

        response.Data.ShouldNotBeNull();
        response.Data.ShouldContain(m => m.IsActive);
    }

    [Fact]
    [Trait("Category", "ReadOnly")]
    public async Task GetMonitors_HasPrimaryMonitor()
    {
        var response = await GetAsync<List<MonitorInfo>>("/api/monitor/list");

        response.Data.ShouldNotBeNull();
        response.Data.Count(m => m.IsPrimary).ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task EnableMonitor_ValidId_ReturnsOk()
    {
        var monitors = await GetAsync<List<MonitorInfo>>("/api/monitor/list");
        monitors.Data.ShouldNotBeNull();

        var active = monitors.Data.First(m => m.IsActive);
        var encodedId = Uri.EscapeDataString(active.MonitorId);

        var response = await PostAsync($"/api/monitor/enable/{encodedId}");
        response.Success.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task EnableMonitor_InvalidId_Returns404()
    {
        var response = await PostRawAsync("/api/monitor/enable/nonexistent123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetPrimary_ValidId_ReturnsOk()
    {
        var monitors = await GetAsync<List<MonitorInfo>>("/api/monitor/list");
        monitors.Data.ShouldNotBeNull();

        var primary = monitors.Data.First(m => m.IsPrimary);
        var encodedId = Uri.EscapeDataString(primary.MonitorId);

        var response = await PostAsync($"/api/monitor/primary/{encodedId}");
        response.Success.ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "Mutating")]
    public async Task SetPrimary_InvalidId_Returns404()
    {
        var response = await PostRawAsync("/api/monitor/primary/nonexistent123");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
