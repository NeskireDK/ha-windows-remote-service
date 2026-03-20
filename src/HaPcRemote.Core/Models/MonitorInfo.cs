namespace HaPcRemote.Service.Models;

public sealed class MonitorInfo
{
    public required string Name { get; init; }
    public required string MonitorId { get; init; }
    public string? SerialNumber { get; init; }
    public required string MonitorName { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int DisplayFrequency { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsPrimary { get; init; }
    public bool HasSavedLayout { get; init; }
}
