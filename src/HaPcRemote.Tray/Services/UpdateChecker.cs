using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using HaPcRemote.Tray.Models;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Tray.Services;

internal sealed class UpdateChecker(ILogger<UpdateChecker> logger)
{
    private const string RepoOwner = "NeskireDK";
    private const string RepoName = "ha-pc-remote-service";
    private const string InstallerPrefix = "HaPcRemoteService-Setup-";

    private static readonly HttpClient Http = CreateHttpClient();

    public record ReleaseInfo(string TagName, string InstallerUrl);

    /// <summary>
    /// Checks GitHub for a newer release. Returns null if up-to-date or on error.
    /// </summary>
    public async Task<ReleaseInfo?> CheckAsync(bool includePrereleases = false, CancellationToken ct = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();

            if (includePrereleases)
            {
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
                var releases = await Http.GetFromJsonAsync(url, GitHubJsonContext.Default.ListGitHubRelease, ct);
                if (releases is null) return null;
                return FindBestRelease(releases, currentVersion, includePrereleases);
            }
            else
            {
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var release = await Http.GetFromJsonAsync(url, GitHubJsonContext.Default.GitHubRelease, ct);
                if (release is null) return null;
                return FindBestRelease([release], currentVersion, includePrereleases);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Update check skipped (network unavailable)");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed");
            return null;
        }
    }

    internal ReleaseInfo? FindBestRelease(List<GitHubRelease> releases, Version? currentVersion, bool includePrereleases)
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

    /// <summary>
    /// Downloads the installer to %TEMP% and launches it with /VERYSILENT.
    /// Returns true if the installer was launched.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"HaPcRemote-Update-{release.TagName}.exe");

            logger.LogInformation("Downloading update {Version}...", release.TagName);
            await using var stream = await Http.GetStreamAsync(release.InstallerUrl, ct);
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

            if (process == null) return false;

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
            {
                logger.LogError("Installer exited with code {Code}", process.ExitCode);
                return false;
            }

            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logger.LogWarning("UAC prompt was cancelled for update");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download or launch update");
            return false;
        }
    }

    internal static Version? GetCurrentVersion()
    {
        var infoVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
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
        // Normalize to 3-component so 0.7 == 0.7.0
        return v.Build < 0 ? new Version(v.Major, v.Minor, 0) : v;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "HaPcRemote-Tray");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return client;
    }
}
