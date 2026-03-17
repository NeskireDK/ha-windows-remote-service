using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

public sealed class UpdateService(IHttpClientFactory httpClientFactory, ILogger<UpdateService> logger, bool includePrereleases = false) : IUpdateService
{
    private const string RepoOwner = "NeskireDK";
    private const string RepoName = "ha-pc-remote-service";
    private const string InstallerPrefix = "HaPcRemoteService-Setup-";

    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<UpdateResult> CheckAndApplyAsync(CancellationToken ct = default)
    {
        if (!_lock.Wait(0))
            return UpdateResult.Failed("Update already in progress");

        try
        {
            return await CheckAndApplyInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<UpdateResult> CheckAndApplyInternalAsync(CancellationToken ct)
    {
        var currentVersion = GetCurrentVersion();
        var currentVersionStr = currentVersion?.ToString(3) ?? "unknown";

        ReleaseInfo? release;
        try
        {
            release = await CheckForUpdateAsync(currentVersion, ct);
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
            return UpdateResult.UpToDate(currentVersionStr);

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

    private async Task<ReleaseInfo?> CheckForUpdateAsync(Version? currentVersion, CancellationToken ct)
    {
        using var client = CreateHttpClient();

        if (includePrereleases)
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
            var releases = await client.GetFromJsonAsync(url, UpdateJsonContext.Default.ListGitHubReleaseDto, ct);
            if (releases is null) return null;
            return FindBestRelease(releases, currentVersion);
        }
        else
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await client.GetFromJsonAsync(url, UpdateJsonContext.Default.GitHubReleaseDto, ct);
            if (release is null) return null;
            return FindBestRelease([release], currentVersion);
        }
    }

    private ReleaseInfo? FindBestRelease(List<GitHubReleaseDto> releases, Version? currentVersion)
    {
        foreach (var release in releases)
        {
            if (!includePrereleases && IsPrerelease(release.TagName))
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
        var infoVersion = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (infoVersion is null) return null;
        var versionPart = infoVersion.Split('+')[0];
        if (!Version.TryParse(versionPart, out var v)) return null;
        return v.Build < 0 ? new Version(v.Major, v.Minor, 0) : v;
    }

    private static Version? ParseVersion(string tag)
    {
        var cleaned = tag.TrimStart('v');
        // Strip prerelease suffix (e.g. "1.7.0-rc.1" → "1.7.0")
        var dashIdx = cleaned.IndexOf('-');
        if (dashIdx >= 0) cleaned = cleaned[..dashIdx];
        if (!Version.TryParse(cleaned, out var v)) return null;
        return v.Build < 0 ? new Version(v.Major, v.Minor, 0) : v;
    }

    private HttpClient CreateHttpClient()
    {
        var client = httpClientFactory.CreateClient("GitHubUpdate");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "HaPcRemote-Service");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return client;
    }

    private sealed record ReleaseInfo(string TagName, string InstallerUrl);
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
