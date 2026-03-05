using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class AppServiceTests
{
    private static IOptionsMonitor<PcRemoteOptions> CreateOptions(
        Dictionary<string, AppDefinitionOptions>? apps = null)
    {
        var monitor = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new PcRemoteOptions
        {
            Apps = apps ?? new Dictionary<string, AppDefinitionOptions>()
        });
        return monitor;
    }

    private static AppService CreateService(
        Dictionary<string, AppDefinitionOptions>? apps = null,
        IAppLauncher? launcher = null,
        IBigPictureTracker? tracker = null) =>
        new(CreateOptions(apps), launcher ?? A.Fake<IAppLauncher>(), tracker ?? A.Fake<IBigPictureTracker>());

    [Fact]
    public async Task LaunchAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.LaunchAsync("nonexistent"));
    }

    [Fact]
    public async Task KillAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.KillAsync("nonexistent"));
    }

    [Fact]
    public async Task GetStatusAsync_UnknownKey_ThrowsKeyNotFoundException()
    {
        var service = CreateService();

        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.GetStatusAsync("nonexistent"));
    }

    [Fact]
    public async Task GetAllStatusesAsync_ReturnsConfiguredApps()
    {
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = "notepad.exe",
                ProcessName = "notepad_test_unlikely_running_12345"
            },
            ["calc"] = new()
            {
                DisplayName = "Calculator",
                ExePath = "calc.exe",
                ProcessName = "calc_test_unlikely_running_12345"
            }
        };
        var service = CreateService(apps);

        var result = await service.GetAllStatusesAsync();

        result.Count.ShouldBe(2);
        result.ShouldContain(a => a.Key == "notepad" && a.DisplayName == "Notepad");
        result.ShouldContain(a => a.Key == "calc" && a.DisplayName == "Calculator");
    }

    [Fact]
    public async Task GetAllStatusesAsync_EmptyConfig_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.GetAllStatusesAsync();

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStatusAsync_KnownKey_ReturnsAppInfo()
    {
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["myapp"] = new()
            {
                DisplayName = "My App",
                ExePath = "myapp.exe",
                ProcessName = "myapp_test_unlikely_running_12345"
            }
        };
        var service = CreateService(apps);

        var result = await service.GetStatusAsync("myapp");

        result.Key.ShouldBe("myapp");
        result.DisplayName.ShouldBe("My App");
        result.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task LaunchAsync_KnownKey_CallsAppLauncherWithCorrectArgs()
    {
        var launcher = A.Fake<IAppLauncher>();
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = @"C:\Windows\notepad.exe",
                Arguments = "--new-window",
                ProcessName = "notepad"
            }
        };
        var service = CreateService(apps, launcher);

        await service.LaunchAsync("notepad");

        A.CallTo(() => launcher.LaunchAsync(@"C:\Windows\notepad.exe", "--new-window", false))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchAsync_NullArguments_PassesNullToLauncher()
    {
        var launcher = A.Fake<IAppLauncher>();
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["calc"] = new()
            {
                DisplayName = "Calculator",
                ExePath = "calc.exe",
                ProcessName = "calc"
            }
        };
        var service = CreateService(apps, launcher);

        await service.LaunchAsync("calc");

        A.CallTo(() => launcher.LaunchAsync("calc.exe", null, false))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task KillAsync_KnownKey_DoesNotThrow()
    {
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = "notepad.exe",
                ProcessName = "notepad_unlikely_running_xyz_12345"
            }
        };
        var service = CreateService(apps);

        // Process not running — should complete without throwing
        await Should.NotThrowAsync(() => service.KillAsync("notepad"));
    }

    [Fact]
    public async Task GetAllStatusesAsync_AppKeyIsPreservedInResult()
    {
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["my-app-key"] = new()
            {
                DisplayName = "My App",
                ExePath = "myapp.exe",
                ProcessName = "myapp_unlikely_running_xyz_12345"
            }
        };
        var service = CreateService(apps);

        var result = await service.GetAllStatusesAsync();

        result.Count.ShouldBe(1);
        result[0].Key.ShouldBe("my-app-key");
    }

    [Fact]
    public async Task GetStatusAsync_IsRunning_ReflectsProcessState()
    {
        // Use a process name that is definitely not running
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["ghost"] = new()
            {
                DisplayName = "Ghost",
                ExePath = "ghost.exe",
                ProcessName = "ghost_never_running_zzz_99999"
            }
        };
        var service = CreateService(apps);

        var result = await service.GetStatusAsync("ghost");

        result.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task LaunchAsync_UseShellExecuteTrue_PassesTrueToLauncher()
    {
        var launcher = A.Fake<IAppLauncher>();
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["steam"] = new()
            {
                DisplayName = "Steam",
                ExePath = @"C:\Program Files (x86)\Steam\steam.exe",
                Arguments = "-bigpicture",
                ProcessName = "steam",
                UseShellExecute = true
            }
        };
        var service = CreateService(apps, launcher);

        await service.LaunchAsync("steam");

        A.CallTo(() => launcher.LaunchAsync(
                @"C:\Program Files (x86)\Steam\steam.exe", "-bigpicture", true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchAsync_AppKeyIsCaseSensitive_ThrowsForWrongCase()
    {
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = "notepad.exe",
                ProcessName = "notepad"
            }
        };
        var service = CreateService(apps);

        // Default Dictionary is case-sensitive — "Notepad" != "notepad"
        await Should.ThrowAsync<KeyNotFoundException>(
            () => service.LaunchAsync("Notepad"));
    }
}
