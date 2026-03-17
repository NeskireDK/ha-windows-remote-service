using System.Text.Json;
using System.Text.Json.Serialization;
using HaPcRemote.Shared.Configuration;

namespace HaPcRemote.Tray.Models;

internal sealed class TraySettings
{
    public bool AutoUpdate { get; set; } = false;
    public bool IncludePrereleases { get; set; } = false;
    public string LogLevel { get; set; } = "Warning";

    public int SettingsWidth { get; set; }
    public int SettingsHeight { get; set; }
    public int LogViewerWidth { get; set; }
    public int LogViewerHeight { get; set; }

    public static TraySettings Load()
    {
        try
        {
            var path = ConfigPaths.GetTraySettingsPath();
            if (!File.Exists(path)) return new TraySettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, TraySettingsJsonContext.Default.TraySettings)
                   ?? new TraySettings();
        }
        catch { return new TraySettings(); }
    }

    public void Save()
    {
        try
        {
            var path = ConfigPaths.GetTraySettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, TraySettingsJsonContext.Default.TraySettings));
        }
        catch { }
    }
}

[JsonSerializable(typeof(TraySettings))]
internal sealed partial class TraySettingsJsonContext : JsonSerializerContext { }
