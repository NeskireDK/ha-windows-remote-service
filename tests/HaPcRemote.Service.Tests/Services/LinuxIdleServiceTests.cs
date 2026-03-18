using FakeItEasy;
using HaPcRemote.Service.Services;
using Microsoft.Extensions.Logging;
using Shouldly;

#pragma warning disable CA1416 // Platform compatibility

namespace HaPcRemote.Service.Tests.Services;

public class LinuxIdleServiceTests
{
    private readonly ILogger<LinuxIdleService> _logger = A.Fake<ILogger<LinuxIdleService>>();

    [Fact]
    public void GetIdleSeconds_NoBackendAvailable_ReturnsNull()
    {
        // On Windows (CI), no Linux backends are available
        var service = new LinuxIdleService(_logger);
        var result = service.GetIdleSeconds();
        result.ShouldBeNull();
    }

}

public class MutterIdleBackendParsingTests
{
    [Theory]
    [InlineData("(uint64 12345,)", 12345ul)]
    [InlineData("(uint64 0,)", 0ul)]
    [InlineData("(uint64 5000,)", 5000ul)]
    [InlineData("(uint64 999999999,)", 999999999ul)]
    public void ParseGdbusUInt64_ValidOutput_ReturnsValue(string output, ulong expected)
    {
        MutterIdleBackend.ParseGdbusUInt64(output).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error")]
    [InlineData("()")]
    public void ParseGdbusUInt64_InvalidOutput_ReturnsNull(string output)
    {
        MutterIdleBackend.ParseGdbusUInt64(output).ShouldBeNull();
    }
}

public class LogindIdleBackendParsingTests
{
    [Theory]
    [InlineData("(<true>,)", true)]
    [InlineData("(<false>,)", false)]
    [InlineData("(<TRUE>,)", true)]
    public void ParseGdbusBool_ValidOutput_ReturnsCorrectValue(string output, bool expected)
    {
        LogindIdleBackend.ParseGdbusBool(output, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("()")]
    [InlineData("(something,)")]
    public void ParseGdbusBool_InvalidOutput_ReturnsFalse(string output)
    {
        LogindIdleBackend.ParseGdbusBool(output, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("(<uint64 1234567890>,)", 1234567890ul)]
    [InlineData("(<uint64 0>,)", 0ul)]
    public void ParseGdbusVariantUInt64_ValidOutput_ReturnsValue(string output, ulong expected)
    {
        LogindIdleBackend.ParseGdbusVariantUInt64(output).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("error")]
    public void ParseGdbusVariantUInt64_InvalidOutput_ReturnsNull(string output)
    {
        LogindIdleBackend.ParseGdbusVariantUInt64(output).ShouldBeNull();
    }

    [Fact]
    public void GetMonotonicTimeUs_ReturnsPositiveValue()
    {
        var result = LogindIdleBackend.GetMonotonicTimeUs();
        result.ShouldNotBeNull();
        result.Value.ShouldBeGreaterThan(0);
    }
}

public class LinuxIdleServiceEnvironmentTests
{
    [Fact]
    public void IsGnome_WithGnomeDesktop_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "ubuntu:GNOME");
            LinuxIdleService.IsGnome().ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", original);
        }
    }

    [Fact]
    public void IsGnome_WithKdeDesktop_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "KDE");
            LinuxIdleService.IsGnome().ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", original);
        }
    }

    [Fact]
    public void IsGnome_WithNoDesktop_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", null);
            LinuxIdleService.IsGnome().ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", original);
        }
    }

    [Fact]
    public void IsX11_WithX11Session_ReturnsTrue()
    {
        var original = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "x11");
            LinuxIdleService.IsX11().ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", original);
        }
    }

    [Fact]
    public void IsX11_WithWaylandSession_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "wayland");
            LinuxIdleService.IsX11().ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", original);
        }
    }

    [Fact]
    public void IsX11_WithNoSession_ReturnsFalse()
    {
        var original = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", null);
            LinuxIdleService.IsX11().ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", original);
        }
    }
}
