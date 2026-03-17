using System.Net;
using System.Net.Http.Json;
using FakeItEasy;
using HaPcRemote.Service.Models;
using Shouldly;

namespace HaPcRemote.Service.Tests.Endpoints;

public class MonitorEndpointTests : EndpointTestBase
{
    private static readonly List<MonitorInfo> TwoMonitors =
    [
        new MonitorInfo
        {
            Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", SerialNumber = "ABC123",
            MonitorName = "LG ULTRAGEAR", Width = 3840, Height = 2160,
            DisplayFrequency = 144, IsActive = true, IsPrimary = true
        },
        new MonitorInfo
        {
            Name = @"\\.\DISPLAY2", MonitorId = "DEL4321", SerialNumber = "XYZ789",
            MonitorName = "Dell U2723QE", Width = 2560, Height = 1440,
            DisplayFrequency = 60, IsActive = true, IsPrimary = false
        }
    ];

    [Fact]
    public async Task GetMonitors_ReturnsMonitorList()
    {
        A.CallTo(() => MonitorService.GetMonitorsAsync()).Returns(TwoMonitors);
        using var client = CreateClient();

        var response = await client.GetAsync("/api/monitor/list");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<ApiResponse<List<MonitorInfo>>>(
            AppJsonContext.Default.ApiResponseListMonitorInfo);
        json.ShouldNotBeNull();
        json.Data!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task EnableMonitor_UnknownId_Returns404()
    {
        A.CallTo(() => MonitorService.EnableMonitorAsync("UNKNOWN"))
            .Throws(new KeyNotFoundException("Monitor 'UNKNOWN' not found."));
        using var client = CreateClient();

        var response = await client.PostAsync("/api/monitor/enable/UNKNOWN", null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EnableMonitor_ValidId_ReturnsOk()
    {
        A.CallTo(() => MonitorService.EnableMonitorAsync("DEL4321")).Returns(Task.CompletedTask);
        using var client = CreateClient();

        var response = await client.PostAsync("/api/monitor/enable/DEL4321", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

}
