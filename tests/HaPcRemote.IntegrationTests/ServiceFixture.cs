using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace HaPcRemote.IntegrationTests;

public class ServiceFixture : IAsyncLifetime
{
    private const int WolDurationSeconds = 20;
    private const int WolIntervalMs = 1000;
    private const int PollIntervalMs = 5000;
    private const int PollTimeoutMs = 180_000; // 3 minutes

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.IntegrationTests.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var baseUrl = Environment.GetEnvironmentVariable("PCREMOTE_BASE_URL")
                      ?? config["ServiceBaseUrl"]
                      ?? throw new InvalidOperationException("ServiceBaseUrl not configured");

        var macAddress = Environment.GetEnvironmentVariable("PCREMOTE_MAC_ADDRESS")
                         ?? config["MacAddress"];

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };

        if (await IsHealthy(client))
            return;

        if (string.IsNullOrWhiteSpace(macAddress))
            throw new InvalidOperationException(
                "Service is offline and no MacAddress configured for WoL. " +
                "Set MacAddress in appsettings.IntegrationTests.json or PCREMOTE_MAC_ADDRESS env var.");

        // Send WoL packets
        var mac = PhysicalAddress.Parse(macAddress.Replace(":", "-").Replace(".", "-"));
        var endTime = DateTime.UtcNow.AddSeconds(WolDurationSeconds);
        while (DateTime.UtcNow < endTime)
        {
            await SendWolPacket(mac);
            await Task.Delay(WolIntervalMs);
        }

        // Poll until healthy
        var deadline = DateTime.UtcNow.AddMilliseconds(PollTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await IsHealthy(client))
                return;
            await Task.Delay(PollIntervalMs);
        }

        throw new TimeoutException($"Service at {baseUrl} did not come online within {PollTimeoutMs / 1000}s after WoL.");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<bool> IsHealthy(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync("/api/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task SendWolPacket(PhysicalAddress mac)
    {
        var macBytes = mac.GetAddressBytes();
        var packet = new byte[102];

        // 6 bytes of 0xFF
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;

        // 16 repetitions of the MAC address
        for (var i = 0; i < 16; i++)
            Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        await udp.SendAsync(packet, packet.Length, "255.255.255.255", 9);
    }
}

[CollectionDefinition("Service")]
public class ServiceCollection : ICollectionFixture<ServiceFixture>;
