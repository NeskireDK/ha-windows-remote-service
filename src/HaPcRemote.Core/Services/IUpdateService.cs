using System.Text.Json.Serialization;

namespace HaPcRemote.Service.Services;

public interface IUpdateService
{
    /// <summary>
    /// Checks for a newer release on GitHub and installs it if available.
    /// Returns a result indicating what happened.
    /// </summary>
    Task<UpdateResult> CheckAndApplyAsync(CancellationToken ct = default);

    /// <summary>Check for a newer release without installing. Returns null if up-to-date.</summary>
    Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default);

    /// <summary>Download and install a previously found release.</summary>
    Task<UpdateResult> ApplyAsync(ReleaseInfo release, CancellationToken ct = default);
}

public sealed record ReleaseInfo(string TagName, string InstallerUrl);

public sealed class UpdateResult
{
    public UpdateStatus Status { get; init; }
    public string? CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? Message { get; init; }

    public static UpdateResult UpToDate(string version) => new()
    {
        Status = UpdateStatus.UpToDate,
        CurrentVersion = version,
        Message = "Already up to date"
    };

    public static UpdateResult UpdateStarted(string current, string latest) => new()
    {
        Status = UpdateStatus.UpdateStarted,
        CurrentVersion = current,
        LatestVersion = latest,
        Message = $"Update from {current} to {latest} started"
    };

    public static UpdateResult Failed(string message) => new()
    {
        Status = UpdateStatus.Failed,
        Message = message
    };
}

[JsonConverter(typeof(JsonStringEnumConverter<UpdateStatus>))]
public enum UpdateStatus
{
    UpToDate,
    UpdateStarted,
    Failed
}
