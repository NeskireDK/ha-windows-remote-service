using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

public sealed class UpdateService(IHttpClientFactory httpClientFactory, ILogger<UpdateService> logger, Func<bool>? includePrereleases = null) : IUpdateService
{
    private const string RepoOwner = "NeskireDK";
    private const string RepoName = "ha-pc-remote-service";
    private const string InstallerPrefix = "HaPcRemoteService-Setup-";

    private readonly SemaphoreSlim _lock = new(1, 1);

    private bool IncludePrereleases => includePrereleases?.Invoke() ?? false;

    public async Task<UpdateResult> CheckAndApplyAsync(CancellationToken ct = default)
    {
        if (!_lock.Wait(0))
            return UpdateResult.Failed("Update already in progress");

        try
        {
            ReleaseInfo? release;
            try
            {
                release = await CheckAsync(ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogDebug(ex, "Update check skipped (network unavailable)");
                return UpdateResult.Failed("Network unavailable");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Update check failed");
                return UpdateResult.Failed("Update check failed");
            }

            if (release is null)
            {
                var v = GetCurrentVersion();
                return UpdateResult.UpToDate(v is not null ? FormatVersion(v) : "unknown");
            }
            return await ApplyAsync(release, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();
        var currentVersionStr = currentVersion is not null ? FormatVersion(currentVersion) : "unknown";
        var prerelease = IncludePrereleases;

        logger.LogInformation("Update check starting — current: {CurrentVersion}, prereleases: {IncludePrereleases}",
            currentVersionStr, prerelease);

        var release = await CheckForUpdateAsync(currentVersion, prerelease, ct);
        if (release is null)
            logger.LogDebug("No update found — already up to date at {CurrentVersion}", currentVersionStr);
        return release;
    }

    public async Task<UpdateResult> ApplyAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();
        var currentVersionStr = currentVersion is not null ? FormatVersion(currentVersion) : "unknown";

        try
        {
            var success = await DownloadAndInstallAsync(release, ct);
            if (success)
                return UpdateResult.UpdateStarted(currentVersionStr, release.TagName);
            return UpdateResult.Failed("Installer failed to launch");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download or install update");
            return UpdateResult.Failed("Download or install failed");
        }
    }

    private async Task<ReleaseInfo?> CheckForUpdateAsync(Version? currentVersion, bool prerelease, CancellationToken ct)
    {
        using var client = CreateHttpClient();

        if (prerelease)
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            logger.LogDebug("Fetching all releases from {Url}", url);
            var releases = await client.GetFromJsonAsync(url, UpdateJsonContext.Default.ListGitHubReleaseDto, ct);
            if (releases is null) return null;
            return FindBestRelease(releases, currentVersion, prerelease);
        }
        else
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            logger.LogDebug("Fetching latest stable release from {Url}", url);
            var release = await client.GetFromJsonAsync(url, UpdateJsonContext.Default.GitHubReleaseDto, ct);
            if (release is null) return null;
            return FindBestRelease([release], currentVersion, prerelease);
        }
    }

    private ReleaseInfo? FindBestRelease(List<GitHubReleaseDto> releases, Version? currentVersion, bool prerelease)
    {
        foreach (var release in releases)
        {
            if (!prerelease && IsPrerelease(release.TagName))
                continue;

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion is null || currentVersion is null || latestVersion <= currentVersion)
                continue;

            var installer = release.Assets?
                .FirstOrDefault(a => a.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                                  && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (installer is null) continue;

            logger.LogInformation("Update available: {CurrentVersion} -> {LatestVersion}", currentVersion, release.TagName);
            return new ReleaseInfo(release.TagName, installer.BrowserDownloadUrl);
        }

        return null;
    }

    internal static bool IsPrerelease(string tagName)
    {
        var cleaned = tagName.TrimStart('v');
        return cleaned.Contains('-');
    }

    private async Task<bool> DownloadAndInstallAsync(ReleaseInfo release, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"HaPcRemote-Update-{release.TagName}.exe");

        logger.LogInformation("Downloading update {Version}...", release.TagName);
        using var client = CreateHttpClient();
        await using var stream = await client.GetStreamAsync(release.InstallerUrl, ct);
        await using var file = File.Create(tempPath);
        await stream.CopyToAsync(file, ct);
        file.Close();

        logger.LogInformation("Launching installer from {Path}", tempPath);
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = tempPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
            UseShellExecute = true,
            Verb = "runas"
        });

        return process != null;
    }

    internal static Version? GetCurrentVersion()
    {
        var infoVersion = typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (infoVersion is null) return null;
        return ParseVersion(infoVersion.Split('+')[0]);
    }

    /// <summary>
    /// Parses version strings like "v1.7.0", "1.7.0-rc.3", "v1.7.0-rc.1".
    /// Prerelease suffix is encoded in the 4th component: "1.7.0-rc.3" → Version(1,7,0,3).
    /// Stable versions get 4th component = int.MaxValue so they sort higher than any RC.
    /// </summary>
    internal static Version? ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v');
        var dashIdx = cleaned.IndexOf('-');
        var prerelease = -1;

        if (dashIdx >= 0)
        {
            var suffix = cleaned[(dashIdx + 1)..];
            // Extract trailing number from prerelease suffix
            // Handles both "rc.3" (dot-separated) and "rc3" (concatenated) formats
            var dotIdx = suffix.LastIndexOf('.');
            if (dotIdx >= 0 && int.TryParse(suffix[(dotIdx + 1)..], out var n))
                prerelease = n;
            else if (TrailingDigits(suffix) is { } digits && int.TryParse(digits, out var m))
                prerelease = m;
            else
                prerelease = 0; // e.g. "beta" with no number

            cleaned = cleaned[..dashIdx];
        }

        if (!Version.TryParse(cleaned, out var v)) return null;
        var major = v.Major;
        var minor = v.Minor;
        var patch = v.Build < 0 ? 0 : v.Build;

        // Stable (no dash) → int.MaxValue so v1.7.0 > v1.7.0-rc.99
        var revision = prerelease < 0 ? int.MaxValue : prerelease;
        return new Version(major, minor, patch, revision);
    }

    internal static string FormatVersion(Version v) =>
        v.Revision == int.MaxValue
            ? v.ToString(3)
            : $"{v.Major}.{v.Minor}.{v.Build}-rc.{v.Revision}";

    private static string? TrailingDigits(string s)
    {
        var i = s.Length;
        while (i > 0 && char.IsAsciiDigit(s[i - 1])) i--;
        return i < s.Length ? s[i..] : null;
    }

    private HttpClient CreateHttpClient()
    {
        var client = httpClientFactory.CreateClient("GitHubUpdate");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "HaPcRemote-Service");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return client;
    }

}

// Internal DTOs for GitHub API — separate from Tray models
internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubAssetDto>? Assets { get; set; }
}

internal sealed class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}

[JsonSerializable(typeof(GitHubReleaseDto))]
[JsonSerializable(typeof(List<GitHubReleaseDto>))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
