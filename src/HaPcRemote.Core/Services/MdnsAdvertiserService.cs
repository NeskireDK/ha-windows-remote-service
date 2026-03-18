using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using HaPcRemote.Service.Configuration;
using Microsoft.Extensions.Options;

namespace HaPcRemote.Service.Services;

/// <summary>
/// Lightweight mDNS service advertiser. Responds to DNS-SD queries for
/// _pc-remote._tcp.local. with SRV, TXT, and A records.
/// No external dependencies - fully Native AOT compatible.
/// </summary>
public sealed class MdnsAdvertiserService(IOptionsMonitor<PcRemoteOptions> options, ILogger<MdnsAdvertiserService> logger) : IHostedService, IDisposable
{
    private const int MdnsPort = 5353;
    private const string ServiceType = "_pc-remote._tcp.local.";
    private const string DnsSdQuery = "_services._dns-sd._udp.local.";

    private static readonly IPAddress MdnsMulticastAddress = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint MdnsEndpoint = new(MdnsMulticastAddress, MdnsPort);

    private readonly string _hostname = GetHostname();
    private readonly string _instanceName = $"{Environment.MachineName}._pc-remote._tcp.local.";
    private readonly Dictionary<string, string> _txtRecords = new()
    {
        ["txtvers"] = "1",
        ["version"] = GetVersion(),
        ["machine_name"] = Environment.MachineName
    };

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _respondPending;
    private DateTime _lastResponseTime;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _udpClient = CreateUdpClient();
            _listenTask = ListenAsync(_cts.Token);
            logger.LogInformation("mDNS advertiser started: {Instance} on port {Port}", _instanceName, options.CurrentValue.Port);

