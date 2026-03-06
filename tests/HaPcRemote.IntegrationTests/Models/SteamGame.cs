namespace HaPcRemote.IntegrationTests.Models;

public class SteamGame
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long LastPlayed { get; set; }
    public bool IsShortcut { get; set; }
    public string? ExePath { get; set; }
    public string? LaunchOptions { get; set; }
}
