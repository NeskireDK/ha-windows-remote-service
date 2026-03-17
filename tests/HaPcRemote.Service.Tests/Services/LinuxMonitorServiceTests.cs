using System.Text.Json;
using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

#pragma warning disable CA1416 // platform compatibility

public class LinuxMonitorServiceTests : IDisposable
{
    private const string XrandrQueryDualMonitor = """
        Screen 0: minimum 8 x 8, current 5120 x 1440, maximum 32767 x 32767
        DP-0 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 597mm x 336mm
           2560x1440     59.95*+  143.97
           1920x1080     60.00    59.94
        HDMI-0 connected 2560x1440+2560+0 (normal left inverted right x axis y axis) 597mm x 336mm
           2560x1440     59.95*+
           1920x1080     60.00
        """;

    private const string XrandrQuerySinglePrimary = """
        Screen 0: minimum 8 x 8, current 2560 x 1440, maximum 32767 x 32767
        DP-0 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 597mm x 336mm
           2560x1440     143.97*+
           1920x1080     60.00    59.94
        """;

    private const string XrandrQueryHighRefresh = """
        Screen 0: minimum 8 x 8, current 3840 x 2160, maximum 32767 x 32767
        DP-1 connected primary 3840x2160+0+0 (normal left inverted right x axis y axis) 600mm x 340mm
           3840x2160     120.00*+  60.00    59.94
           2560x1440     144.00
        """;

    private readonly string _tempDir;
    private readonly ICliRunner _cliRunner = A.Fake<ICliRunner>();

