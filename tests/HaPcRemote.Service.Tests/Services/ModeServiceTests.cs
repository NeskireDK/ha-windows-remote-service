using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class ModeServiceTests
{
    private readonly IAudioService _audioService = A.Fake<IAudioService>();
    private readonly IMonitorService _monitorService = A.Fake<IMonitorService>();
    private readonly IAppService _appService = A.Fake<IAppService>();

    private ModeService CreateService(Dictionary<string, ModeConfig>? modes = null)
    {
        var options = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new PcRemoteOptions
        {
            Modes = modes ?? new Dictionary<string, ModeConfig>()
        });
        return new ModeService(options, _audioService, _monitorService, _appService, A.Fake<ILogger<ModeService>>());
    }

    // ── GetModeNames ─────────────────────────────────────────────────

    [Fact]
    public void GetModeNames_ReturnsConfiguredModes()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["desktop"] = new(),
            ["couch"] = new()
        });

        var names = service.GetModeNames();

        names.Count.ShouldBe(2);
        names.ShouldContain("desktop");
        names.ShouldContain("couch");
    }

    [Fact]
    public void GetModeNames_NoModes_ReturnsEmpty()
    {
        var service = CreateService();

        service.GetModeNames().ShouldBeEmpty();
    }

    // ── ApplyModeAsync — unknown mode ────────────────────────────────

    [Fact]
    public async Task ApplyModeAsync_UnknownMode_ThrowsKeyNotFoundException()
    {
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.ApplyModeAsync("nonexistent"));
    }

    // ── ApplyModeAsync — full config ─────────────────────────────────

    [Fact]
    public async Task ApplyModeAsync_AllPropertiesSet_CallsAllServices()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["gaming"] = new()
            {
                AudioDevice = "Speakers",
                Volume = 80,
                KillApp = "discord",
                LaunchApp = "steam-bigpicture"
            }
        });

        await service.ApplyModeAsync("gaming");

        A.CallTo(() => _audioService.SetDefaultDeviceAsync("Speakers"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _audioService.SetVolumeAsync(80))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _appService.KillAsync("discord"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _appService.LaunchAsync("steam-bigpicture"))
            .MustHaveHappenedOnceExactly();
    }

    // ── ApplyModeAsync — partial configs ─────────────────────────────

    [Fact]
    public async Task ApplyModeAsync_OnlyAudioDevice_SetsAudioOnly()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["audio-only"] = new() { AudioDevice = "Headphones" }
        });

        await service.ApplyModeAsync("audio-only");

        A.CallTo(() => _audioService.SetDefaultDeviceAsync("Headphones"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _monitorService.SoloMonitorAsync(A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _audioService.SetVolumeAsync(A<int>._))
            .MustNotHaveHappened();
        A.CallTo(() => _appService.KillAsync(A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _appService.LaunchAsync(A<string>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ApplyModeAsync_OnlyVolume_SetsVolumeOnly()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["quiet"] = new() { Volume = 25 }
        });

        await service.ApplyModeAsync("quiet");

        A.CallTo(() => _audioService.SetVolumeAsync(25))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _audioService.SetDefaultDeviceAsync(A<string>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ApplyModeAsync_OnlyKillApp_KillsOnly()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["kill-only"] = new() { KillApp = "discord" }
        });

        await service.ApplyModeAsync("kill-only");

        A.CallTo(() => _appService.KillAsync("discord"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _appService.LaunchAsync(A<string>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ApplyModeAsync_OnlyLaunchApp_LaunchesOnly()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["launch-only"] = new() { LaunchApp = "steam-bigpicture" }
        });

        await service.ApplyModeAsync("launch-only");

        A.CallTo(() => _appService.LaunchAsync("steam-bigpicture"))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _appService.KillAsync(A<string>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task ApplyModeAsync_EmptyConfig_CallsNothing()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["empty"] = new()
        });

        await service.ApplyModeAsync("empty");

        A.CallTo(() => _audioService.SetDefaultDeviceAsync(A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _monitorService.SoloMonitorAsync(A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _audioService.SetVolumeAsync(A<int>._))
            .MustNotHaveHappened();
        A.CallTo(() => _appService.KillAsync(A<string>._))
            .MustNotHaveHappened();
        A.CallTo(() => _appService.LaunchAsync(A<string>._))
            .MustNotHaveHappened();
    }

    // ── ApplyModeAsync — ordering ────────────────────────────────────

    [Fact]
    public async Task ApplyModeAsync_KillBeforeLaunch()
    {
        var callOrder = new List<string>();

        A.CallTo(() => _appService.KillAsync(A<string>._))
            .Invokes(() => callOrder.Add("kill"));
        A.CallTo(() => _appService.LaunchAsync(A<string>._))
            .Invokes(() => callOrder.Add("launch"));

        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["switch"] = new() { KillApp = "old-app", LaunchApp = "new-app" }
        });

        await service.ApplyModeAsync("switch");

        callOrder.ShouldBe(new[] { "kill", "launch" });
    }

    [Fact]
    public async Task ApplyModeAsync_AudioBeforeSoloBeforeVolume()
    {
        var callOrder = new List<string>();

        A.CallTo(() => _audioService.SetDefaultDeviceAsync(A<string>._))
            .Invokes(() => callOrder.Add("audio"));
        A.CallTo(() => _monitorService.SoloMonitorAsync(A<string>._))
            .Invokes(() => callOrder.Add("monitor"));
        A.CallTo(() => _audioService.SetVolumeAsync(A<int>._))
            .Invokes(() => callOrder.Add("volume"));

        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["full"] = new()
            {
                AudioDevice = "Speakers",
                SoloMonitor = "GSM59A4",
                Volume = 50
            }
        });

        await service.ApplyModeAsync("full");

        callOrder.ShouldBe(new[] { "audio", "monitor", "volume" });
    }

    // ── ApplyModeAsync — volume edge values ──────────────────────────

    [Fact]
    public async Task ApplyModeAsync_VolumeZero_SetsZero()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["mute"] = new() { Volume = 0 }
        });

        await service.ApplyModeAsync("mute");

        A.CallTo(() => _audioService.SetVolumeAsync(0))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ApplyModeAsync_Volume100_Sets100()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["loud"] = new() { Volume = 100 }
        });

        await service.ApplyModeAsync("loud");

        A.CallTo(() => _audioService.SetVolumeAsync(100))
            .MustHaveHappenedOnceExactly();
    }

    // ── ApplyModeAsync — SoloMonitor ──────────────────────────────────

    [Fact]
    public async Task ApplyModeAsync_SoloMonitorSet_CallsSoloMonitorAsync()
    {
        var service = CreateService(new Dictionary<string, ModeConfig>
        {
            ["solo"] = new() { SoloMonitor = "GSM59A4" }
        });

        await service.ApplyModeAsync("solo");

        A.CallTo(() => _monitorService.SoloMonitorAsync("GSM59A4"))
            .MustHaveHappenedOnceExactly();
    }

}
