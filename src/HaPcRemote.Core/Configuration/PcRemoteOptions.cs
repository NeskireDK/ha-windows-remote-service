namespace HaPcRemote.Service.Configuration;

public sealed class PcRemoteOptions
{
    public const string SectionName = "PcRemote";

    public int Port { get; set; } = 5000;
    public AuthOptions Auth { get; set; } = new();
    public string ToolsPath { get; set; } = "./tools";

    public Dictionary<string, AppDefinitionOptions> Apps { get; set; } = new();
    public Dictionary<string, ModeConfig> Modes { get; set; } = new();
    public PowerSettings Power { get; set; } = new();
    public SteamConfig Steam { get; set; } = new();
}

public sealed class PowerSettings
{
    /// <summary>Minutes of total inactivity (no game + no input) before sleep. 0 = disabled.</summary>
    public int AutoSleepAfterMinutes { get; set; } = 0;
}

public sealed class AuthOptions
{
    public bool Enabled { get; set; } = true;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class SteamConfig
{
    public string DefaultPcMode { get; set; } = string.Empty;
    public Dictionary<string, string> GamePcModeBindings { get; set; } = new();
}

public sealed class ModeConfig
{
    public string? AudioDevice { get; set; }
    public string? SoloMonitor { get; set; }
    public int? Volume { get; set; }
    public string? LaunchApp { get; set; }
    public string? KillApp { get; set; }
    public int? KillToLaunchDelayMs { get; set; }
    public int? PostLaunchDelayMs { get; set; }
}
