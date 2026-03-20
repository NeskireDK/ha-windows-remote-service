namespace HaPcRemote.Service.Models;

public sealed class AudioDevice
{
    public required string Name { get; init; }
    public required int Volume { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsConnected { get; init; }
}
