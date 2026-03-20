using System.ComponentModel;
using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Native;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Tests.Services;

public class WindowsMonitorServiceTests
{
    private readonly IDisplayConfigApi _api = A.Fake<IDisplayConfigApi>();
    private readonly ILogger<WindowsMonitorService> _logger = A.Fake<ILogger<WindowsMonitorService>>();

    private static IOptionsMonitor<PcRemoteOptions> MakeOptions(
        DisplaySwitchingMode mode = DisplaySwitchingMode.Direct,
        bool useSavedLayout = true)
    {
        var options = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => options.CurrentValue).Returns(new PcRemoteOptions
        {
            DisplaySwitching = mode,
            UseSavedLayout = useSavedLayout
        });
        return options;
    }

    private WindowsMonitorService CreateService() => new(_api, _logger, MakeOptions());

    private WindowsMonitorService CreateCompatibleService()
    {
        var service = new WindowsMonitorService(_api, _logger, MakeOptions(DisplaySwitchingMode.Compatible));
        service.RetryDelaysMs = [0, 0, 0];
        service.StepDelayMs = 0;
        return service;
    }

    // ── Test data builders ────────────────────────────────────────────

    private static LUID Adapter1 => new() { LowPart = 1, HighPart = 0 };
    private static LUID Adapter2 => new() { LowPart = 2, HighPart = 0 };

    private static DISPLAYCONFIG_PATH_INFO MakeActivePath(LUID adapter, uint targetId, uint sourceId, uint sourceModeIdx, uint targetModeIdx) =>
        new()
        {
            sourceInfo = new() { adapterId = adapter, id = sourceId, modeInfoIdx = sourceModeIdx },
            targetInfo = new()
            {
                adapterId = adapter, id = targetId, modeInfoIdx = targetModeIdx,
                targetAvailable = 1,
                refreshRate = new() { Numerator = 120000, Denominator = 1000 }
            },
            flags = DISPLAYCONFIG_PATH_FLAGS.ACTIVE
        };

    private static DISPLAYCONFIG_PATH_INFO MakeInactivePath(LUID adapter, uint targetId, uint sourceId) =>
        new()
        {
            sourceInfo = new() { adapterId = adapter, id = sourceId, modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID },
            targetInfo = new()
            {
                adapterId = adapter, id = targetId, modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID,
                targetAvailable = 1,
                refreshRate = new() { Numerator = 60000, Denominator = 1000 }
            },
            flags = DISPLAYCONFIG_PATH_FLAGS.NONE
        };

    private static DISPLAYCONFIG_MODE_INFO MakeSourceMode(LUID adapter, uint id, uint w, uint h, int x, int y) =>
        new()
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.SOURCE,
            id = id,
            adapterId = adapter,
            info = new() { sourceMode = new() { width = w, height = h, position = new() { x = x, y = y } } }
        };

    private static DISPLAYCONFIG_MODE_INFO MakeTargetMode(LUID adapter, uint id, uint hzNumerator, uint hzDenominator) =>
        new()
        {
            infoType = DISPLAYCONFIG_MODE_INFO_TYPE.TARGET,
            id = id,
            adapterId = adapter,
            info = new()
            {
                targetMode = new()
                {
                    targetVideoSignalInfo = new()
                    {
                        vSyncFreq = new() { Numerator = hzNumerator, Denominator = hzDenominator }
                    }
                }
            }
        };

    /// <summary>
    /// Sets up the fake API to return a two-monitor config:
    /// Monitor 0: GSM59A4 "LG ULTRAGEAR" on \\.\DISPLAY1, 3840x2160@144Hz, primary, active
    /// Monitor 1: DEL4321 "Dell U2723QE" on \\.\DISPLAY2, 2560x1440@60Hz, not primary, active
    /// </summary>
    private void SetupTwoMonitorConfig()
    {
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
        };

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),        // DISPLAY1 source — primary (0,0)
            MakeTargetMode(Adapter1, 10, 144000, 1000),            // DISPLAY1 target — 144Hz
            MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),     // DISPLAY2 source — offset right
            MakeTargetMode(Adapter1, 20, 60000, 1000),             // DISPLAY2 target — 60Hz
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));

        // GSM: G=7, S=19, M=13 → native 0x1E6D → big-endian wire 0x6D1E
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));

        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");
    }

    /// <summary>
    /// Two monitors: one active (primary), one inactive.
    /// </summary>
    private void SetupOneActiveOneInactive()
    {
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
        };

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 120000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
    }

    // ── GetMonitorsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetMonitorsAsync_ReturnsBothMonitors()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetMonitorsAsync_ParsesGdiName()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].Name.ShouldBe(@"\\.\DISPLAY1");
        monitors[1].Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public async Task GetMonitorsAsync_ParsesMonitorId()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].MonitorId.ShouldBe("GSM59A4");
        monitors[1].MonitorId.ShouldBe("DEL4321");
    }

    [Fact]
    public async Task GetMonitorsAsync_ParsesFriendlyName()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].MonitorName.ShouldBe("LG ULTRAGEAR");
        monitors[1].MonitorName.ShouldBe("Dell U2723QE");
    }

    [Fact]
    public async Task GetMonitorsAsync_ParsesResolution()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].Width.ShouldBe(3840);
        monitors[0].Height.ShouldBe(2160);
        monitors[1].Width.ShouldBe(2560);
        monitors[1].Height.ShouldBe(1440);
    }

    [Fact]
    public async Task GetMonitorsAsync_ParsesRefreshRate()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].DisplayFrequency.ShouldBe(144);
        monitors[1].DisplayFrequency.ShouldBe(60);
    }

    [Fact]
    public async Task GetMonitorsAsync_IdentifiesPrimary()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].IsPrimary.ShouldBeTrue();
        monitors[1].IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task GetMonitorsAsync_IdentifiesActiveStatus()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[0].IsActive.ShouldBeTrue();
        monitors[1].IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetMonitorsAsync_InactiveMonitor_HasEmptyGdiName()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors[1].Name.ShouldBe("");
    }

    [Fact]
    public async Task GetMonitorsAsync_InactiveMonitor_HasFallbackRefreshRate()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        // Falls back to path targetInfo.refreshRate
        monitors[1].DisplayFrequency.ShouldBe(60);
    }

    [Fact]
    public async Task GetMonitorsAsync_CachesResult()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.GetMonitorsAsync();
        await service.GetMonitorsAsync();

        // Each QueryMonitors call issues QDC_ALL_PATHS + QDC_DATABASE_CURRENT; cache means only one QueryMonitors call
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetMonitorsAsync_AfterInvalidateCache_QueriesAgain()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.GetMonitorsAsync();
        service.InvalidateCache();
        await service.GetMonitorsAsync();

        // Two QueryMonitors calls → two QDC_ALL_PATHS calls
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task GetMonitorsAsync_SkipsUnavailableTargets()
    {
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
        };
        // Make target unavailable
        paths[0].targetInfo.targetAvailable = 0;

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 1920, 1080, 0, 0),
            MakeTargetMode(Adapter1, 10, 60000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMonitorsAsync_DeduplicatesByAdapterAndTargetId()
    {
        // Two paths for the same target (one active, one inactive)
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeInactivePath(Adapter1, targetId: 10, sourceId: 1), // duplicate target
        };
        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 1920, 1080, 0, 0),
            MakeTargetMode(Adapter1, 10, 60000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");

        var service = CreateService();
        var monitors = await service.GetMonitorsAsync();

        monitors.Count.ShouldBe(1);
    }

    // ── EnableMonitorAsync ────────────────────────────────────────────

    [Fact]
    public async Task EnableMonitorAsync_SetsActiveFlag()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.EnableMonitorAsync("DEL4321");

        appliedPaths.ShouldNotBeNull();
        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        (targetPath.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE).ShouldBe(DISPLAYCONFIG_PATH_FLAGS.ACTIVE);
    }

    [Fact]
    public async Task EnableMonitorAsync_InvalidatesModeIndexes()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.EnableMonitorAsync("DEL4321");

        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        targetPath.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        targetPath.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    [Fact]
    public async Task EnableMonitorAsync_AlreadyActive_DoesNotCallApply()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.EnableMonitorAsync("GSM59A4"); // already active

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task EnableMonitorAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(() => service.EnableMonitorAsync("UNKNOWN"));
    }

    // ── DisableMonitorAsync ───────────────────────────────────────────

    [Fact]
    public async Task DisableMonitorAsync_ClearsActiveFlag()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.DisableMonitorAsync("DEL4321");

        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        (targetPath.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE).ShouldBe(DISPLAYCONFIG_PATH_FLAGS.NONE);
    }

    [Fact]
    public async Task DisableMonitorAsync_AlreadyInactive_DoesNotCallApply()
    {
        SetupOneActiveOneInactive();
        var service = CreateService();

        await service.DisableMonitorAsync("DEL4321"); // already inactive

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task DisableMonitorAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(() => service.DisableMonitorAsync("UNKNOWN"));
    }

    // ── SetPrimaryAsync ───────────────────────────────────────────────

    [Fact]
    public async Task SetPrimaryAsync_MovesTargetToOrigin()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        DISPLAYCONFIG_MODE_INFO[]? appliedModes = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] _, DISPLAYCONFIG_MODE_INFO[] m, SetDisplayConfigFlags _) => appliedModes = m);

        await service.SetPrimaryAsync("DEL4321");

        appliedModes.ShouldNotBeNull();
        // DISPLAY2 source mode (index 2) should now be at (0,0)
        appliedModes![2].info.sourceMode.position.x.ShouldBe(0);
        appliedModes![2].info.sourceMode.position.y.ShouldBe(0);
    }

    [Fact]
    public async Task SetPrimaryAsync_OffsetsOtherMonitors()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        DISPLAYCONFIG_MODE_INFO[]? appliedModes = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] _, DISPLAYCONFIG_MODE_INFO[] m, SetDisplayConfigFlags _) => appliedModes = m);

        await service.SetPrimaryAsync("DEL4321");

        // DISPLAY1 was at (0,0), DISPLAY2 was at (3840,0)
        // After making DISPLAY2 primary: offset by (-3840, 0)
        // DISPLAY1 → (-3840, 0), DISPLAY2 → (0, 0)
        appliedModes![0].info.sourceMode.position.x.ShouldBe(-3840);
        appliedModes![0].info.sourceMode.position.y.ShouldBe(0);
    }

    [Fact]
    public async Task SetPrimaryAsync_AlreadyPrimary_DoesNotCallApply()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.SetPrimaryAsync("GSM59A4"); // already at (0,0)

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task SetPrimaryAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(() => service.SetPrimaryAsync("UNKNOWN"));
    }

    // ── SoloMonitorAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SoloMonitorAsync_ActivatesTargetAndDeactivatesOthers()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        appliedPaths.ShouldNotBeNull();
        var target = appliedPaths!.First(p => p.targetInfo.id == 20);
        var other = appliedPaths!.First(p => p.targetInfo.id == 10);
        (target.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE).ShouldBe(DISPLAYCONFIG_PATH_FLAGS.ACTIVE);
        (other.flags & DISPLAYCONFIG_PATH_FLAGS.ACTIVE).ShouldBe(DISPLAYCONFIG_PATH_FLAGS.NONE);
    }

    [Fact]
    public async Task SoloMonitorAsync_SingleAtomicApplyCall()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.SoloMonitorAsync("GSM59A4");

        // Should only call ApplyConfig once (atomic operation)
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SoloMonitorAsync_InvalidatesModeIndexesOnDeactivatedPaths()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        var deactivated = appliedPaths!.First(p => p.targetInfo.id == 10);
        deactivated.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        deactivated.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    [Fact]
    public async Task SoloMonitorAsync_UseSavedLayoutFalse_InvalidatesModeIndexesOnTargetPath()
    {
        SetupTwoMonitorConfig();
        var service = CreateServiceWithSavedLayout(false);

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        var target = appliedPaths!.First(p => p.targetInfo.id == 20);
        target.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        target.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    [Fact]
    public async Task SoloMonitorAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(() => service.SoloMonitorAsync("UNKNOWN"));
    }

    // ── FormatEdidId ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0x6D1E, 0x59A4, "GSM59A4")] // LG
    [InlineData(0xAC10, 0x4321, "DEL4321")] // Dell
    public void FormatEdidId_DecodesCorrectly(ushort mfgId, ushort productId, string expected)
    {
        var result = WindowsMonitorService.FormatEdidId(mfgId, productId);

        result.ShouldBe(expected);
    }

    // ── EDID collision (identical monitors) ─────────────────────────────

    private void SetupIdenticalMonitors()
    {
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
        };

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 2560, 1440, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
            MakeSourceMode(Adapter1, 1, 2560, 1440, 2560, 0),
            MakeTargetMode(Adapter1, 20, 144000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));

        // Same EDID for both — identical manufacturer + product code
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));

        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");
    }

    [Fact]
    public void QueryMonitors_IdenticalEdid_AssignsUniqueMonitorIds()
    {
        SetupIdenticalMonitors();
        var service = CreateService();

        var monitors = service.QueryMonitors();

        monitors.Count.ShouldBe(2);
        monitors[0].MonitorId.ShouldBe("GSM59A4");
        monitors[1].MonitorId.ShouldBe("GSM59A4#2");
    }

    [Fact]
    public void QueryMonitors_IdenticalEdid_BothResolvableById()
    {
        SetupIdenticalMonitors();
        var service = CreateService();

        var monitors = service.QueryMonitors();

        WindowsMonitorService.FindMonitor(monitors, "GSM59A4").Name.ShouldBe(@"\\.\DISPLAY1");
        WindowsMonitorService.FindMonitor(monitors, "GSM59A4#2").Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void QueryMonitors_IdenticalEdid_BothResolvableByGdiName()
    {
        SetupIdenticalMonitors();
        var service = CreateService();

        var monitors = service.QueryMonitors();

        WindowsMonitorService.FindMonitor(monitors, @"\\.\DISPLAY1").MonitorId.ShouldBe("GSM59A4");
        WindowsMonitorService.FindMonitor(monitors, @"\\.\DISPLAY2").MonitorId.ShouldBe("GSM59A4#2");
    }

    [Fact]
    public void QueryMonitors_IdenticalEdid_ActiveReplacesInactive_KeepsBaseId()
    {
        // Inactive path comes first, then active path for the same target
        var paths = new[]
        {
            MakeInactivePath(Adapter1, targetId: 10, sourceId: 0),
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
        };

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 2560, 1440, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");

        var service = CreateService();

        var monitors = service.QueryMonitors();

        monitors.Count.ShouldBe(1);
        monitors[0].MonitorId.ShouldBe("GSM59A4"); // Not GSM59A4#2
        monitors[0].IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task EnableMonitorAsync_IdenticalEdid_SecondMonitor_ResolvesCorrectTarget()
    {
        // Setup identical monitors but second one inactive
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
        };

        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 2560, 1440, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
        };

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).Returns((paths, modes));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");

        var service = CreateService();

        await service.EnableMonitorAsync("GSM59A4#2");

        // Should have called ApplyConfig — verifying it didn't throw
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── Retry resilience ────────────────────────────────────────────

    private WindowsMonitorService CreateServiceWithNoRetryDelay()
    {
        var service = new WindowsMonitorService(_api, _logger, MakeOptions());
        service.RetryDelaysMs = [0, 0, 0];
        return service;
    }

    [Fact]
    public async Task EnableMonitorAsync_Error31_RetriesAndSucceeds()
    {
        SetupOneActiveOneInactive();
        var service = CreateServiceWithNoRetryDelay();

        var callCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Win32Exception(ERROR_GEN_FAILURE);
            });

        await service.EnableMonitorAsync("DEL4321");

        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task DisableMonitorAsync_Error31_RetriesAndSucceeds()
    {
        SetupTwoMonitorConfig();
        var service = CreateServiceWithNoRetryDelay();

        var callCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Win32Exception(ERROR_GEN_FAILURE);
            });

        await service.DisableMonitorAsync("DEL4321");

        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SetPrimaryAsync_Error31_RetriesAndSucceeds()
    {
        SetupTwoMonitorConfig();
        var service = CreateServiceWithNoRetryDelay();

        var callCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Win32Exception(ERROR_GEN_FAILURE);
            });

        await service.SetPrimaryAsync("DEL4321");

        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SoloMonitorAsync_Error31_RetriesAndSucceeds()
    {
        SetupTwoMonitorConfig();
        var service = CreateServiceWithNoRetryDelay();

        var callCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new Win32Exception(ERROR_GEN_FAILURE);
            });

        await service.SoloMonitorAsync("DEL4321");

        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task EnableMonitorAsync_Error87_RequeriesAndRetries()
    {
        SetupOneActiveOneInactive();
        var service = CreateServiceWithNoRetryDelay();

        var queryCount = 0;
        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._))
            .ReturnsLazily(() =>
            {
                queryCount++;
                // Return the same valid config each time
                var paths = new[]
                {
                    MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
                    MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
                };
                var modes = new[]
                {
                    MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
                    MakeTargetMode(Adapter1, 10, 120000, 1000),
                };
                return (paths, modes);
            });

        var applyCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() =>
            {
                applyCount++;
                if (applyCount == 1)
                    throw new Win32Exception(ERROR_INVALID_PARAMETER);
            });

        await service.EnableMonitorAsync("DEL4321");

        applyCount.ShouldBe(2);
        // Initial query for GetMonitorsAsync + first buildConfig + re-query on error 87
        queryCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task EnableMonitorAsync_ExhaustsRetries_Throws()
    {
        SetupOneActiveOneInactive();
        var service = CreateServiceWithNoRetryDelay();

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Throws(() => new Win32Exception(ERROR_GEN_FAILURE));

        await Should.ThrowAsync<Win32Exception>(() => service.EnableMonitorAsync("DEL4321"));

        // 1 initial + 3 retries = 4 total attempts
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappened(4, Times.Exactly);
    }

    [Fact]
    public async Task SoloMonitorAsync_ExhaustsRetries_Throws()
    {
        SetupTwoMonitorConfig();
        var service = CreateServiceWithNoRetryDelay();

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Throws(() => new Win32Exception(ERROR_GEN_FAILURE));

        await Should.ThrowAsync<Win32Exception>(() => service.SoloMonitorAsync("DEL4321"));

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappened(4, Times.Exactly);
    }

    [Fact]
    public async Task EnableMonitorAsync_NonRetryableError_ThrowsImmediately()
    {
        SetupOneActiveOneInactive();
        var service = CreateServiceWithNoRetryDelay();

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Throws(() => new Win32Exception(5)); // ERROR_ACCESS_DENIED — not retryable

        await Should.ThrowAsync<Win32Exception>(() => service.EnableMonitorAsync("DEL4321"));

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── DISPLAYCONFIG_RATIONAL.ToHz ───────────────────────────────────

    [Theory]
    [InlineData(144000u, 1000u, 144)]
    [InlineData(120000u, 1000u, 120)]
    [InlineData(60000u, 1000u, 60)]
    [InlineData(59940u, 1000u, 60)]  // 59.94 rounds to 60
    [InlineData(0u, 0u, 0)]          // zero denominator
    [InlineData(143856u, 1000u, 144)] // 143.856 rounds to 144
    public void Rational_ToHz_CalculatesCorrectly(uint numerator, uint denominator, int expected)
    {
        var rational = new DISPLAYCONFIG_RATIONAL { Numerator = numerator, Denominator = denominator };

        rational.ToHz().ShouldBe(expected);
    }

    // ── Compatible mode ──────────────────────────────────────────────

    [Fact]
    public async Task Compatible_SoloMonitorAsync_CallsApplyMultipleTimes()
    {
        SetupTwoMonitorConfig();
        var service = CreateCompatibleService();

        var applyCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() => applyCount++);

        await service.SoloMonitorAsync("DEL4321");

        // Should call Apply multiple times (set primary + disable other)
        applyCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Compatible_SoloMonitorAsync_SkipsEnableIfTargetAlreadyActive()
    {
        SetupTwoMonitorConfig();
        var service = CreateCompatibleService();

        var applyCallPaths = new List<DISPLAYCONFIG_PATH_INFO[]>();
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) =>
                applyCallPaths.Add(p));

        await service.SoloMonitorAsync("GSM59A4"); // already active + primary

        // Only the disable step for the other monitor
        applyCallPaths.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Compatible_EnableMonitorAsync_NoOpIfAlreadyActive()
    {
        SetupTwoMonitorConfig();
        var service = CreateCompatibleService();

        await service.EnableMonitorAsync("GSM59A4"); // already active

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Compatible_DisableMonitorAsync_NoOpIfAlreadyInactive()
    {
        SetupOneActiveOneInactive();
        var service = CreateCompatibleService();

        await service.DisableMonitorAsync("DEL4321"); // already inactive

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustNotHaveHappened();
    }

    [Fact]
    public async Task Compatible_DisableMonitorAsync_ShufflesPrimaryFirst()
    {
        SetupTwoMonitorConfig();
        var service = CreateCompatibleService();

        var applyCount = 0;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() => applyCount++);

        // GSM59A4 is the primary — disabling it should set DEL4321 as primary first
        await service.DisableMonitorAsync("GSM59A4");

        // At least 2 calls: SetPrimary(DEL4321) + Disable(GSM59A4)
        applyCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Compatible_SetPrimaryAsync_EnablesFirstIfInactive()
    {
        var service = CreateCompatibleService();

        // Start with DEL4321 inactive; after first ApplyConfig (Enable), switch to both active
        var applyCount = 0;
        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._))
            .ReturnsLazily(() =>
            {
                if (applyCount > 0)
                {
                    // After Enable: both active, GSM59A4 at (0,0), DEL4321 at (3840,0)
                    return (
                        new[]
                        {
                            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
                            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
                        },
                        new[]
                        {
                            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
                            MakeTargetMode(Adapter1, 10, 120000, 1000),
                            MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),
                            MakeTargetMode(Adapter1, 20, 60000, 1000),
                        });
                }
                // Initial: DEL4321 inactive
                return (
                    new[]
                    {
                        MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
                        MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
                    },
                    new[]
                    {
                        MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
                        MakeTargetMode(Adapter1, 10, 120000, 1000),
                    });
            });

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes(() => applyCount++);

        await service.SetPrimaryAsync("DEL4321"); // inactive

        // At least 2 calls: Enable + SetPrimary
        applyCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Compatible_VerificationFailure_Retries()
    {
        SetupOneActiveOneInactive();
        var service = CreateCompatibleService();

        // First Apply succeeds but verification fails (still shows inactive)
        var queryCount = 0;
        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._))
            .ReturnsLazily(() =>
            {
                queryCount++;
                // After enough queries, return the monitor as active
                if (queryCount >= 5)
                {
                    var activePaths = new[]
                    {
                        MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
                        MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
                    };
                    var activeModes = new[]
                    {
                        MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
                        MakeTargetMode(Adapter1, 10, 120000, 1000),
                        MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),
                        MakeTargetMode(Adapter1, 20, 60000, 1000),
                    };
                    return (activePaths, activeModes);
                }

                // Return original inactive config
                var paths = new[]
                {
                    MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
                    MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
                };
                var modes = new[]
                {
                    MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
                    MakeTargetMode(Adapter1, 10, 120000, 1000),
                };
                return (paths, modes);
            });

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");

        await service.EnableMonitorAsync("DEL4321");

        // Should have called ApplyConfig at least 2 times (first attempt + retry after verification failure)
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappened(2, Times.OrMore);
    }

    [Fact]
    public async Task Direct_SoloMonitorAsync_UnchangedBehavior_SingleApplyCall()
    {
        SetupTwoMonitorConfig();
        var service = CreateService(); // Direct mode (default)

        await service.SoloMonitorAsync("GSM59A4");

        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .MustHaveHappenedOnceExactly();
    }

    // ── UseSavedLayout tests ─────────────────────────────────────────

    private WindowsMonitorService CreateServiceWithSavedLayout(bool useSavedLayout)
        => new(_api, _logger, MakeOptions(useSavedLayout: useSavedLayout));

    /// <summary>
    /// Sets up one active + one inactive monitor using standard EDID values.
    /// Returns the inactive monitor's enable-config paths/modes for use with QDC_DATABASE_CURRENT or QDC_ALL_PATHS.
    /// Inactive monitor ID = "DEL4321" (targetId 20).
    /// </summary>
    private (DISPLAYCONFIG_PATH_INFO[], DISPLAYCONFIG_MODE_INFO[]) SetupSavedLayoutMocks()
    {
        SetupOneActiveOneInactive();

        var enablePaths = new[] { MakeInactivePath(Adapter1, 20, 1) };
        var enableModes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
        return (enablePaths, enableModes);
    }

    [Fact]
    public async Task EnableMonitor_UseSavedLayout_QueriesDatabaseCurrentFirst()
    {
        var service = CreateServiceWithSavedLayout(true);
        var (paths, modes) = SetupSavedLayoutMocks();

        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Returns((paths, modes));

        await service.EnableMonitorAsync("DEL4321");

        // Called once during HasSavedLayout probe (QueryMonitors) and once in BuildEnableConfig
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .MustHaveHappened(2, Times.Exactly);
    }

    [Fact]
    public async Task EnableMonitor_UseSavedLayout_FallsBackOnError87()
    {
        var service = CreateServiceWithSavedLayout(true);
        var (paths, modes) = SetupSavedLayoutMocks();

        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Throws(new Win32Exception(87));
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS))
            .Returns((paths, modes));

        await service.EnableMonitorAsync("DEL4321");

        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS))
            .MustHaveHappened();
    }

    [Fact]
    public async Task EnableMonitor_UseSavedLayoutFalse_UsesDatabaseCurrentOnlyForProbe()
    {
        var service = CreateServiceWithSavedLayout(false);
        var (paths, modes) = SetupSavedLayoutMocks();

        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS))
            .Returns((paths, modes));

        await service.EnableMonitorAsync("DEL4321");

        // QDC_DATABASE_CURRENT is called once from the HasSavedLayout probe, but not from BuildEnableConfig
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS))
            .MustHaveHappened();
    }

    [Fact]
    public async Task EnableMonitor_UseSavedLayout_PreservesModeIndexes()
    {
        var service = CreateServiceWithSavedLayout(true);

        // QDC_ALL_PATHS for GetMonitorsAsync (inactive DEL4321)
        var allPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
        };
        var allModes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 120000, 1000),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).Returns((allPaths, allModes));

        // QDC_DATABASE_CURRENT returns DEL4321 with non-INVALID mode indexes
        var dbPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 5, targetModeIdx: 6),
        };
        var dbModes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT)).Returns((dbPaths, dbModes));

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.EnableMonitorAsync("DEL4321");

        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        targetPath.sourceInfo.modeInfoIdx.ShouldNotBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        targetPath.targetInfo.modeInfoIdx.ShouldNotBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        targetPath.sourceInfo.modeInfoIdx.ShouldBe(5u);
        targetPath.targetInfo.modeInfoIdx.ShouldBe(6u);
    }

    [Fact]
    public async Task EnableMonitor_UseSavedLayout_FallbackClearsModeIndexes()
    {
        var service = CreateServiceWithSavedLayout(true);

        // QDC_DATABASE_CURRENT throws error 87 — fallback to QDC_ALL_PATHS
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Throws(new Win32Exception(ERROR_INVALID_PARAMETER));

        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeInactivePath(Adapter1, targetId: 20, sourceId: 1),
        };
        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 120000, 1000),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).Returns((paths, modes));

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.EnableMonitorAsync("DEL4321");

        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        targetPath.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        targetPath.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    [Fact]
    public async Task SoloMonitor_UseSavedLayout_PreservesTargetModeIndexes()
    {
        var service = CreateServiceWithSavedLayout(true);

        // QDC_ALL_PATHS for GetMonitorsAsync
        var allPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
        };
        var allModes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
            MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),
            MakeTargetMode(Adapter1, 20, 60000, 1000),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).Returns((allPaths, allModes));

        // QDC_DATABASE_CURRENT returns DEL4321 with non-INVALID mode indexes
        var dbPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 7, targetModeIdx: 8),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Returns((dbPaths, Array.Empty<DISPLAYCONFIG_MODE_INFO>()));

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        targetPath.sourceInfo.modeInfoIdx.ShouldBe(7u);
        targetPath.targetInfo.modeInfoIdx.ShouldBe(8u);
    }

    [Fact]
    public async Task SoloMonitor_UseSavedLayout_AlwaysClearsDeactivatedPathModeIndexes()
    {
        var service = CreateServiceWithSavedLayout(true);

        // QDC_ALL_PATHS for GetMonitorsAsync
        var allPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
        };
        var allModes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
            MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),
            MakeTargetMode(Adapter1, 20, 60000, 1000),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).Returns((allPaths, allModes));

        // QDC_DATABASE_CURRENT — both paths have valid mode indexes
        var dbPaths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 4, targetModeIdx: 5),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 7, targetModeIdx: 8),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Returns((dbPaths, Array.Empty<DISPLAYCONFIG_MODE_INFO>()));

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        // Deactivated path (GSM59A4, targetId 10) must have INVALID indexes
        var deactivatedPath = appliedPaths!.First(p => p.targetInfo.id == 10);
        deactivatedPath.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        deactivatedPath.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    [Fact]
    public async Task SoloMonitor_AllPaths_ClearsAllModeIndexes()
    {
        var service = CreateServiceWithSavedLayout(false);

        // QDC_ALL_PATHS for both GetMonitorsAsync and BuildSoloConfig
        var paths = new[]
        {
            MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1),
            MakeActivePath(Adapter1, targetId: 20, sourceId: 1, sourceModeIdx: 2, targetModeIdx: 3),
        };
        var modes = new[]
        {
            MakeSourceMode(Adapter1, 0, 3840, 2160, 0, 0),
            MakeTargetMode(Adapter1, 10, 144000, 1000),
            MakeSourceMode(Adapter1, 1, 2560, 1440, 3840, 0),
            MakeTargetMode(Adapter1, 20, 60000, 1000),
        };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_ALL_PATHS)).Returns((paths, modes));

        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 10)).Returns(("LG ULTRAGEAR", (ushort)0x6D1E, (ushort)0x59A4));
        A.CallTo(() => _api.GetTargetDeviceInfo(Adapter1, 20)).Returns(("Dell U2723QE", (ushort)0xAC10, (ushort)0x4321));
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 0)).Returns(@"\\.\DISPLAY1");
        A.CallTo(() => _api.GetSourceGdiName(Adapter1, 1)).Returns(@"\\.\DISPLAY2");

        DISPLAYCONFIG_PATH_INFO[]? appliedPaths = null;
        A.CallTo(() => _api.ApplyConfig(A<DISPLAYCONFIG_PATH_INFO[]>._, A<DISPLAYCONFIG_MODE_INFO[]>._, A<SetDisplayConfigFlags>._))
            .Invokes((DISPLAYCONFIG_PATH_INFO[] p, DISPLAYCONFIG_MODE_INFO[] _, SetDisplayConfigFlags _) => appliedPaths = p);

        await service.SoloMonitorAsync("DEL4321");

        // With UseSavedLayout=false, all paths (target and inactive) must have INVALID indexes
        var targetPath = appliedPaths!.First(p => p.targetInfo.id == 20);
        var otherPath = appliedPaths!.First(p => p.targetInfo.id == 10);
        targetPath.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        targetPath.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        otherPath.sourceInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        otherPath.targetInfo.modeInfoIdx.ShouldBe(DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
    }

    // ── HasSavedLayout ───────────────────────────────────────────────

    [Fact]
    public void QueryMonitors_HasSavedLayout_TrueWhenPresentInDatabaseCurrent()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        // QDC_DATABASE_CURRENT returns a path for targetId 10 (GSM59A4) only
        var dbPaths = new[] { MakeActivePath(Adapter1, targetId: 10, sourceId: 0, sourceModeIdx: 0, targetModeIdx: 1) };
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Returns((dbPaths, Array.Empty<DISPLAYCONFIG_MODE_INFO>()));

        var monitors = service.QueryMonitors();

        monitors.First(m => m.MonitorId == "GSM59A4").HasSavedLayout.ShouldBeTrue();
        monitors.First(m => m.MonitorId == "DEL4321").HasSavedLayout.ShouldBeFalse();
    }

    [Fact]
    public void QueryMonitors_HasSavedLayout_FalseWhenDatabaseCurrentThrowsError87()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Throws(new Win32Exception(ERROR_INVALID_PARAMETER));

        var monitors = service.QueryMonitors();

        monitors.ShouldAllBe(m => !m.HasSavedLayout);
    }

    [Fact]
    public void QueryMonitors_HasSavedLayout_FalseWhenNotInDatabaseCurrent()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        // QDC_DATABASE_CURRENT returns empty — no saved layout for any monitor
        A.CallTo(() => _api.QueryConfig(QueryDisplayConfigFlags.QDC_DATABASE_CURRENT))
            .Returns((Array.Empty<DISPLAYCONFIG_PATH_INFO>(), Array.Empty<DISPLAYCONFIG_MODE_INFO>()));

        var monitors = service.QueryMonitors();

        monitors.ShouldAllBe(m => !m.HasSavedLayout);
    }
}
