using System.Runtime.Versioning;
using HaPcRemote.Service.Models;
using Microsoft.Extensions.Logging;

namespace HaPcRemote.Service.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxAudioService(ICliRunner cliRunner, ILogger<LinuxAudioService> logger) : IAudioService
{
    public async Task<List<AudioDevice>> GetDevicesAsync()
    {
        // pactl list sinks short — columns: index, name, module, sample-spec, state
        var output = await cliRunner.RunAsync("pactl", ["list", "sinks", "short"]);
        var defaultSink = await GetDefaultSinkNameAsync();
        return ParseSinksShort(output, defaultSink);
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

        await cliRunner.RunAsync("pactl", ["set-default-sink", deviceName]);
    }

    public async Task SetVolumeAsync(int level)
    {
        var defaultSink = await GetDefaultSinkNameAsync();
        if (string.IsNullOrEmpty(defaultSink))
            throw new InvalidOperationException("No default audio device found.");

        await cliRunner.RunAsync("pactl", ["set-sink-volume", defaultSink, $"{level}%"]);
        await cliRunner.RunAsync("pactl", ["set-sink-mute", defaultSink, "0"]);
    }

    private async Task<string> GetDefaultSinkNameAsync()
    {
        try
        {
            var output = await cliRunner.RunAsync("pactl", ["get-default-sink"]);
            return output.Trim();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to get default sink");
            return string.Empty;
        }
    }

    internal static List<AudioDevice> ParseSinksShort(string output, string defaultSinkName)
    {
        // Output format (tab-separated): index  name  module  sample-spec  state
        var devices = new List<AudioDevice>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var columns = trimmed.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
                continue;

            var name = columns[1].Trim();
            if (string.IsNullOrEmpty(name))
                continue;

            var isDefault = string.Equals(name, defaultSinkName, StringComparison.OrdinalIgnoreCase);

            devices.Add(new AudioDevice
            {
                Name = name,
                IsDefault = isDefault,
                Volume = 0, // pactl list sinks short does not include volume; would need verbose output
                IsConnected = true // pactl only reports active (running/idle/suspended) sinks
            });
        }
        return devices;
    }
}
