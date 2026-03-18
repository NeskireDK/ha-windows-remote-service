using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class ConfigurationWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public ConfigurationWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ha-pcremote-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "appsettings.json");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ConfigurationWriter CreateWriter() => new(_configPath);

    // ── Read ──────────────────────────────────────────────────────────

    [Fact]
    public void Read_NoFile_ReturnsDefaults()
    {
        var writer = CreateWriter();

        var options = writer.Read();

        options.Port.ShouldBe(5000);
        options.Modes.ShouldBeEmpty();
        options.Power.AutoSleepAfterMinutes.ShouldBe(0);
    }

    [Fact]
    public void Read_ExistingFile_ParsesCorrectly()
    {
        File.WriteAllText(_configPath, """
            {
                "PcRemote": {
                    "Port": 8080,
                    "Modes": {
                        "couch": { "AudioDevice": "HDMI", "Volume": 40 }
                    }
                }
            }
            """);
        var writer = CreateWriter();

        var options = writer.Read();

        options.Port.ShouldBe(8080);
        options.Modes.ShouldContainKey("couch");
        options.Modes["couch"].AudioDevice.ShouldBe("HDMI");
        options.Modes["couch"].Volume.ShouldBe(40);
    }

    // ── Write ─────────────────────────────────────────────────────────

    [Fact]
    public void Write_CreatesFileWithPcRemoteSection()
    {
        var writer = CreateWriter();
        var options = new PcRemoteOptions { Port = 9090 };

        writer.Write(options);

        File.Exists(_configPath).ShouldBeTrue();
        var result = writer.Read();
        result.Port.ShouldBe(9090);
    }

    [Fact]
    public void Write_PreservesOtherSections()
    {
        File.WriteAllText(_configPath, """
            {
                "Logging": { "LogLevel": "Warning" },
                "PcRemote": { "Port": 5000 }
            }
            """);
        var writer = CreateWriter();

        writer.Write(new PcRemoteOptions { Port = 9090 });

        var json = File.ReadAllText(_configPath);
        json.ShouldContain("Logging");
        json.ShouldContain("Warning");
    }

    // ── SaveMode / DeleteMode ─────────────────────────────────────────

    [Fact]
    public void SaveMode_AddsNewMode()
    {
        var writer = CreateWriter();

        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI", Volume = 40 });

        var options = writer.Read();
        options.Modes.ShouldContainKey("couch");
        options.Modes["couch"].AudioDevice.ShouldBe("HDMI");
        options.Modes["couch"].Volume.ShouldBe(40);
    }

    [Fact]
    public void SaveMode_UpdatesExistingMode()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI", Volume = 40 });
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "Speakers", Volume = 25 });

        var options = writer.Read();
        options.Modes["couch"].AudioDevice.ShouldBe("Speakers");
        options.Modes["couch"].Volume.ShouldBe(25);
    }

    [Fact]
    public void SaveMode_PreservesOtherModes()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });
        writer.SaveMode("desktop", new ModeConfig { AudioDevice = "Speakers" });

        var options = writer.Read();
        options.Modes.Count.ShouldBe(2);
        options.Modes.ShouldContainKey("couch");
        options.Modes.ShouldContainKey("desktop");
    }

    [Fact]
    public void DeleteMode_RemovesMode()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });
        writer.SaveMode("desktop", new ModeConfig { AudioDevice = "Speakers" });

        writer.DeleteMode("couch");

        var options = writer.Read();
        options.Modes.ShouldNotContainKey("couch");
        options.Modes.ShouldContainKey("desktop");
    }

    [Fact]
    public void DeleteMode_NonExistent_NoOp()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });

        writer.DeleteMode("nonexistent");

        var options = writer.Read();
        options.Modes.Count.ShouldBe(1);
    }

    // ── SavePowerSettings ─────────────────────────────────────────────

    [Fact]
    public void SavePowerSettings_PersistsSettings()
    {
        var writer = CreateWriter();

        writer.SavePowerSettings(new PowerSettings
        {
            AutoSleepAfterMinutes = 30
        });

        var options = writer.Read();
        options.Power.AutoSleepAfterMinutes.ShouldBe(30);
    }

    [Fact]
    public void SavePowerSettings_DoesNotAffectModes()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });

        writer.SavePowerSettings(new PowerSettings { AutoSleepAfterMinutes = 30 });

        var options = writer.Read();
        options.Modes.ShouldContainKey("couch");
        options.Power.AutoSleepAfterMinutes.ShouldBe(30);
    }

    // ── SavePort ────────────────────────────────────────────────────────

    [Fact]
    public void SavePort_PersistsNewPort()
    {
        var writer = CreateWriter();

        writer.SavePort(8080);

        var options = writer.Read();
        options.Port.ShouldBe(8080);
    }

    [Fact]
    public void SavePort_DoesNotAffectModesOrPower()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });
        writer.SavePowerSettings(new PowerSettings { AutoSleepAfterMinutes = 30 });

        writer.SavePort(9090);

        var options = writer.Read();
        options.Port.ShouldBe(9090);
        options.Modes.ShouldContainKey("couch");
        options.Power.AutoSleepAfterMinutes.ShouldBe(30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1023)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void SavePort_InvalidPort_Throws(int port)
    {
        var writer = CreateWriter();

        Should.Throw<ArgumentOutOfRangeException>(() => writer.SavePort(port));
    }

    // ── RenameMode ─────────────────────────────────────────────────────

    [Fact]
    public void RenameMode_AtomicallyRenamesMode()
    {
        var writer = CreateWriter();
        writer.SaveMode("old-name", new ModeConfig { AudioDevice = "HDMI", Volume = 40 });

        writer.RenameMode("old-name", "new-name", new ModeConfig { AudioDevice = "HDMI", Volume = 40 });

        var options = writer.Read();
        options.Modes.ShouldNotContainKey("old-name");
        options.Modes.ShouldContainKey("new-name");
        options.Modes["new-name"].AudioDevice.ShouldBe("HDMI");
    }

    [Fact]
    public void RenameMode_PreservesOtherModes()
    {
        var writer = CreateWriter();
        writer.SaveMode("couch", new ModeConfig { AudioDevice = "HDMI" });
        writer.SaveMode("desktop", new ModeConfig { AudioDevice = "Speakers" });

        writer.RenameMode("couch", "tv", new ModeConfig { AudioDevice = "HDMI" });

        var options = writer.Read();
        options.Modes.ShouldNotContainKey("couch");
        options.Modes.ShouldContainKey("tv");
        options.Modes.ShouldContainKey("desktop");
    }

    // ── ModeConfig fields ─────────────────────────────────────────────

    [Fact]
    public void SaveMode_AllFieldsRoundTrip()
    {
        var writer = CreateWriter();
        var mode = new ModeConfig
        {
            AudioDevice = "HDMI",
            SoloMonitor = "GSM59A4",
            Volume = 40,
            LaunchApp = "steam-bigpicture",
            KillApp = null
        };

        writer.SaveMode("couch", mode);

        var result = writer.Read().Modes["couch"];
        result.AudioDevice.ShouldBe("HDMI");
        result.SoloMonitor.ShouldBe("GSM59A4");
        result.Volume.ShouldBe(40);
        result.LaunchApp.ShouldBe("steam-bigpicture");
        result.KillApp.ShouldBeNull();
    }

    // ── SaveApp ───────────────────────────────────────────────────────

    [Fact]
    public void SaveApp_WritesAndReadsBack()
    {
        var writer = CreateWriter();
        var app = new AppDefinitionOptions
        {
            DisplayName = "Steam",
            ExePath = @"C:\Program Files (x86)\Steam\steam.exe",
            Arguments = "-bigpicture",
            ProcessName = "steam",
            UseShellExecute = false
        };

        writer.SaveApp("steam", app);

        var result = writer.Read().Apps["steam"];
        result.DisplayName.ShouldBe("Steam");
        result.ExePath.ShouldBe(@"C:\Program Files (x86)\Steam\steam.exe");
        result.Arguments.ShouldBe("-bigpicture");
        result.ProcessName.ShouldBe("steam");
        result.UseShellExecute.ShouldBeFalse();
    }

    [Fact]
    public void SaveApp_OverwritesExistingKey()
    {
        var writer = CreateWriter();
        writer.SaveApp("steam", new AppDefinitionOptions
        {
            DisplayName = "Steam Old",
            ExePath = @"C:\old\steam.exe",
            ProcessName = "steam"
        });

        writer.SaveApp("steam", new AppDefinitionOptions
        {
            DisplayName = "Steam New",
            ExePath = @"C:\new\steam.exe",
            ProcessName = "steam"
        });

        writer.Read().Apps["steam"].DisplayName.ShouldBe("Steam New");
    }
}