    public LinuxMonitorServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"linux-monitor-test-{Guid.NewGuid():N}");
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
            ProfilesPath = profilesPath ?? _tempDir
        });
        return monitor;
    }

    private LinuxMonitorService CreateService(string? profilesPath = null) =>
        new(
            _cliRunner,
            CreateOptions(profilesPath),
            A.Fake<ILogger<LinuxMonitorService>>());

    // ── xrandr --query parsing tests ─────────────────────────────────

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesBothOutputs()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs.Count.ShouldBe(2);
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesOutputNames()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs[0].Name.ShouldBe("DP-0");
        result.Outputs[1].Name.ShouldBe("HDMI-0");
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesResolution()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs[0].Width.ShouldBe(2560);
        result.Outputs[0].Height.ShouldBe(1440);
        result.Outputs[1].Width.ShouldBe(2560);
        result.Outputs[1].Height.ShouldBe(1440);
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesPositions()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs[0].PositionX.ShouldBe(0);
        result.Outputs[0].PositionY.ShouldBe(0);
        result.Outputs[1].PositionX.ShouldBe(2560);
        result.Outputs[1].PositionY.ShouldBe(0);
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesPrimary()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs[0].IsPrimary.ShouldBeTrue();
        result.Outputs[1].IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_ParsesRefreshRate()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs[0].RefreshRate.ShouldBe(60); // 59.95 rounds to 60
        result.Outputs[1].RefreshRate.ShouldBe(60);
    }

    [Fact]
    public void ParseXrandrQuery_DualMonitor_AllEnabled()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryDualMonitor);

        result.Outputs.ShouldAllBe(o => o.IsEnabled);
    }

    [Fact]
    public void ParseXrandrQuery_HighRefresh_ParsesRefreshRate()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQueryHighRefresh);

        result.Outputs[0].RefreshRate.ShouldBe(120);
    }

    [Fact]
    public void ParseXrandrQuery_SinglePrimary_Parses144Hz()
    {
        var result = LinuxMonitorService.ParseXrandrQuery(XrandrQuerySinglePrimary);

        result.Outputs[0].RefreshRate.ShouldBe(144);
    }

    [Fact]
    public void ParseXrandrQuery_EmptyInput_ReturnsEmptyOutputs()
    {
        var result = LinuxMonitorService.ParseXrandrQuery("");

        result.Outputs.ShouldBeEmpty();
    }

    [Fact]
    public void ParseXrandrQuery_DisconnectedOutputs_AreIgnored()
    {
        var output = """
            Screen 0: minimum 8 x 8, current 2560 x 1440, maximum 32767 x 32767
            DP-0 connected primary 2560x1440+0+0 (normal left inverted right x axis y axis) 597mm x 336mm
               2560x1440     59.95*+
            DP-1 disconnected (normal left inverted right x axis y axis)
            HDMI-0 disconnected (normal left inverted right x axis y axis)
            """;

        var result = LinuxMonitorService.ParseXrandrQuery(output);

        result.Outputs.Count.ShouldBe(1);
        result.Outputs[0].Name.ShouldBe("DP-0");
    }

    // ── BuildXrandrApplyArgs tests ───────────────────────────────────

    [Fact]
    public void BuildXrandrApplyArgs_SingleOutput_GeneratesCorrectArgs()
    {
        var profile = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0",
                    Width = 2560,
                    Height = 1440,
                    RefreshRate = 60,
                    PositionX = 0,
                    PositionY = 0,
                    IsPrimary = true,
                    IsEnabled = true
                }
            ]
        };

        var args = LinuxMonitorService.BuildXrandrApplyArgs(profile);

        args.ShouldBe([
            "--output", "DP-0",
            "--mode", "2560x1440",
            "--pos", "0x0",
            "--rate", "60",
            "--primary"
        ]);
    }

    [Fact]
    public void BuildXrandrApplyArgs_DualOutput_GeneratesArgsForBoth()
    {
        var profile = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0", Width = 2560, Height = 1440, RefreshRate = 144,
                    PositionX = 0, PositionY = 0, IsPrimary = true, IsEnabled = true
                },
                new LinuxMonitorOutputConfig
                {
                    Name = "HDMI-0", Width = 1920, Height = 1080, RefreshRate = 60,
                    PositionX = 2560, PositionY = 0, IsPrimary = false, IsEnabled = true
                }
            ]
        };

        var args = LinuxMonitorService.BuildXrandrApplyArgs(profile);

        args.ShouldContain("--output");
        args.ShouldContain("DP-0");
        args.ShouldContain("HDMI-0");
        args.ShouldContain("--primary");

        // Primary flag should only appear once
        args.Count(a => a == "--primary").ShouldBe(1);
    }

    [Fact]
    public void BuildXrandrApplyArgs_DisabledOutput_GeneratesOffFlag()
    {
        var profile = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "HDMI-0", IsEnabled = false
                }
            ]
        };

        var args = LinuxMonitorService.BuildXrandrApplyArgs(profile);

        args.ShouldBe(["--output", "HDMI-0", "--off"]);
    }

    [Fact]
    public void BuildXrandrApplyArgs_ZeroRefreshRate_OmitsRateFlag()
    {
        var profile = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0", Width = 2560, Height = 1440,
                    RefreshRate = 0, PositionX = 0, PositionY = 0,
                    IsPrimary = false, IsEnabled = true
                }
            ]
        };

        var args = LinuxMonitorService.BuildXrandrApplyArgs(profile);

        args.ShouldNotContain("--rate");
    }

    // ── JSON roundtrip tests ─────────────────────────────────────────

    [Fact]
    public void ProfileData_JsonRoundtrip_PreservesAllFields()
    {
        var original = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0", Width = 2560, Height = 1440, RefreshRate = 144,
                    PositionX = 0, PositionY = 0, IsPrimary = true, IsEnabled = true
                },
                new LinuxMonitorOutputConfig
                {
                    Name = "HDMI-0", Width = 1920, Height = 1080, RefreshRate = 60,
                    PositionX = 2560, PositionY = 180, IsPrimary = false, IsEnabled = true
                }
            ]
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(original, jsonOptions);
        var deserialized = JsonSerializer.Deserialize<LinuxMonitorProfileData>(json, jsonOptions);

        deserialized.ShouldNotBeNull();
        deserialized.Outputs.Count.ShouldBe(2);

        deserialized.Outputs[0].Name.ShouldBe("DP-0");
        deserialized.Outputs[0].Width.ShouldBe(2560);
        deserialized.Outputs[0].Height.ShouldBe(1440);
        deserialized.Outputs[0].RefreshRate.ShouldBe(144);
        deserialized.Outputs[0].PositionX.ShouldBe(0);
        deserialized.Outputs[0].PositionY.ShouldBe(0);
        deserialized.Outputs[0].IsPrimary.ShouldBeTrue();
        deserialized.Outputs[0].IsEnabled.ShouldBeTrue();

        deserialized.Outputs[1].Name.ShouldBe("HDMI-0");
        deserialized.Outputs[1].PositionX.ShouldBe(2560);
        deserialized.Outputs[1].PositionY.ShouldBe(180);
        deserialized.Outputs[1].IsPrimary.ShouldBeFalse();
    }

    // ── Profile listing tests ────────────────────────────────────────

    [Fact]
    public async Task GetProfilesAsync_WithJsonFiles_ReturnsProfileNames()
    {
        File.WriteAllText(Path.Combine(_tempDir, "gaming.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "work.json"), "{}");
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
    public async Task GetProfilesAsync_ProfilesSortedAlphabetically()
    {
        File.WriteAllText(Path.Combine(_tempDir, "zebra.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "alpha.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "middle.json"), "{}");

        var service = CreateService();

        var result = await service.GetProfilesAsync();

        result.Count.ShouldBe(3);
        result[0].Name.ShouldBe("alpha");
        result[1].Name.ShouldBe("middle");
        result[2].Name.ShouldBe("zebra");
    }

    // ── SaveProfileAsync tests ───────────────────────────────────────

    [Fact]
    public async Task SaveProfileAsync_WritesJsonFile()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr", A<IEnumerable<string>>.That.Contains("--query"), A<int>._))
            .Returns(XrandrQueryDualMonitor);

        var service = CreateService();

        await service.SaveProfileAsync("gaming");

        File.Exists(Path.Combine(_tempDir, "gaming.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task SaveProfileAsync_WritesValidJson()
    {
        A.CallTo(() => _cliRunner.RunAsync("xrandr", A<IEnumerable<string>>.That.Contains("--query"), A<int>._))
            .Returns(XrandrQueryDualMonitor);

        var service = CreateService();

        await service.SaveProfileAsync("gaming");

        var json = File.ReadAllText(Path.Combine(_tempDir, "gaming.json"));
        var data = JsonSerializer.Deserialize<LinuxMonitorProfileData>(json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        data.ShouldNotBeNull();
        data.Outputs.Count.ShouldBe(2);
        data.Outputs[0].Name.ShouldBe("DP-0");
        data.Outputs[0].IsPrimary.ShouldBeTrue();
        data.Outputs[1].Name.ShouldBe("HDMI-0");
    }

    [Fact]
    public async Task SaveProfileAsync_CreatesDirectoryIfMissing()
    {
        var subDir = Path.Combine(_tempDir, "profiles");
        A.CallTo(() => _cliRunner.RunAsync("xrandr", A<IEnumerable<string>>.That.Contains("--query"), A<int>._))
            .Returns(XrandrQuerySinglePrimary);

        var service = CreateService(subDir);

        await service.SaveProfileAsync("test");

        File.Exists(Path.Combine(subDir, "test.json")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("sub/dir")]
    [InlineData("sub\\dir")]
    [InlineData("..")]
    public async Task SaveProfileAsync_PathTraversal_ThrowsArgumentException(string profileName)
    {
        var service = CreateService();

        await Should.ThrowAsync<ArgumentException>(
            () => service.SaveProfileAsync(profileName));
    }

    // ── ApplyProfileAsync tests ──────────────────────────────────────

    [Fact]
    public async Task ApplyProfileAsync_ValidProfile_CallsXrandr()
    {
        // Save a profile first
        var profileData = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0", Width = 2560, Height = 1440, RefreshRate = 60,
                    PositionX = 0, PositionY = 0, IsPrimary = true, IsEnabled = true
                }
            ]
        };
        var json = JsonSerializer.Serialize(profileData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_tempDir, "gaming.json"), json);

        var service = CreateService();

        await service.ApplyProfileAsync("gaming");

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("DP-0") &&
                a.Contains("--mode") && a.Contains("2560x1440") &&
                a.Contains("--primary")),
            A<int>._)).MustHaveHappenedOnceExactly();
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
    public async Task ApplyProfileAsync_InvalidatesCache()
    {
        // Set up a profile
        var profileData = new LinuxMonitorProfileData
        {
            Outputs =
            [
                new LinuxMonitorOutputConfig
                {
                    Name = "DP-0", Width = 2560, Height = 1440,
                    PositionX = 0, PositionY = 0, IsEnabled = true
                }
            ]
        };
        var json = JsonSerializer.Serialize(profileData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(_tempDir, "test.json"), json);

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"), A<int>._))
            .Returns("Monitors: 1\n 0: +*DP-0 2560/597x1440/336+0+0  DP-0\n");
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a => a.Contains("--output")), A<int>._))
            .Returns("");

        var service = CreateService();

        // Prime the cache
        await service.GetMonitorsAsync();

        // Apply profile
        await service.ApplyProfileAsync("test");

        // Get monitors again — should call CLI again (cache invalidated)
        await service.GetMonitorsAsync();

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--listmonitors"),
            A<int>._)).MustHaveHappened(2, Times.Exactly);
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

        // ── Save then apply roundtrip test ───────────────────────────────

    [Fact]
    public async Task SaveThenApply_GeneratesCorrectXrandrCommand()
    {
        // Save captures xrandr --query output
        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Contains("--query"), A<int>._))
            .Returns(XrandrQueryDualMonitor);

        var service = CreateService();
        await service.SaveProfileAsync("dual");

        // Now apply should call xrandr with correct args
        await service.ApplyProfileAsync("dual");

        A.CallTo(() => _cliRunner.RunAsync("xrandr",
            A<IEnumerable<string>>.That.Matches(a =>
                a.Contains("--output") && a.Contains("DP-0")
                && a.Contains("HDMI-0")
                && a.Contains("--mode") && a.Contains("2560x1440")
                && a.Contains("--pos") && a.Contains("0x0")
                && a.Contains("2560x0")
                && a.Contains("--primary")),
            A<int>._)).MustHaveHappenedOnceExactly();
    }
}
