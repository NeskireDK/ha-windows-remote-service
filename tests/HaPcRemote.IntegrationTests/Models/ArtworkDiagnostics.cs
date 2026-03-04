namespace HaPcRemote.IntegrationTests.Models;

public class ArtworkPathCheck
{
    public string Path { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long? SizeBytes { get; set; }
}

public class ArtworkDiagnostics
{
    public int AppId { get; set; }
    public string FileId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public bool IsShortcut { get; set; }
    public string? ResolvedPath { get; set; }
    public string CdnUrl { get; set; } = string.Empty;
    public List<ArtworkPathCheck> PathsChecked { get; set; } = new();
}
