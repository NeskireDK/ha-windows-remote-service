using HaPcRemote.Service.Services;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class BigPictureTrackerTests
{
    [Fact]
    public void IsRunning_DefaultsToFalse()
    {
        var tracker = new BigPictureTracker();

        tracker.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void MarkStarted_SetsIsRunningToTrue()
    {
        var tracker = new BigPictureTracker();

        tracker.MarkStarted();

        tracker.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void MarkStopped_SetsIsRunningToFalse()
    {
        var tracker = new BigPictureTracker();
        tracker.MarkStarted();

        tracker.MarkStopped();

        tracker.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void MarkStarted_IsIdempotent()
    {
        var tracker = new BigPictureTracker();

        tracker.MarkStarted();
        tracker.MarkStarted();

        tracker.IsRunning.ShouldBeTrue();
    }
}
