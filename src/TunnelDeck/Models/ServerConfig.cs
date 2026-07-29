namespace TunnelDeck.Models;

/// <summary>
/// A single VPN server parsed from a VLESS subscription URI.
/// Field set is focused on VLESS + Reality/TLS over TCP (what Happ-style
/// subscriptions ship), with basic ws/grpc transport support.
/// </summary>
public sealed class ServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display name (from the URI fragment, e.g. "NL-1").</summary>
    public string Name { get; set; } = "Server";

    /// <summary>Outbound protocol: vless | vmess | trojan | shadowsocks.</summary>
    public string Protocol { get; set; } = "vless";

    /// <summary>Password (trojan / shadowsocks).</summary>
    public string Password { get; set; } = "";

    /// <summary>Encryption method (shadowsocks), e.g. aes-256-gcm.</summary>
    public string Method { get; set; } = "";

    /// <summary>vmess alterId (usually 0) and cipher (auto).</summary>
    public int AlterId { get; set; } = 0;
    public string VmessSecurity { get; set; } = "auto";

    public string Server { get; set; } = "";
    public int Port { get; set; } = 443;
    public string Uuid { get; set; } = "";

    /// <summary>xtls-rprx-vision, or empty.</summary>
    public string Flow { get; set; } = "";

    /// <summary>reality | tls | none</summary>
    public string Security { get; set; } = "none";

    public string Sni { get; set; } = "";

    /// <summary>uTLS fingerprint (chrome, firefox, safari, ...). Defaults to chrome.</summary>
    public string Fingerprint { get; set; } = "chrome";

    // Reality-specific
    public string PublicKey { get; set; } = "";
    public string ShortId { get; set; } = "";

    // Transport: tcp | ws | grpc
    public string Transport { get; set; } = "tcp";
    public string WsPath { get; set; } = "";
    public string WsHost { get; set; } = "";
    public string GrpcServiceName { get; set; } = "";

    /// <summary>Original vless:// URI, kept for round-tripping / debugging.</summary>
    public string RawUri { get; set; } = "";

    public string Endpoint => $"{Server}:{Port}";
}
