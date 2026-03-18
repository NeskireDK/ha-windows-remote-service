using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;
using ValveKeyValue;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Pure static methods for parsing Steam VDF files and computing shortcut IDs.
/// Extracted from SteamService to separate parsing concerns from runtime orchestration.
/// </summary>
internal static class SteamVdfParser
{
    public static List<string> ParseLibraryFolders(string vdfContent)
    {
        var paths = new List<string>();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(vdfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);

        foreach (var folder in data)
        {
            var path = folder["path"]?.ToString();
            if (!string.IsNullOrEmpty(path))
                paths.Add(path.Replace(@"\\", @"\"));
        }

        return paths;
    }

    public static SteamGame? ParseAppManifest(string acfContent)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(acfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);

        var appIdStr = data["appid"]?.ToString();
        var name = data["name"]?.ToString();
        var lastPlayedStr = data["LastPlayed"]?.ToString();
        var lastUpdatedStr = data["LastUpdated"]?.ToString();

        if (string.IsNullOrEmpty(appIdStr) || string.IsNullOrEmpty(name))
            return null;

        if (!int.TryParse(appIdStr, out var appId))
            return null;

        // Prefer LastPlayed (actual play history); fall back to LastUpdated (install/update time)
        if (!long.TryParse(lastPlayedStr, out var lastPlayed))
            long.TryParse(lastUpdatedStr, out lastPlayed);

        return new SteamGame { AppId = appId, Name = name, LastPlayed = lastPlayed };
    }

    public static string? ParseInstallDir(string acfContent)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(acfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);
        return data["installdir"]?.ToString();
    }

    public static List<SteamGame> ParseShortcuts(Stream stream, ILogger? logger = null)
    {
        var shortcuts = new List<SteamGame>();
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Binary);
        KVObject root;
        try
        {
            root = kv.Deserialize(stream);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Shortcut parsing: failed to deserialize shortcuts.vdf");
            return shortcuts;
        }

        foreach (var entry in root)
        {
            var appName = entry["AppName"]?.ToString()
                          ?? entry["appname"]?.ToString();
            if (string.IsNullOrEmpty(appName))
                continue;

            var rawExe = entry["Exe"]?.ToString() ?? entry["exe"]?.ToString();
            logger?.LogDebug("Shortcut parsing [{Name}]: raw Exe field = {RawExe}", appName, rawExe ?? "(null)");

            var (exe, exeArgs) = ParseExeField(rawExe);
            logger?.LogDebug("Shortcut parsing [{Name}]: parsed ExePath={ExePath}, ExeArgs={ExeArgs}",
                appName, exe ?? "(null)", exeArgs ?? "(null)");

            // Steam stores shortcut appid as a signed 32-bit int (high bit set).
            // Try appid first, then fallback to calculating from exe+appname.
            int appId;
            var appIdStr = entry["appid"]?.ToString();
            if (!string.IsNullOrEmpty(appIdStr) && int.TryParse(appIdStr, out var parsed) && parsed != 0)
            {
                appId = parsed;
            }
            else
            {
                // Fallback: generate the shortcut appid from exe + appname
                appId = GenerateShortcutAppId(exe ?? "", appName);
            }

            var launchOptions = entry["LaunchOptions"]?.ToString()
                                ?? entry["launchoptions"]?.ToString();

            // If the Exe field contained arguments and LaunchOptions is empty, use the extracted args
            if (string.IsNullOrEmpty(launchOptions) && !string.IsNullOrEmpty(exeArgs))
            {
                launchOptions = exeArgs;
                logger?.LogDebug("Shortcut parsing [{Name}]: promoted exe args to LaunchOptions={LaunchOptions}",
                    appName, launchOptions);
            }

            var lastPlayedStr = entry["LastPlayTime"]?.ToString();
            long.TryParse(lastPlayedStr, out var lastPlayed);

            logger?.LogDebug("Shortcut parsing [{Name}]: AppId={AppId}, ExePath={ExePath}, LaunchOptions={LaunchOptions}",
                appName, appId, exe ?? "(null)", launchOptions ?? "(null)");

            shortcuts.Add(new SteamGame
            {
                AppId = appId,
                Name = appName,
                LastPlayed = lastPlayed,
                IsShortcut = true,
                ExePath = string.IsNullOrEmpty(exe) ? null : exe,
                LaunchOptions = string.IsNullOrEmpty(launchOptions) ? null : launchOptions
            });
        }

        return shortcuts;
    }

    /// <summary>
    /// Parses the Exe field from shortcuts.vdf. Steam stores this as a quoted path
    /// optionally followed by arguments, e.g.: "D:\emulator\emu.exe" -g "D:\games\rom.bin"
    /// Returns (exePath, args) where args may be null.
    /// </summary>
    public static (string? ExePath, string? Args) ParseExeField(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (null, null);

        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, null);

        // Case 1: Quoted path — extract path between first pair of quotes, rest is args
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var exePath = trimmed[1..closingQuote];
                var remainder = trimmed[(closingQuote + 1)..].Trim();
                return (exePath, string.IsNullOrEmpty(remainder) ? null : remainder);
            }

            // Malformed: opening quote but no closing — strip quotes and return as-is
            return (trimmed.Trim('"'), null);
        }

        // Case 2: Unquoted path — split on first space (if path doesn't contain spaces)
        // But prefer checking if the whole string is a valid file path first
        if (File.Exists(trimmed))
            return (trimmed, null);

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            var candidate = trimmed[..spaceIdx];
            var remainder = trimmed[(spaceIdx + 1)..].Trim();
            return (candidate, string.IsNullOrEmpty(remainder) ? null : remainder);
        }

        return (trimmed, null);
    }

    /// <summary>
    /// Shortcut appids are generated by Steam from exe+appname and are always negative when
    /// stored as a signed 32-bit int (high bit set).
    /// </summary>
    public static bool IsShortcutAppId(int appId) => appId < 0;

    /// <summary>
    /// Generates a non-Steam shortcut appid using the same CRC algorithm Steam uses.
    /// The result is always negative as a signed int32 (high bit set).
    /// </summary>
    public static int GenerateShortcutAppId(string exe, string appName)
    {
        // Steam algorithm: CRC32(exe + appname) | 0x80000000
        var input = System.Text.Encoding.UTF8.GetBytes(exe + appName);
        var crc = Crc32(input);
        return (int)(crc | 0x80000000);
    }

    private static uint Crc32(byte[] data)
    {
        const uint polynomial = 0xEDB88320;
        var crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
