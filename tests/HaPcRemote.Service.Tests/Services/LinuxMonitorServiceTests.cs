using FakeItEasy;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

#pragma warning disable CA1416 // platform compatibility

public class LinuxMonitorServiceTests
{
    private readonly ICliRunner _cliRunner = A.Fake<ICliRunner>();

    private LinuxMonitorService CreateService() =>
        new(
            _cliRunner,
            A.Fake<ILogger<LinuxMonitorService>>());

    // ── ParseListMonitors tests ──────────────────────────────────────

    [Fact]
    public void ParseListMonitors_DualMonitor_ParsesBoth()
    {
        var output = "Monitors: 2\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n 1: +HDMI-0 2560/597x1440/336+2560+0  HDMI-0\n";

        var result = LinuxMonitorService.ParseListMonitors(output);

        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("DP-0");
        result[1].Name.ShouldBe("HDMI-0");
    }

    [Fact]
    public void ParseListMonitors_PrimaryFlagParsed()
    {
        var output = "Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n";

        var result = LinuxMonitorService.ParseListMonitors(output);

        result[0].IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void ParseListMonitors_EmptyInput_ReturnsEmpty()
    {
        var result = LinuxMonitorService.ParseListMonitors("");

        result.ShouldBeEmpty();
    }

    // ── GetMonitorsAsync tests ───────────────────────────────────────

    [Fact]
    public async Task GetMonitorsAsync_CallsXrandrListMonitors()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n");

        var service = CreateService();
        var result = await service.GetMonitorsAsync();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("DP-0");
    }

    // ── EnableMonitorAsync tests ─────────────────────────────────────

    [Fact]
    public async Task EnableMonitorAsync_CallsXrandrAuto()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n");

        var service = CreateService();
        await service.GetMonitorsAsync(); // prime cache
        await service.EnableMonitorAsync("DP-0");

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("DP-0") && a.Contains("--auto")),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    // ── DisableMonitorAsync tests ────────────────────────────────────

    [Fact]
    public async Task DisableMonitorAsync_CallsXrandrOff()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n");

        var service = CreateService();
        await service.GetMonitorsAsync();
        await service.DisableMonitorAsync("DP-0");

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("DP-0") && a.Contains("--off")),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    // ── Idempotency tests ─────────────────────────────────────────────

    [Fact]
    public async Task EnableMonitorAsync_AlreadyActive_DoesNotCallXrandr()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n");

        var service = CreateService();

        await service.EnableMonitorAsync("DP-0"); // already active

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--auto"), A<int>._))
            .MustNotHaveHappened();
    }

    // ── SoloMonitorAsync tests ───────────────────────────────────────

    [Fact]
    public async Task SoloMonitorAsync_EnablesTargetDisablesOthers()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 2\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n 1: +HDMI-0 1920/530x1080/300+2560+0  HDMI-0\n");

        var service = CreateService();
        await service.GetMonitorsAsync();
        await service.SoloMonitorAsync("DP-0");

        // Target enabled with --auto --primary
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("DP-0") && a.Contains("--auto") && a.Contains("--primary")),
            A<int>._)).MustHaveHappenedOnceExactly();

        // Other disabled with --off
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("HDMI-0") && a.Contains("--off")),
            A<int>._)).MustHaveHappenedOnceExactly();
    }
}
