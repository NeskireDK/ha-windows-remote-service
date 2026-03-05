using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class MonitorServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ICliRunner _cliRunner = A.Fake<ICliRunner>();

    private void SetupCliRunnerWithXml(string xml)
    {
        A.CallTo(() => _cliRunner.RunAsync(A<string>._, A<IEnumerable<string>>._, A<int>._))
            .Invokes((string _, IEnumerable<string> args, int _) =>
            {
                var argList = args.ToList();
                if (argList.Count >= 2 && argList[0] == "/sxml" && !string.IsNullOrEmpty(argList[1]))
                    File.WriteAllText(argList[1], xml);
            })
            .Returns(string.Empty);
    }

    public MonitorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"monitor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private IOptionsMonitor<PcRemoteOptions> CreateOptions(string? profilesPath = null)
    {
        var monitor = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new PcRemoteOptions
        {
            ToolsPath = "./tools",
            ProfilesPath = profilesPath ?? _tempDir
        });
        return monitor;
    }

    private MonitorService CreateService(string? profilesPath = null) =>
        new MonitorService(CreateOptions(profilesPath), _cliRunner, A.Fake<ILogger<MonitorService>>());

    // ── Profile tests ─────────────────────────────────────────────────

    [Fact]
    public async Task GetProfilesAsync_WithCfgFiles_ReturnsProfileNames()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gaming.cfg"), "");
        File.WriteAllText(Path.Combine(_tempDir, "work.cfg"), "");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), ""); // should be ignored

        var service = CreateService();

        var result = await service.GetProfilesAsync();

        result.Count.ShouldBe(2);
        result.ShouldContain(p => p.Name == "gaming");
        result.ShouldContain(p => p.Name == "work");
    }

    [Fact]
    public async Task GetProfilesAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.GetProfilesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetProfilesAsync_NonExistentDirectory_ReturnsEmptyList()
    {
        var service = CreateService("/nonexistent/path");

        var result = await service.GetProfilesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApplyProfileAsync_UnknownProfile_ThrowsKeyNotFoundException()
    {
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.ApplyProfileAsync("nonexistent"));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("..")]
    public async Task ApplyProfileAsync_PathTraversal_ThrowsArgumentException(string profileName)
    {
        var service = CreateService();

        await Should.ThrowAsync<ArgumentException>(
            () => service.ApplyProfileAsync(profileName));
    }

    [Fact]
    public async Task ApplyProfileAsync_ValidProfile_CallsCliRunner()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gaming.cfg"), "");
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await service.ApplyProfileAsync("gaming");

        A.CallTo(() => _cliRunner.RunAsync(
            A<string>.That.EndsWith("MultiMonitorTool.exe"),
            A<IEnumerable<string>>.That.Contains("/LoadConfig"),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task ApplyProfileAsync_WithInactiveMonitors_EnablesThemBeforeLoadConfig()
    {
        File.WriteAllText(Path.Combine(_tempDir, "desk.cfg"), "");
        var calls = new List<string[]>();
        A.CallTo(() => _cliRunner.RunAsync(A<string>._, A<IEnumerable<string>>._, A<int>._))
            .Invokes((string _, IEnumerable<string> args, int _) =>
            {
                var argList = args.ToList();
                if (argList.Count >= 2 && argList[0] == "/sxml" && !string.IsNullOrEmpty(argList[1]))
                    File.WriteAllText(argList[1], TestData.Load("monitors-inactive-target.xml"));
                else
                    calls.Add(argList.ToArray());
            })
            .Returns(string.Empty);

        var service = CreateService();

        await service.ApplyProfileAsync("desk");

        // First call: enable the inactive monitor (DISPLAY2)
        calls[0][0].ShouldBe("/enable");
        calls[0][1].ShouldBe(@"\\.\DISPLAY2");

        // Second call: /LoadConfig
        calls[1][0].ShouldBe("/LoadConfig");
        calls[1][1].ShouldEndWith("desk.cfg");
    }

    [Fact]
    public async Task ApplyProfileAsync_AllMonitorsActive_SkipsPreEnable()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gaming.cfg"), "");
        var calls = new List<string[]>();
        A.CallTo(() => _cliRunner.RunAsync(A<string>._, A<IEnumerable<string>>._, A<int>._))
            .Invokes((string _, IEnumerable<string> args, int _) =>
            {
                var argList = args.ToList();
                if (argList.Count >= 2 && argList[0] == "/sxml" && !string.IsNullOrEmpty(argList[1]))
                    File.WriteAllText(argList[1], TestData.Load("monitors-sample.xml"));
                else
                    calls.Add(argList.ToArray());
            })
            .Returns(string.Empty);

        var service = CreateService();

        await service.ApplyProfileAsync("gaming");

        // Only call should be /LoadConfig — no /enable calls since all monitors are active
        calls.Count.ShouldBe(1);
        calls[0][0].ShouldBe("/LoadConfig");
    }

    [Fact]
    public async Task ApplyProfileAsync_InvalidatesCache()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gaming.cfg"), "");
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        // Prime the cache
        await service.GetMonitorsAsync();

        // Apply profile (EnableAllInactive uses cached monitors, no extra /sxml call)
        await service.ApplyProfileAsync("gaming");

        // GetMonitorsAsync should hit the CLI again (cache was invalidated by ApplyProfile)
        await service.GetMonitorsAsync();

        // 2 /sxml calls: initial prime + post-apply (EnableAllInactive used cached data)
        A.CallTo(() => _cliRunner.RunAsync(
            A<string>._,
            A<IEnumerable<string>>.That.Matches(a => a.First() == "/sxml"),
            A<int>._)).MustHaveHappened(2, Times.Exactly);
    }

    // ── XML parsing tests ─────────────────────────────────────────────

    [Fact]
    public void ParseXmlOutput_ParsesAllConnectedMonitors()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors.Count.ShouldBe(3);
    }

    [Fact]
    public void ParseXmlOutput_ParsesDisplayName()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].Name.ShouldBe(@"\\.\DISPLAY1");
        monitors[1].Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void ParseXmlOutput_ParsesShortMonitorId()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].MonitorId.ShouldBe("GSM59A4");
        monitors[1].MonitorId.ShouldBe("DEL4321");
    }

    [Fact]
    public void ParseXmlOutput_ParsesFriendlyName()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].MonitorName.ShouldBe("LG ULTRAGEAR");
        monitors[1].MonitorName.ShouldBe("Dell U2723QE");
    }

    [Fact]
    public void ParseXmlOutput_ParsesSerialNumber()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].SerialNumber.ShouldBe("ABC123");
        monitors[1].SerialNumber.ShouldBe("XYZ789");
    }

    [Fact]
    public void ParseXmlOutput_EmptySerialNumber_ReturnsNull()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[2].SerialNumber.ShouldBeNull();
    }

    [Fact]
    public void ParseXmlOutput_ParsesResolution()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].Width.ShouldBe(3840);
        monitors[0].Height.ShouldBe(2160);
        monitors[1].Width.ShouldBe(2560);
        monitors[1].Height.ShouldBe(1440);
    }

    [Fact]
    public void ParseXmlOutput_ParsesDisplayFrequency()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].DisplayFrequency.ShouldBe(144);
        monitors[1].DisplayFrequency.ShouldBe(60);
        monitors[2].DisplayFrequency.ShouldBe(240);
    }

    [Fact]
    public void ParseXmlOutput_IdentifiesPrimaryMonitor()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors[0].IsPrimary.ShouldBeTrue();
        monitors[1].IsPrimary.ShouldBeFalse();
        monitors[2].IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public void ParseXmlOutput_ActiveMonitors_AreActive()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        monitors.ShouldAllBe(m => m.IsActive);
    }

    [Fact]
    public void ParseXmlOutput_FiltersDisconnectedMonitors()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-disconnected.xml"));

        monitors.Count.ShouldBe(1);
        monitors[0].Name.ShouldBe(@"\\.\DISPLAY1");
    }

    [Fact]
    public void ParseXmlOutput_EmptyInput_ReturnsEmptyList()
    {
        var monitors = MonitorService.ParseXmlOutput("");

        monitors.ShouldBeEmpty();
    }

    [Fact]
    public void ParseXmlOutput_WhitespaceOnlyInput_ReturnsEmptyList()
    {
        var monitors = MonitorService.ParseXmlOutput("   \n  \n  ");

        monitors.ShouldBeEmpty();
    }

    [Fact]
    public void ParseXmlOutput_ItemWithEmptyName_IsSkipped()
    {
        var xml = """
            <?xml version="1.0" ?>
            <monitors_list>
              <item>
                <name></name>
                <short_monitor_id>X</short_monitor_id>
                <resolution>1920 X 1080</resolution>
                <active>Yes</active>
                <disconnected>No</disconnected>
              </item>
            </monitors_list>
            """;
        var monitors = MonitorService.ParseXmlOutput(xml);

        monitors.ShouldBeEmpty();
    }

    [Fact]
    public void ParseXmlOutput_MissingOptionalElements_UsesDefaults()
    {
        var xml = """
            <?xml version="1.0" ?>
            <monitors_list>
              <item>
                <name>\\.\DISPLAY1</name>
              </item>
            </monitors_list>
            """;
        var monitors = MonitorService.ParseXmlOutput(xml);

        monitors.Count.ShouldBe(1);
        monitors[0].MonitorId.ShouldBe("");
        monitors[0].MonitorName.ShouldBe("");
        monitors[0].SerialNumber.ShouldBeNull();
        monitors[0].Width.ShouldBe(0);
        monitors[0].Height.ShouldBe(0);
        monitors[0].DisplayFrequency.ShouldBe(0);
        monitors[0].IsActive.ShouldBeFalse();
        monitors[0].IsPrimary.ShouldBeFalse();
    }

    // ── Resolution parsing tests ──────────────────────────────────────

    [Fact]
    public void ParseResolution_StandardFormat_ParsesCorrectly()
    {
        MonitorService.ParseResolution("1920 X 1200", out var w, out var h);

        w.ShouldBe(1920);
        h.ShouldBe(1200);
    }

    [Fact]
    public void ParseResolution_4K_ParsesCorrectly()
    {
        MonitorService.ParseResolution("3840 X 2160", out var w, out var h);

        w.ShouldBe(3840);
        h.ShouldBe(2160);
    }

    [Fact]
    public void ParseResolution_Null_ReturnsZeros()
    {
        MonitorService.ParseResolution(null, out var w, out var h);

        w.ShouldBe(0);
        h.ShouldBe(0);
    }

    [Fact]
    public void ParseResolution_Empty_ReturnsZeros()
    {
        MonitorService.ParseResolution("", out var w, out var h);

        w.ShouldBe(0);
        h.ShouldBe(0);
    }

    [Fact]
    public void ParseResolution_MalformedInput_ReturnsZeros()
    {
        MonitorService.ParseResolution("not a resolution", out var w, out var h);

        w.ShouldBe(0);
        h.ShouldBe(0);
    }

    // ── FindMonitor / ID matching tests ───────────────────────────────

    [Fact]
    public void FindMonitor_MatchByDisplayName_ReturnsMonitor()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        var result = MonitorService.FindMonitor(monitors, @"\\.\DISPLAY2");

        result.MonitorId.ShouldBe("DEL4321");
    }

    [Fact]
    public void FindMonitor_MatchByShortMonitorId_ReturnsMonitor()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        var result = MonitorService.FindMonitor(monitors, "DEL4321");

        result.Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void FindMonitor_MatchBySerialNumber_ReturnsMonitor()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        var result = MonitorService.FindMonitor(monitors, "XYZ789");

        result.Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void FindMonitor_CaseInsensitive_ReturnsMonitor()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        var result = MonitorService.FindMonitor(monitors, "gsm59a4");

        result.Name.ShouldBe(@"\\.\DISPLAY1");
    }

    [Fact]
    public void FindMonitor_UnknownId_ThrowsKeyNotFoundException()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        Should.Throw<KeyNotFoundException>(
            () => MonitorService.FindMonitor(monitors, "UNKNOWN123"));
    }

    [Fact]
    public void FindMonitor_EmptyList_ThrowsKeyNotFoundException()
    {
        var monitors = new List<MonitorInfo>();

        Should.Throw<KeyNotFoundException>(
            () => MonitorService.FindMonitor(monitors, "GSM59A4"));
    }

    [Fact]
    public void FindMonitor_NullSerialNotMatchedByEmptyString()
    {
        var monitors = MonitorService.ParseXmlOutput(TestData.Load("monitors-sample.xml"));

        Should.Throw<KeyNotFoundException>(
            () => MonitorService.FindMonitor(monitors, ""));
    }

    // ── Async method tests (mocked ICliRunner) ────────────────────────

    [Fact]
    public async Task GetMonitorsAsync_CallsCliRunnerAndParsesOutput()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        var monitors = await service.GetMonitorsAsync();

        monitors.Count.ShouldBe(3);
        A.CallTo(() => _cliRunner.RunAsync(
            A<string>.That.EndsWith("MultiMonitorTool.exe"),
            A<IEnumerable<string>>.That.Matches(a =>
                a.First() == "/sxml" && a.Last() != ""),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task EnableMonitorAsync_ResolvesAndCallsCliRunner()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await service.EnableMonitorAsync("DEL4321");

        A.CallTo(() => _cliRunner.RunAsync(
            A<string>._,
            A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "/enable", @"\\.\DISPLAY2" }),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DisableMonitorAsync_ResolvesAndCallsCliRunner()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await service.DisableMonitorAsync("GSM59A4");

        A.CallTo(() => _cliRunner.RunAsync(
            A<string>._,
            A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "/disable", @"\\.\DISPLAY1" }),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task SetPrimaryAsync_ResolvesAndCallsCliRunner()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await service.SetPrimaryAsync("XYZ789");

        A.CallTo(() => _cliRunner.RunAsync(
            A<string>._,
            A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "/SetPrimary", @"\\.\DISPLAY2" }),
            A<int>._)).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task EnableMonitorAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.EnableMonitorAsync("UNKNOWN"));
    }

    [Fact]
    public async Task SoloMonitorAsync_TargetAlreadyActive_SetsPrimaryThenDisablesOthers()
    {
        // DEL4321 (DISPLAY2) is already active in monitors-sample.xml
        var calls = new List<string[]>();
        A.CallTo(() => _cliRunner.RunAsync(A<string>._, A<IEnumerable<string>>._, A<int>._))
            .Invokes((string _, IEnumerable<string> args, int _) =>
            {
                var argList = args.ToList();
                if (argList.Count >= 2 && argList[0] == "/sxml" && !string.IsNullOrEmpty(argList[1]))
                    File.WriteAllText(argList[1], TestData.Load("monitors-sample.xml"));
                else
                    calls.Add(argList.ToArray());
            })
            .Returns(string.Empty);

        var service = CreateService();

        await service.SoloMonitorAsync("DEL4321");

        // Must NOT enable the target (it is already active)
        calls.ShouldNotContain(c => c[0] == "/enable" && c[1] == @"\\.\DISPLAY2");

        // First mutating call must be SetPrimary on the target
        calls[0][0].ShouldBe("/SetPrimary");
        calls[0][1].ShouldBe(@"\\.\DISPLAY2");

        // Then disables of the other active monitors (DISPLAY1 and DISPLAY3)
        calls.ShouldContain(c => c[0] == "/disable" && c[1] == @"\\.\DISPLAY1");
        calls.ShouldContain(c => c[0] == "/disable" && c[1] == @"\\.\DISPLAY3");

        // No disable on the target
        calls.ShouldNotContain(c => c[0] == "/disable" && c[1] == @"\\.\DISPLAY2");
    }

    [Fact]
    public async Task SoloMonitorAsync_TargetInactive_EnablesThenSetsPrimaryThenDisablesOthers()
    {
        var calls = new List<string[]>();
        A.CallTo(() => _cliRunner.RunAsync(A<string>._, A<IEnumerable<string>>._, A<int>._))
            .Invokes((string _, IEnumerable<string> args, int _) =>
            {
                var argList = args.ToList();
                if (argList.Count >= 2 && argList[0] == "/sxml" && !string.IsNullOrEmpty(argList[1]))
                    File.WriteAllText(argList[1], TestData.Load("monitors-inactive-target.xml"));
                else
                    calls.Add(argList.ToArray());
            })
            .Returns(string.Empty);

        var service = CreateService();

        await service.SoloMonitorAsync("DEL4321");

        // Order: enable target, set primary, disable others
        calls[0][0].ShouldBe("/enable");
        calls[0][1].ShouldBe(@"\\.\DISPLAY2");

        calls[1][0].ShouldBe("/SetPrimary");
        calls[1][1].ShouldBe(@"\\.\DISPLAY2");

        calls[2][0].ShouldBe("/disable");
        calls[2][1].ShouldBe(@"\\.\DISPLAY1");

        calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task SoloMonitorAsync_UnknownId_ThrowsKeyNotFoundException()
    {
        SetupCliRunnerWithXml(TestData.Load("monitors-sample.xml"));
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.SoloMonitorAsync("UNKNOWN"));
    }

    // ── ApplyProfileAsync edge cases ──────────────────────────────────

    [Fact]
    public async Task ApplyProfileAsync_ProfileNameWithDots_ThrowsArgumentException()
    {
        var service = CreateService();

        await Should.ThrowAsync<ArgumentException>(
            () => service.ApplyProfileAsync(".."));
    }

    [Fact]
    public async Task ApplyProfileAsync_ProfileFileMissing_ThrowsKeyNotFoundException()
    {
        // Profile name is valid (no traversal), but .cfg file does not exist
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.ApplyProfileAsync("nonexistent-profile"));
    }

    [Fact]
    public async Task GetProfilesAsync_OnlyNonCfgFiles_ReturnsEmptyList()
    {
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "config.json"), "");
        var service = CreateService();

        var result = await service.GetProfilesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetProfilesAsync_ProfileNamesAreSortedAlphabetically()
    {
        File.WriteAllText(Path.Combine(_tempDir, "zebra.cfg"), "");
        File.WriteAllText(Path.Combine(_tempDir, "alpha.cfg"), "");
        File.WriteAllText(Path.Combine(_tempDir, "middle.cfg"), "");
        var service = CreateService();

        var result = await service.GetProfilesAsync();

        result.Count.ShouldBe(3);
        result[0].Name.ShouldBe("alpha");
        result[1].Name.ShouldBe("middle");
        result[2].Name.ShouldBe("zebra");
    }

    // ── ParseXmlOutput edge cases ─────────────────────────────────────

    [Fact]
    public void ParseXmlOutput_MalformedXml_ThrowsException()
    {
        var badXml = "<monitors_list><item><name>unclosed";

        Should.Throw<Exception>(() => MonitorService.ParseXmlOutput(badXml));
    }

    [Fact]
    public void ParseXmlOutput_AllMonitorsDisconnected_ReturnsEmptyList()
    {
        var xml = """
            <?xml version="1.0" ?>
            <monitors_list>
              <item>
                <name>\\.\DISPLAY1</name>
                <disconnected>Yes</disconnected>
                <active>No</active>
              </item>
              <item>
                <name>\\.\DISPLAY2</name>
                <disconnected>Yes</disconnected>
                <active>No</active>
              </item>
            </monitors_list>
            """;

        var monitors = MonitorService.ParseXmlOutput(xml);

        monitors.ShouldBeEmpty();
    }

    [Fact]
    public void ParseXmlOutput_FrequencyMissing_DefaultsToZero()
    {
        var xml = """
            <?xml version="1.0" ?>
            <monitors_list>
              <item>
                <name>\\.\DISPLAY1</name>
                <active>Yes</active>
                <disconnected>No</disconnected>
              </item>
            </monitors_list>
            """;

        var monitors = MonitorService.ParseXmlOutput(xml);

        monitors.Count.ShouldBe(1);
        monitors[0].DisplayFrequency.ShouldBe(0);
    }
}
