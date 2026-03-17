using FakeItEasy;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Native;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Shouldly;
using static HaPcRemote.Service.Native.DisplayConfigApi;

namespace HaPcRemote.Service.Tests.Services;

public class WindowsMonitorServiceTests
{
    private readonly IDisplayConfigApi _api = A.Fake<IDisplayConfigApi>();
    private readonly ILogger<WindowsMonitorService> _logger = A.Fake<ILogger<WindowsMonitorService>>();

    private WindowsMonitorService CreateService() => new(_api, _logger);

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

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetMonitorsAsync_AfterInvalidateCache_QueriesAgain()
    {
        SetupTwoMonitorConfig();
        var service = CreateService();

        await service.GetMonitorsAsync();
        service.InvalidateCache();
        await service.GetMonitorsAsync();

        A.CallTo(() => _api.QueryConfig(A<QueryDisplayConfigFlags>._)).MustHaveHappened(2, Times.Exactly);
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

    [Fact]
    public void FormatEdidId_LG_ProducesGSM()
    {
        // "GSM" = G(7) S(19) M(13) → native: (7<<10)|(19<<5)|13 = 0x1E6D
        // Big-endian on wire: 0x6D1E
        var result = WindowsMonitorService.FormatEdidId(0x6D1E, 0x59A4);

        result.ShouldBe("GSM59A4");
    }

    [Fact]
    public void FormatEdidId_Dell_ProducesDEL()
    {
        // "DEL" = D(4) E(5) L(12) → native: (4<<10)|(5<<5)|12 = 0x10AC
        // Big-endian on wire: 0xAC10
        var result = WindowsMonitorService.FormatEdidId(0xAC10, 0x4321);

        result.ShouldBe("DEL4321");
    }

    // ── FindMonitor / MatchesId ───────────────────────────────────────

    [Fact]
    public void FindMonitor_MatchByGdiName()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", MonitorName = "LG", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = true },
            new() { Name = @"\\.\DISPLAY2", MonitorId = "DEL4321", MonitorName = "Dell", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = false },
        };

        var result = WindowsMonitorService.FindMonitor(monitors, @"\\.\DISPLAY2");

        result.MonitorId.ShouldBe("DEL4321");
    }

    [Fact]
    public void FindMonitor_MatchByMonitorId()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", MonitorName = "LG", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = true },
        };

        var result = WindowsMonitorService.FindMonitor(monitors, "GSM59A4");

        result.Name.ShouldBe(@"\\.\DISPLAY1");
    }

    [Fact]
    public void FindMonitor_MatchBySerialNumber()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", SerialNumber = "ABC123", MonitorName = "LG", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = true },
        };

        var result = WindowsMonitorService.FindMonitor(monitors, "ABC123");

        result.MonitorId.ShouldBe("GSM59A4");
    }

    [Fact]
    public void FindMonitor_CaseInsensitive()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", MonitorName = "LG", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = true },
        };

        var result = WindowsMonitorService.FindMonitor(monitors, "gsm59a4");

        result.Name.ShouldBe(@"\\.\DISPLAY1");
    }

    [Fact]
    public void FindMonitor_UnknownId_ThrowsKeyNotFoundException()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", MonitorName = "LG", Width = 0, Height = 0, DisplayFrequency = 0, IsActive = true, IsPrimary = true },
        };

        Should.Throw<KeyNotFoundException>(() => WindowsMonitorService.FindMonitor(monitors, "UNKNOWN"));
    }

    [Fact]
    public void FindMonitor_EmptyList_ThrowsKeyNotFoundException()
    {
        Should.Throw<KeyNotFoundException>(() => WindowsMonitorService.FindMonitor([], "GSM59A4"));
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
        SetupIdenticalMonitors();
        var service = CreateService();

        await service.EnableMonitorAsync("GSM59A4#2");

        // Should have called ApplyConfig — verifying it didn't throw
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
}
