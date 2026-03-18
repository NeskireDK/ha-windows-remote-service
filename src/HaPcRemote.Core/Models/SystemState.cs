namespace HaPcRemote.Service.Models;

public sealed class SystemState
{
    public AudioState? Audio { get; init; }
    public List<MonitorInfo>? Monitors { get; init; }
    public List<SteamGame>? SteamGames { get; init; }
    public SteamRunningGame? RunningGame { get; init; }
    public List<string>? Modes { get; init; }
    public int? IdleSeconds { get; init; }
    public SteamBindings? SteamBindings { get; init; }
    public bool? SteamReady { get; init; }
    public int? AutoSleepAfterMinutes { get; init; }
}

public sealed class AudioState
{
    public required List<AudioDevice> Devices { get; init; }
    public string? Current { get; init; }
    public int? Volume { get; init; }
}