            // Send an initial unsolicited announcement
            _ = Task.Run(() => SendAnnouncementAsync(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to start mDNS advertiser. Discovery will not be available");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("mDNS advertiser stopping");

        // Send goodbye packet (TTL=0) before shutting down per RFC 6762 §10.1
        if (_udpClient is not null)
        {
            try
            {
                var goodbye = BuildGoodbyePacket();
                await _udpClient.SendAsync(goodbye, goodbye.Length, MdnsEndpoint);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to send mDNS goodbye packet");
            }
        }

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_listenTask is not null)
        {
            try
            {
                await _listenTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        if (_udpClient is not null)
        {
            try { _udpClient.DropMulticastGroup(MdnsMulticastAddress); } catch { /* best effort */ }
            _udpClient.Close();
        }
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _udpClient?.Dispose();
    }

    private UdpClient CreateUdpClient()
    {
        var client = new UdpClient();
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsPort));
        client.JoinMulticastGroup(MdnsMulticastAddress);
        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        return client;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient!.ReceiveAsync(ct);
                ProcessQuery(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error receiving mDNS packet");
            }
        }
    }

    private void ProcessQuery(byte[] data)
    {
        // Minimal DNS header parsing: ID(2) + Flags(2) + QDCount(2) + ANCount(2) + NSCount(2) + ARCount(2)
        if (data.Length < 12) return;

        var flags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2));
        var isQuery = (flags & 0x8000) == 0; // QR bit = 0 means query
        if (!isQuery) return;

        var qdCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var offset = 12;

        for (var i = 0; i < qdCount && offset < data.Length; i++)
        {
            var name = ReadDnsName(data, ref offset);
            if (offset + 4 > data.Length) break;

            var qtype = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
            offset += 2;
            // qclass
            offset += 2;

            if (name.Equals(ServiceType, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(DnsSdQuery, StringComparison.OrdinalIgnoreCase))
            {
                _ = SendResponseAsync();
                return;
            }

            if (name.Equals(_instanceName, StringComparison.OrdinalIgnoreCase))
            {
                _ = SendResponseAsync();
                return;
            }
        }
    }

    private async Task SendAnnouncementAsync()
    {
        try
        {
            // Send the announcement a few times with small delays for reliability
            for (var i = 0; i < 3; i++)
            {
                await SendResponseAsync();
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error during mDNS announcement");
        }
    }

    private async Task SendResponseAsync()
    {
        if (Interlocked.CompareExchange(ref _respondPending, 1, 0) != 0)
            return;

        try
        {
            if ((DateTime.UtcNow - _lastResponseTime).TotalSeconds < 1)
                return;

            var packet = BuildResponsePacket();
            await _udpClient!.SendAsync(packet, packet.Length, MdnsEndpoint);
            _lastResponseTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to send mDNS response");
        }
        finally
        {
            Interlocked.Exchange(ref _respondPending, 0);
        }
    }

    private byte[] BuildResponsePacket() => BuildPacket(4500, 120, 4500, 120);

    private byte[] BuildGoodbyePacket() => BuildPacket(0, 0, 0, 0);

    private byte[] BuildPacket(uint ptrTtl, uint srvTtl, uint txtTtl, uint aTtl)
    {
        using var ms = new MemoryStream(512);
        using var writer = new BinaryWriter(ms);

        // DNS Header
        writer.Write((ushort)0); // Transaction ID
        WriteBigEndian(writer, 0x8400); // Flags: Response + Authoritative
        WriteBigEndian(writer, 0); // Questions
        WriteBigEndian(writer, 4); // Answer count: PTR + SRV + TXT + A
        WriteBigEndian(writer, 0); // Authority
        WriteBigEndian(writer, 0); // Additional

        // PTR record: _pc-remote._tcp.local. -> instance (shared record, no cache-flush)
        WriteDnsName(writer, ServiceType);
        WriteBigEndian(writer, 12); // Type PTR
        WriteBigEndian(writer, 0x0001); // Class IN (no cache-flush — PTR is a shared record per RFC 6762 §10.2)
        WriteBigEndian32(writer, ptrTtl);
        var ptrData = EncodeDnsName(_instanceName);
        WriteBigEndian(writer, (ushort)ptrData.Length);
        writer.Write(ptrData);

        // SRV record: instance -> hostname:port
        WriteDnsName(writer, _instanceName);
        WriteBigEndian(writer, 33); // Type SRV
        WriteBigEndian(writer, 0x8001); // Class IN + cache flush
        WriteBigEndian32(writer, srvTtl);
        var srvTarget = EncodeDnsName(_hostname);
        WriteBigEndian(writer, (ushort)(6 + srvTarget.Length));
        WriteBigEndian(writer, 0); // Priority
        WriteBigEndian(writer, 0); // Weight
        WriteBigEndian(writer, (ushort)options.CurrentValue.Port); // Port
        writer.Write(srvTarget);

        // TXT record
        WriteDnsName(writer, _instanceName);
        WriteBigEndian(writer, 16); // Type TXT
        WriteBigEndian(writer, 0x8001); // Class IN + cache flush
        WriteBigEndian32(writer, txtTtl);
        var txtData = EncodeTxtRecords();
        WriteBigEndian(writer, (ushort)txtData.Length);
        writer.Write(txtData);

        // A record: hostname -> IP address
        var ipAddress = GetLocalIpAddress();
        WriteDnsName(writer, _hostname);
        WriteBigEndian(writer, 1); // Type A
        WriteBigEndian(writer, 0x8001); // Class IN + cache flush
        WriteBigEndian32(writer, aTtl);
        WriteBigEndian(writer, 4);
        writer.Write(ipAddress.GetAddressBytes());

        return ms.ToArray();
    }

    private byte[] EncodeTxtRecords()
    {
        using var ms = new MemoryStream();
        foreach (var (key, value) in _txtRecords)
        {
            var entry = Encoding.UTF8.GetBytes($"{key}={value}");
            ms.WriteByte((byte)entry.Length);
            ms.Write(entry);
        }
        return ms.ToArray();
    }

    private static void WriteDnsName(BinaryWriter writer, string name)
    {
        writer.Write(EncodeDnsName(name));
    }

    private static byte[] EncodeDnsName(string name)
    {
        using var ms = new MemoryStream();
        var labels = name.TrimEnd('.').Split('.');
        foreach (var label in labels)
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0); // Root label
        return ms.ToArray();
    }

    private static string ReadDnsName(byte[] data, ref int offset)
    {
        var sb = new StringBuilder();
        var jumped = false;
        var savedOffset = 0;
        var hops = 0;
        const int maxHops = 10;

        while (offset < data.Length)
        {
            var len = data[offset];
            if (len == 0)
            {
                offset++;
                break;
            }

            // DNS name compression pointer
            if ((len & 0xC0) == 0xC0)
            {
                if (++hops > maxHops) break; // guard against malformed pointer loops
                if (!jumped)
                {
                    savedOffset = offset + 2;
                }
                if (offset + 1 >= data.Length) break;
                offset = ((len & 0x3F) << 8) | data[offset + 1];
                jumped = true;
                continue;
            }

            offset++;
            if (offset + len > data.Length) break;

            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(data, offset, len));
            offset += len;
        }

        if (jumped) offset = savedOffset;
        sb.Append('.');
        return sb.ToString();
    }

    private static void WriteBigEndian(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteBigEndian32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)((value >> 16) & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)(value & 0xFF));
    }

    private static IPAddress GetLocalIpAddress()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;

            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address;
            }
        }

        return IPAddress.Loopback;
    }

    private static string GetHostname()
    {
        var name = Environment.MachineName.ToLowerInvariant();
        return $"{name}.local.";
    }

    private static string GetVersion()
    {
        return typeof(MdnsAdvertiserService).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
            ?? "0.0.0";
    }
}
