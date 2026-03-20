using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Handles artwork resolution, local cache lookup, and diagnostics for Steam games.
/// Extracted from SteamService to separate artwork concerns from game management.
/// </summary>
internal static class SteamArtworkService
{
    private static readonly string[] ArtworkExtensions = ["png", "jpg", "jpeg", "webp"];

    /// <summary>
    /// Finds the artwork file for a given appId. Priority:
    /// 1. Custom grid art: userdata/{steamid}/config/grid/{appId}p.{ext}
    /// 2. Library cache: appcache/librarycache/{appId}_library_600x900.{ext}
    /// 3. Library cache fallbacks: _library_hero, _header, _logo
    /// For official games only (appId > 0), callers may try CDN after this returns null.
    /// </summary>
    public static string? FindArtworkPath(string steamPath, string? steamUserId, int appId, ILogger? logger = null)
    {
        // For non-Steam shortcuts, use unsigned representation for filenames
        var fileId = SteamVdfParser.IsShortcutAppId(appId) ? ((uint)appId).ToString() : appId.ToString();
        logger?.LogDebug("Artwork: lookup appId={AppId} fileId={FileId} isShortcut={IsShortcut}",
            appId, fileId, SteamVdfParser.IsShortcutAppId(appId));

        // Priority 1: Custom grid art (user-set posters)
        if (steamUserId != null)
        {
            var gridDir = Path.Combine(steamPath, "userdata", steamUserId, "config", "grid");
            if (Directory.Exists(gridDir))
            {
                foreach (var ext in ArtworkExtensions)
                {
                    var path = Path.Combine(gridDir, $"{fileId}p.{ext}");
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        logger?.LogDebug("Artwork: found in custom grid {Path} ({Size} KB)", path, fi.Length / 1024);
                        return path;
                    }
                }
                logger?.LogDebug("Artwork: not found in custom grid {GridDir}", gridDir);
            }
            else
            {
                logger?.LogWarning("Artwork: custom grid dir missing {GridDir}", gridDir);
            }
        }
        else
        {
            logger?.LogDebug("Artwork: no steamUserId, skipping custom grid lookup");
        }

        // Priority 2: Steam local library cache (multiple filename variants)
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache");
        if (Directory.Exists(cacheDir))
        {
            // Steam caches artwork with these suffixes — try most useful first
            string[] libraryCacheSuffixes = ["_library_600x900", "_library_hero", "_header", "_logo"];
            foreach (var suffix in libraryCacheSuffixes)
            {
                foreach (var ext in ArtworkExtensions)
                {
                    var path = Path.Combine(cacheDir, $"{fileId}{suffix}.{ext}");
                    var fi = new FileInfo(path);
                    if (fi.Exists)
                    {
                        logger?.LogDebug("Artwork: found in library cache {Path} ({Size} KB)", path, fi.Length / 1024);
                        return path;
                    }
                }
            }
            logger?.LogDebug("Artwork: not found in library cache {CacheDir} (checked {Count} suffix variants)",
                cacheDir, libraryCacheSuffixes.Length);
        }
        else
        {
            logger?.LogWarning("Artwork: library cache dir missing {CacheDir}", cacheDir);
        }

        return null;
    }

    /// <summary>
    /// Returns diagnostic info about all artwork paths checked for a given appId.
    /// Does not download from CDN — purely local file checks.
    /// </summary>
    public static ArtworkDiagnostics GetArtworkDiagnostics(string steamPath, string? steamUserId, int appId, string gameName)
    {
        var fileId = SteamVdfParser.IsShortcutAppId(appId) ? ((uint)appId).ToString() : appId.ToString();
        var paths = new List<ArtworkPathCheck>();
        string? resolvedPath = null;

        // Priority 1: Custom grid art
        if (steamUserId != null)
        {
            var gridDir = Path.Combine(steamPath, "userdata", steamUserId, "config", "grid");
            foreach (var ext in ArtworkExtensions)
            {
                var path = Path.Combine(gridDir, $"{fileId}p.{ext}");
                var fii = new FileInfo(path);
                var exists = fii.Exists;
                long? size = exists ? fii.Length : null;
                paths.Add(new ArtworkPathCheck { Path = path, Category = "Custom Grid", Exists = exists, SizeBytes = size });
                if (exists && resolvedPath == null) resolvedPath = path;
            }
        }

        // Priority 2: Library cache
        var cacheDir = Path.Combine(steamPath, "appcache", "librarycache");
        string[] libraryCacheSuffixes = ["_library_600x900", "_library_hero", "_header", "_logo"];
        foreach (var suffix in libraryCacheSuffixes)
        {
            foreach (var ext in ArtworkExtensions)
            {
                var path = Path.Combine(cacheDir, $"{fileId}{suffix}.{ext}");
                var fii = new FileInfo(path);
                var exists = fii.Exists;
                long? size = exists ? fii.Length : null;
                paths.Add(new ArtworkPathCheck { Path = path, Category = $"Library Cache ({suffix})", Exists = exists, SizeBytes = size });
                if (exists && resolvedPath == null) resolvedPath = path;
            }
        }

        var cdnUrl = SteamVdfParser.IsShortcutAppId(appId)
            ? ""
            : $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg";

        return new ArtworkDiagnostics
        {
            AppId = appId,
            FileId = fileId,
            GameName = gameName,
            IsShortcut = SteamVdfParser.IsShortcutAppId(appId),
            ResolvedPath = resolvedPath,
            CdnUrl = cdnUrl,
            PathsChecked = paths
        };
    }
}
