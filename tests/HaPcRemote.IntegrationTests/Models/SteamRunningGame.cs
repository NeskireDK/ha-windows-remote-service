namespace HaPcRemote.IntegrationTests.Models;

public class SteamRunningGame
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ProcessId { get; set; }
}
