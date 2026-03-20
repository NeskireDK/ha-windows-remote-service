using HaPcRemote.Service.Configuration;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

public class AudioService(IOptionsMonitor<PcRemoteOptions> options, ICliRunner cliRunner) : IAudioService
{
    public async Task<List<AudioDevice>> GetDevicesAsync()
    {
        var output = await cliRunner.RunAsync(GetExePath(), ["/scomma", "", "/Columns", "Type,Name,Direction,Default,Volume Percent,Status"]);
        return ParseCsvOutput(output);
    }

    public async Task<AudioDevice?> GetCurrentDeviceAsync()
    {
        var devices = await GetDevicesAsync();
        return devices.Find(d => d.IsDefault);
    }

    public async Task SetDefaultDeviceAsync(string deviceName)
    {
        var devices = await GetDevicesAsync();
        if (!devices.Exists(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase)))
            throw new KeyNotFoundException($"Audio device '{deviceName}' not found.");

        await cliRunner.RunAsync(GetExePath(), ["/SetDefault", deviceName, "1"]);
    }

    public async Task SetVolumeAsync(int level)
    {
        var current = await GetCurrentDeviceAsync()
            ?? throw new InvalidOperationException("No default audio device found.");

        await cliRunner.RunAsync(GetExePath(), ["/SetVolume", current.Name, level.ToString()]);
        await cliRunner.RunAsync(GetExePath(), ["/Unmute", current.Name]);
    }

    private string GetExePath() =>
        Path.Combine(options.CurrentValue.ToolsPath, "SoundVolumeView.exe");

    internal static List<AudioDevice> ParseCsvOutput(string csvOutput)
    {
        var devices = new List<AudioDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in csvOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var columns = CsvParser.SplitCsvLine(trimmed);
            if (columns.Count < 5)
                continue;

            // Columns selected via /Columns flag:
            // [0] Type, [1] Name, [2] Direction, [3] Default (Console), [4] Volume Percent, [5] Status

            // Only include hardware sound card devices; exclude Application/Subunit entries
            // (virtual audio devices created by apps, software mixers, etc.)
            if (!string.Equals(columns[0], "Device", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(columns[2], "Render", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!seen.Add(columns[1]))
                continue;

            var status = columns.Count > 5 ? columns[5].Trim() : string.Empty;
            devices.Add(new AudioDevice
            {
                Name = columns[1],
                IsDefault = string.Equals(columns[3], "Render", StringComparison.OrdinalIgnoreCase),
                Volume = ParseVolumePercent(columns[4]),
                IsConnected = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)
            });
        }
        return devices;
    }

    private static int ParseVolumePercent(string value)
    {
        // Value comes as "50.0%" — strip the % and parse
        var cleaned = value.Trim().TrimEnd('%');
        return double.TryParse(cleaned, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? (int)Math.Round(d)
            : 0;
    }

}
