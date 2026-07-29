using System.Diagnostics;
using System.Net.Sockets;

namespace TunnelDeck.Services;

/// <summary>Measures TCP connect latency to a server (a practical "ping" for VLESS/Reality over TCP).</summary>
public static class PingService
{
    /// <summary>Returns the TCP handshake time in ms, or -1 if unreachable within the timeout.</summary>
    public static async Task<int> TcpPingAsync(string host, int port, int timeoutMs = 2500)
    {
        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            var connect = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(timeoutMs));
            sw.Stop();
            if (done != connect || !client.Connected) return -1;
            await connect; // observe exceptions
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
