using ValveKeyValue;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Resolves the active Steam user ID from loginusers.vdf or the userdata directory.
/// </summary>
internal static class SteamUserIdResolver
{
    /// <summary>
    /// Returns the Steam3 user ID (the numeric folder name under userdata/).
    /// Tries loginusers.vdf first (MostRecent=1), falls back to first userdata/ subfolder.
    /// </summary>
    internal static string? Resolve(string steamPath)
    {
        var loginUsersPath = Path.Combine(steamPath, "config", "loginusers.vdf");
        if (File.Exists(loginUsersPath))
        {
            try
            {
                var userId = ParseMostRecentUser(File.ReadAllText(loginUsersPath));
                if (userId != null)
                    return userId;
            }
            catch (Exception)
            {
                // Fall through to directory scan
            }
        }

        // Fallback: pick the first numeric directory in userdata/
        var userDataDir = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userDataDir))
            return null;

        foreach (var dir in Directory.EnumerateDirectories(userDataDir))
        {
            var name = Path.GetFileName(dir);
            if (name != null && name.All(char.IsDigit) && name != "0")
                return name;
        }

        return null;
    }

    /// <summary>
    /// Parses loginusers.vdf and returns the Steam3 ID of the most recently logged-in user.
    /// </summary>
    internal static string? ParseMostRecentUser(string vdfContent)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(vdfContent));
        var kv = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
        var data = kv.Deserialize(stream);

        // loginusers.vdf has Steam64 IDs as keys with a "mostrecent" child.
        // We need the Steam3 ID (userdata folder) — convert: steam3 = steam64 - 76561197960265728
        // But actually, some users just have the steam3 ID in userdata/. Let's find the
        // MostRecent user's Steam64 ID and convert it.
        string? mostRecentSteam64 = null;
        string? firstSteam64 = null;

        foreach (var user in data)
        {
            firstSteam64 ??= user.Name;
            var mostRecent = user["MostRecent"]?.ToString()
                          ?? user["mostrecent"]?.ToString();
            if (mostRecent == "1")
            {
                mostRecentSteam64 = user.Name;
                break;
            }
        }

        var steam64Str = mostRecentSteam64 ?? firstSteam64;
        if (steam64Str == null || !long.TryParse(steam64Str, out var steam64))
            return null;

        // Convert Steam64 ID to Steam3 account ID
        const long steam64Offset = 76561197960265728;
        var steam3 = steam64 - steam64Offset;
        return steam3 > 0 ? steam3.ToString() : null;
    }
}
