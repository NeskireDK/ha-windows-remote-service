using FakeItEasy;
using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class AppServiceBigPictureTests
{
    private static readonly Dictionary<string, AppDefinitionOptions> BigPictureApps = new()
    {
        ["steam-bigpicture"] = new()
        {
            DisplayName = "Steam Big Picture",
            ExePath = "steam://open/bigpicture",
            Arguments = null,
            ProcessName = "steam",
            UseShellExecute = true
        }
    };

    private static IOptionsMonitor<PcRemoteOptions> CreateOptions(
        Dictionary<string, AppDefinitionOptions>? apps = null)
    {
        var monitor = A.Fake<IOptionsMonitor<PcRemoteOptions>>();
        A.CallTo(() => monitor.CurrentValue).Returns(new PcRemoteOptions
        {
            Apps = apps ?? BigPictureApps
        });
        return monitor;
    }

    [Fact]
    public async Task GetStatusAsync_BigPicture_UsesTrackerNotProcessName()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        A.CallTo(() => tracker.IsRunning).Returns(false);
        var service = new AppService(CreateOptions(), A.Fake<IAppLauncher>(), tracker);

        var result = await service.GetStatusAsync("steam-bigpicture");

        result.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_BigPicture_ReturnsTrue_WhenTrackerSaysRunning()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        A.CallTo(() => tracker.IsRunning).Returns(true);
        var service = new AppService(CreateOptions(), A.Fake<IAppLauncher>(), tracker);

        var result = await service.GetStatusAsync("steam-bigpicture");

        result.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public async Task LaunchAsync_BigPicture_AlwaysSendsUri()
    {
        var launcher = A.Fake<IAppLauncher>();
        var tracker = A.Fake<IBigPictureTracker>();
        var service = new AppService(CreateOptions(), launcher, tracker);

        await service.LaunchAsync("steam-bigpicture");

        A.CallTo(() => launcher.LaunchAsync("steam://open/bigpicture", null, true))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchAsync_BigPicture_MarksTrackerAsStarted()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        var service = new AppService(CreateOptions(), A.Fake<IAppLauncher>(), tracker);

        await service.LaunchAsync("steam-bigpicture");

        A.CallTo(() => tracker.MarkStarted()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task LaunchAsync_NonBigPicture_DoesNotTouchTracker()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = "notepad.exe",
                ProcessName = "notepad"
            }
        };
        var service = new AppService(CreateOptions(apps), A.Fake<IAppLauncher>(), tracker);

        await service.LaunchAsync("notepad");

        A.CallTo(() => tracker.MarkStarted()).MustNotHaveHappened();
    }

    [Fact]
    public async Task KillAsync_BigPicture_MarksTrackerAsStopped()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        var service = new AppService(CreateOptions(), A.Fake<IAppLauncher>(), tracker);

        await service.KillAsync("steam-bigpicture");

        A.CallTo(() => tracker.MarkStopped()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task KillAsync_BigPicture_SendsCloseUri()
    {
        var launcher = A.Fake<IAppLauncher>();
        var tracker = A.Fake<IBigPictureTracker>();
        var service = new AppService(CreateOptions(), launcher, tracker);

        await service.KillAsync("steam-bigpicture");

        A.CallTo(() => launcher.LaunchAsync("steam://close/bigpicture", null, true))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => tracker.MarkStopped()).MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task GetAllStatusesAsync_BigPicture_UsesTrackerForStatus()
    {
        var tracker = A.Fake<IBigPictureTracker>();
        A.CallTo(() => tracker.IsRunning).Returns(true);
        var apps = new Dictionary<string, AppDefinitionOptions>
        {
            ["steam-bigpicture"] = BigPictureApps["steam-bigpicture"],
            ["notepad"] = new()
            {
                DisplayName = "Notepad",
                ExePath = "notepad.exe",
                ProcessName = "notepad_unlikely_xyz_99999"
            }
        };
        var service = new AppService(CreateOptions(apps), A.Fake<IAppLauncher>(), tracker);

        var result = await service.GetAllStatusesAsync();

        var bp = result.Single(a => a.Key == "steam-bigpicture");
        bp.IsRunning.ShouldBeTrue();

        var notepad = result.Single(a => a.Key == "notepad");
        notepad.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task LaunchAsync_BigPicture_IsIdempotent_AlwaysLaunches()
    {
        var launcher = A.Fake<IAppLauncher>();
        var tracker = A.Fake<IBigPictureTracker>();
        // Tracker says already running
        A.CallTo(() => tracker.IsRunning).Returns(true);
        var service = new AppService(CreateOptions(), launcher, tracker);

        // Should still launch — Steam handles idempotency
        await service.LaunchAsync("steam-bigpicture");

        A.CallTo(() => launcher.LaunchAsync("steam://open/bigpicture", null, true))
            .MustHaveHappenedOnceExactly();
    }
}
