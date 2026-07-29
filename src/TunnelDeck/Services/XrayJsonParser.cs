using System.Text.Json;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Parses the Xray/V2Ray JSON subscription format returned by Remnawave-style
/// panels (the body is a JSON array, one Xray config per server). Extracts the
/// VLESS outbound of each entry into a <see cref="ServerConfig"/>.
///
/// Decoy entries (fake 0.0.0.0 server + "please download Happ" remark) that the
/// panel serves to non-genuine clients are filtered out.
/// </summary>
public static class XrayJsonParser
{
    /// <summary>
    /// Extracts the human-readable "remarks" of every entry (including decoys) plus
    /// the first outbound server address, for diagnostics. Providers put the reason
    /// ("please download Happ", "device limit", "expired") in the decoy's remarks.
    /// </summary>
    public static string ExtractDiagnostics(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = new List<string>();
            var root = doc.RootElement;
            var entries = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var e in entries)
            {
                var remarks = GetString(e, "remarks");
                string addr = "";
                if (e.TryGetProperty("outbounds", out var obs) && obs.ValueKind == JsonValueKind.Array)
                    foreach (var ob in obs.EnumerateArray())
                        if (string.Equals(GetString(ob, "protocol"), "vless", StringComparison.OrdinalIgnoreCase) &&
                            ob.TryGetProperty("settings", out var st) &&
                            st.TryGetProperty("vnext", out var vn) && vn.ValueKind == JsonValueKind.Array && vn.GetArrayLength() > 0)
                        { addr = GetString(vn[0], "address"); break; }
                items.Add($"remarks='{remarks}' server={addr}");
            }
            return string.Join(" | ", items);
        }
        catch { return "(could not parse remarks)"; }
    }

    public static bool LooksLikeXrayJson(string body)
    {
        var t = body.TrimStart();
        return t.StartsWith('[') || (t.StartsWith('{') && t.Contains("\"outbounds\""));
    }

    public static IReadOnlyList<ServerConfig> ParseMany(string json)
    {
        var result = new List<ServerConfig>();
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in root.EnumerateArray())
                TryAddEntry(entry, result);
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            TryAddEntry(root, result);
        }

        return result;
    }

    private static void TryAddEntry(JsonElement entry, List<ServerConfig> result)
    {
        var remarks = GetString(entry, "remarks");

        if (!entry.TryGetProperty("outbounds", out var outbounds) ||
            outbounds.ValueKind != JsonValueKind.Array)
            return;

        foreach (var ob in outbounds.EnumerateArray())
        {
            if (!string.Equals(GetString(ob, "protocol"), "vless", StringComparison.OrdinalIgnoreCase))
                continue;

            var cfg = FromOutbound(ob, remarks);
            if (cfg is not null && IsRealServer(cfg))
                result.Add(cfg);
            break; // one proxy outbound per config entry
        }
    }

    private static bool IsRealServer(ServerConfig c) =>
        !string.IsNullOrWhiteSpace(c.Server) &&
        c.Server != "0.0.0.0" &&
        c.Port > 1 &&
        !string.IsNullOrWhiteSpace(c.Uuid);

    private static ServerConfig? FromOutbound(JsonElement ob, string remarks)
    {
        if (!ob.TryGetProperty("settings", out var settings)) return null;
        if (!settings.TryGetProperty("vnext", out var vnext) ||
            vnext.ValueKind != JsonValueKind.Array || vnext.GetArrayLength() == 0)
            return null;

        var node = vnext[0];
        var address = GetString(node, "address");
        var port = GetInt(node, "port", 443);

        string uuid = "", flow = "";
        if (node.TryGetProperty("users", out var users) &&
            users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0)
        {
            uuid = GetString(users[0], "id");
            flow = GetString(users[0], "flow");
        }

        var cleanName = TextUtil.StripEmoji(remarks);
        var cfg = new ServerConfig
        {
            Name = string.IsNullOrWhiteSpace(cleanName) ? address : cleanName,
            Server = address,
            Port = port,
            Uuid = uuid,
            Flow = flow,
            Security = "none",
            Transport = "tcp"
        };

        if (ob.TryGetProperty("streamSettings", out var ss))
            ApplyStream(ss, cfg);

        return cfg;
    }

    private static void ApplyStream(JsonElement ss, ServerConfig cfg)
    {
        cfg.Transport = string.IsNullOrWhiteSpace(GetString(ss, "network")) ? "tcp" : GetString(ss, "network");
        cfg.Security = string.IsNullOrWhiteSpace(GetString(ss, "security")) ? "none" : GetString(ss, "security").ToLowerInvariant();

        if (ss.TryGetProperty("realitySettings", out var reality) && reality.ValueKind == JsonValueKind.Object)
        {
            cfg.Sni = GetString(reality, "serverName");
            cfg.PublicKey = GetString(reality, "publicKey");
            cfg.ShortId = GetString(reality, "shortId");
            var fp = GetString(reality, "fingerprint");
            if (!string.IsNullOrWhiteSpace(fp)) cfg.Fingerprint = fp;
        }
        else if (ss.TryGetProperty("tlsSettings", out var tls) && tls.ValueKind == JsonValueKind.Object)
        {
            cfg.Sni = GetString(tls, "serverName");
            var fp = GetString(tls, "fingerprint");
            if (!string.IsNullOrWhiteSpace(fp)) cfg.Fingerprint = fp;
        }

        if (ss.TryGetProperty("wsSettings", out var ws) && ws.ValueKind == JsonValueKind.Object)
        {
            cfg.WsPath = GetString(ws, "path");
            if (ws.TryGetProperty("headers", out var hdrs) && hdrs.ValueKind == JsonValueKind.Object)
                cfg.WsHost = GetString(hdrs, "Host");
        }

        if (ss.TryGetProperty("grpcSettings", out var grpc) && grpc.ValueKind == JsonValueKind.Object)
            cfg.GrpcServiceName = GetString(grpc, "serviceName");
    }

    private static string GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object &&
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement el, string name, int fallback)
    {
        if (el.TryGetProperty(name, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        }
        return fallback;
    }
}
