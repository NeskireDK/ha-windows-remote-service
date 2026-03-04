namespace HaPcRemote.Service.Models;

public sealed class ArtworkPathCheck
{
    public required string Path { get; init; }
    public required string Category { get; init; }
    public bool Exists { get; init; }
    public long? SizeBytes { get; init; }
}

public sealed class ArtworkDiagnostics
{
    public required int AppId { get; init; }
    public required string FileId { get; init; }
    public required string GameName { get; init; }
    public bool IsShortcut { get; init; }
    public string? ResolvedPath { get; init; }
    public string CdnUrl { get; init; } = "";
    public required List<ArtworkPathCheck> PathsChecked { get; init; }
}
