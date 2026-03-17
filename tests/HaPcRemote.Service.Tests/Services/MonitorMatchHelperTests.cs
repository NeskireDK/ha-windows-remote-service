using HaPcRemote.Service.Models;
using HaPcRemote.Service.Services;
using Shouldly;

namespace HaPcRemote.Service.Tests.Services;

public class MonitorMatchHelperTests
{
    private static List<MonitorInfo> CreateMonitors() =>
    [
        new MonitorInfo
        {
            Name = @"\\.\DISPLAY1", MonitorId = "GSM59A4", SerialNumber = "SN001",
            MonitorName = "LG Ultra", Width = 2560, Height = 1440,
            DisplayFrequency = 144, IsActive = true, IsPrimary = true
        },
        new MonitorInfo
        {
            Name = @"\\.\DISPLAY2", MonitorId = "DEL4321", SerialNumber = "XYZ789",
            MonitorName = "Dell", Width = 1920, Height = 1080,
            DisplayFrequency = 60, IsActive = true, IsPrimary = false
        },
        new MonitorInfo
        {
            Name = "DP-0", MonitorId = "0", SerialNumber = null,
            MonitorName = "DP-0", Width = 2560, Height = 1440,
            DisplayFrequency = 0, IsActive = true, IsPrimary = false
        }
    ];

    // ── MatchesId ─────────────────────────────────────────────────────

    [Fact]
    public void MatchesId_ByName_ReturnsTrue()
    {
        var monitor = CreateMonitors()[0];
        MonitorMatchHelper.MatchesId(monitor, @"\\.\DISPLAY1").ShouldBeTrue();
    }

    [Fact]
    public void MatchesId_ByMonitorId_ReturnsTrue()
    {
        var monitor = CreateMonitors()[0];
        MonitorMatchHelper.MatchesId(monitor, "GSM59A4").ShouldBeTrue();
    }

    [Fact]
    public void MatchesId_BySerialNumber_ReturnsTrue()
    {
        var monitor = CreateMonitors()[1];
        MonitorMatchHelper.MatchesId(monitor, "XYZ789").ShouldBeTrue();
    }

    [Fact]
    public void MatchesId_CaseInsensitive_ReturnsTrue()
    {
        var monitor = CreateMonitors()[0];
        MonitorMatchHelper.MatchesId(monitor, "gsm59a4").ShouldBeTrue();
    }

    [Fact]
    public void MatchesId_NullSerial_EmptyStringDoesNotMatch()
    {
        var monitor = CreateMonitors()[2]; // SerialNumber = null
        MonitorMatchHelper.MatchesId(monitor, "").ShouldBeFalse();
    }

    [Fact]
    public void MatchesId_NoMatch_ReturnsFalse()
    {
        var monitor = CreateMonitors()[0];
        MonitorMatchHelper.MatchesId(monitor, "UNKNOWN").ShouldBeFalse();
    }

    // ── FindMonitor ───────────────────────────────────────────────────

    [Fact]
    public void FindMonitor_ByName_ReturnsMonitor()
    {
        var monitors = CreateMonitors();
        var result = MonitorMatchHelper.FindMonitor(monitors, @"\\.\DISPLAY2");
        result.MonitorId.ShouldBe("DEL4321");
    }

    [Fact]
    public void FindMonitor_ByMonitorId_ReturnsMonitor()
    {
        var monitors = CreateMonitors();
        var result = MonitorMatchHelper.FindMonitor(monitors, "DEL4321");
        result.Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void FindMonitor_BySerialNumber_ReturnsMonitor()
    {
        var monitors = CreateMonitors();
        var result = MonitorMatchHelper.FindMonitor(monitors, "XYZ789");
        result.Name.ShouldBe(@"\\.\DISPLAY2");
    }

    [Fact]
    public void FindMonitor_CaseInsensitive_ReturnsMonitor()
    {
        var monitors = CreateMonitors();
        var result = MonitorMatchHelper.FindMonitor(monitors, "gsm59a4");
        result.Name.ShouldBe(@"\\.\DISPLAY1");
    }

    [Fact]
    public void FindMonitor_UnknownId_ThrowsKeyNotFoundException()
    {
        var monitors = CreateMonitors();
        Should.Throw<KeyNotFoundException>(() => MonitorMatchHelper.FindMonitor(monitors, "UNKNOWN"));
    }

    [Fact]
    public void FindMonitor_EmptyList_ThrowsKeyNotFoundException()
    {
        Should.Throw<KeyNotFoundException>(() => MonitorMatchHelper.FindMonitor([], "GSM59A4"));
    }

    [Fact]
    public void FindMonitor_NullSerialNotMatchedByEmptyString()
    {
        var monitors = CreateMonitors();
        Should.Throw<KeyNotFoundException>(() => MonitorMatchHelper.FindMonitor(monitors, ""));
    }
}
