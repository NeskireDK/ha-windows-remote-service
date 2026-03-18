using System.Text.Json;
using System.Text.Json.Nodes;
using HaPcRemote.Service.Configuration;

namespace HaPcRemote.Service.Services;

public sealed class ConfigurationWriter(string configPath) : IConfigurationWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null // PascalCase to match appsettings convention
    };

    private readonly Lock _lock = new();

    public PcRemoteOptions Read()
    {
        lock (_lock)
        {
            var root = ReadJsonRoot();
            var section = root[PcRemoteOptions.SectionName];
            if (section is null)
                return new PcRemoteOptions();

            return section.Deserialize<PcRemoteOptions>(JsonOptions) ?? new PcRemoteOptions();
        }
    }

    public void Write(PcRemoteOptions options)
    {
        lock (_lock)
        {
            var root = ReadJsonRoot();
            root[PcRemoteOptions.SectionName] = JsonSerializer.SerializeToNode(options, JsonOptions);
            WriteJsonRoot(root);
        }
    }

    public void SaveMode(string name, ModeConfig mode)
        => ModifyAndWrite(o => o.Modes[name] = mode);

    public void DeleteMode(string name)
        => ModifyAndWrite(o => o.Modes.Remove(name));

    public void RenameMode(string oldName, string newName, ModeConfig mode)
        => ModifyAndWrite(o => { o.Modes.Remove(oldName); o.Modes[newName] = mode; });

    public void SavePowerSettings(PowerSettings settings)
        => ModifyAndWrite(o => o.Power = settings);

    public void SavePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1024);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ModifyAndWrite(o => o.Port = port);
    }

    public void SaveSteamBindings(SteamConfig steam)
        => ModifyAndWrite(o => o.Steam = steam);

    public void SaveApp(string key, AppDefinitionOptions app)
        => ModifyAndWrite(o => o.Apps[key] = app);


    private void ModifyAndWrite(Action<PcRemoteOptions> modifier)
    {
        lock (_lock)
        {
            var root = ReadJsonRoot();
            var section = root[PcRemoteOptions.SectionName];
            var options = section?.Deserialize<PcRemoteOptions>(JsonOptions) ?? new PcRemoteOptions();
            modifier(options);
            root[PcRemoteOptions.SectionName] = JsonSerializer.SerializeToNode(options, JsonOptions);
            WriteJsonRoot(root);
        }
    }

    private JsonObject ReadJsonRoot()
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        var json = File.ReadAllText(configPath);
        return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
    }

    private void WriteJsonRoot(JsonObject root)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(configPath, root.ToJsonString(JsonOptions));
    }
}
