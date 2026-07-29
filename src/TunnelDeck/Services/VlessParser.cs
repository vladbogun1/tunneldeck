using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Parses <c>vless://</c> URIs (the format shipped by Happ-style subscriptions)
/// into <see cref="ServerConfig"/> objects.
///
/// URI shape:
///   vless://UUID@host:port?type=tcp&amp;security=reality&amp;pbk=...&amp;fp=chrome
///           &amp;sni=...&amp;sid=...&amp;flow=xtls-rprx-vision#Name
/// </summary>
public static class VlessParser
{
    public static IReadOnlyList<ServerConfig> ParseMany(string text)
    {
        var result = new List<ServerConfig>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            ServerConfig? cfg = null;
            if (line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) cfg = TryParse(line);
            else if (line.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)) cfg = TryParseTrojan(line);
            else if (line.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) cfg = TryParseVmess(line);
            else if (line.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) cfg = TryParseShadowsocks(line);

            if (cfg is not null) result.Add(cfg);
        }
        return result;
    }

    public static ServerConfig? TryParse(string uri)
    {
        try
        {
            // Split off the #fragment (name) first — it may contain characters
            // that confuse Uri, so pull it out manually.
            string name = "Server";
            var hashIdx = uri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = Uri.UnescapeDataString(uri[(hashIdx + 1)..]);
                uri = uri[..hashIdx];
            }

            var withoutScheme = uri["vless://".Length..];

            // userinfo@host:port?query
            var atIdx = withoutScheme.IndexOf('@');
            if (atIdx < 0) return null;
            var uuid = withoutScheme[..atIdx];
            var rest = withoutScheme[(atIdx + 1)..];

            string query = "";
            var qIdx = rest.IndexOf('?');
            if (qIdx >= 0)
            {
                query = rest[(qIdx + 1)..];
                rest = rest[..qIdx];
            }

            // rest is now host:port (host may be IPv6 in [..])
            string host;
            int port = 443;
            if (rest.StartsWith('['))
            {
                var close = rest.IndexOf(']');
                host = rest[1..close];
                var after = rest[(close + 1)..];
                if (after.StartsWith(':')) int.TryParse(after[1..], out port);
            }
            else
            {
                var colon = rest.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = rest[..colon];
                    int.TryParse(rest[(colon + 1)..], out port);
                }
                else host = rest;
            }

            var q = ParseQuery(query);

            var cfg = new ServerConfig
            {
                Protocol = "vless",
                Name = string.IsNullOrWhiteSpace(TextUtil.StripEmoji(name)) ? host : TextUtil.StripEmoji(name),
                Server = host,
                Port = port <= 0 ? 443 : port,
                Uuid = uuid,
                Flow = q["flow"] ?? "",
                Security = (q["security"] ?? "none").ToLowerInvariant(),
                Sni = q["sni"] ?? q["peer"] ?? q["host"] ?? "",
                Fingerprint = string.IsNullOrWhiteSpace(q["fp"]) ? "chrome" : q["fp"]!,
                PublicKey = q["pbk"] ?? "",
                ShortId = q["sid"] ?? "",
                Transport = (q["type"] ?? "tcp").ToLowerInvariant(),
                WsPath = q["path"] ?? "",
                WsHost = q["host"] ?? "",
                GrpcServiceName = q["serviceName"] ?? "",
                RawUri = uri
            };

            if (string.IsNullOrWhiteSpace(cfg.Server) || string.IsNullOrWhiteSpace(cfg.Uuid))
                return null;

            return cfg;
        }
        catch
        {
            return null;
        }
    }

    // ---- trojan://password@host:port?security=tls&sni=&type=tcp#name ----
    public static ServerConfig? TryParseTrojan(string uri)
    {
        try
        {
            var (name, body) = SplitName(uri, "trojan://");
            var at = body.IndexOf('@');
            if (at < 0) return null;
            var password = Uri.UnescapeDataString(body[..at]);
            var rest = body[(at + 1)..];
            var (query, hp) = SplitQuery(rest);
            var (host, port) = SplitHostPort(hp, 443);
            var q = ParseQuery(query);
            if (host.Length == 0 || password.Length == 0) return null;
            return new ServerConfig
            {
                Protocol = "trojan", Name = name.Length > 0 ? name : host,
                Server = host, Port = port, Password = password,
                Security = string.IsNullOrWhiteSpace(q["security"]) ? "tls" : q["security"]!.ToLowerInvariant(),
                Sni = q["sni"] ?? q["peer"] ?? q["host"] ?? "",
                Fingerprint = string.IsNullOrWhiteSpace(q["fp"]) ? "chrome" : q["fp"]!,
                Transport = (q["type"] ?? "tcp").ToLowerInvariant(),
                WsPath = q["path"] ?? "", WsHost = q["host"] ?? "",
                GrpcServiceName = q["serviceName"] ?? "", RawUri = uri
            };
        }
        catch { return null; }
    }

    // ---- vmess://base64(json) ----
    public static ServerConfig? TryParseVmess(string uri)
    {
        try
        {
            var b64 = uri["vmess://".Length..].Trim();
            var json = Base64Decode(b64);
            if (json is null) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string k) => r.TryGetProperty(k, out var v)
                ? (v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
                : "";
            var host = S("add"); if (host.Length == 0) return null;
            int.TryParse(S("port"), out var port); if (port <= 0) port = 443;
            var tls = S("tls");
            return new ServerConfig
            {
                Protocol = "vmess", Name = TextUtil.StripEmoji(S("ps")) is { Length: > 0 } n ? n : host,
                Server = host, Port = port, Uuid = S("id"),
                AlterId = int.TryParse(S("aid"), out var aid) ? aid : 0,
                VmessSecurity = string.IsNullOrWhiteSpace(S("scy")) ? "auto" : S("scy"),
                Security = tls.Equals("tls", StringComparison.OrdinalIgnoreCase) ? "tls" : "none",
                Sni = S("sni").Length > 0 ? S("sni") : S("host"),
                Transport = string.IsNullOrWhiteSpace(S("net")) ? "tcp" : S("net").ToLowerInvariant(),
                WsPath = S("path"), WsHost = S("host"),
                GrpcServiceName = S("path"), Fingerprint = "chrome", RawUri = uri
            };
        }
        catch { return null; }
    }

    // ---- ss://base64(method:password)@host:port#name  (or fully-base64) ----
    public static ServerConfig? TryParseShadowsocks(string uri)
    {
        try
        {
            var (name, body) = SplitName(uri, "ss://");
            string method, password, host; int port;
            var at = body.IndexOf('@');
            if (at >= 0)
            {
                var userinfo = body[..at];
                var decoded = Base64Decode(userinfo) ?? userinfo; // may be base64 or plain
                var colon = decoded.IndexOf(':');
                if (colon < 0) return null;
                method = decoded[..colon]; password = decoded[(colon + 1)..];
                (host, port) = SplitHostPort(SplitQuery(body[(at + 1)..]).host, 8388);
            }
            else
            {
                var decoded = Base64Decode(body);
                if (decoded is null) return null;
                var at2 = decoded.IndexOf('@'); if (at2 < 0) return null;
                var mp = decoded[..at2]; var colon = mp.IndexOf(':'); if (colon < 0) return null;
                method = mp[..colon]; password = mp[(colon + 1)..];
                (host, port) = SplitHostPort(decoded[(at2 + 1)..], 8388);
            }
            if (host.Length == 0 || method.Length == 0) return null;
            return new ServerConfig
            {
                Protocol = "shadowsocks", Name = name.Length > 0 ? name : host,
                Server = host, Port = port, Method = method, Password = password,
                Security = "none", Transport = "tcp", RawUri = uri
            };
        }
        catch { return null; }
    }

    // ---- shared helpers ----
    private static (string name, string body) SplitName(string uri, string scheme)
    {
        var name = "";
        var hash = uri.IndexOf('#');
        if (hash >= 0) { name = TextUtil.StripEmoji(Uri.UnescapeDataString(uri[(hash + 1)..])); uri = uri[..hash]; }
        return (name, uri[scheme.Length..]);
    }

    private static (string query, string host) SplitQuery(string s)
    {
        var q = s.IndexOf('?');
        return q >= 0 ? (s[(q + 1)..], s[..q]) : ("", s);
    }

    private static (string host, int port) SplitHostPort(string hp, int def)
    {
        if (hp.StartsWith('['))
        {
            var close = hp.IndexOf(']');
            var h = hp[1..close];
            var after = hp[(close + 1)..];
            int p = def; if (after.StartsWith(':')) int.TryParse(after[1..], out p);
            return (h, p <= 0 ? def : p);
        }
        var colon = hp.LastIndexOf(':');
        if (colon < 0) return (hp, def);
        int.TryParse(hp[(colon + 1)..], out var port);
        return (hp[..colon], port <= 0 ? def : port);
    }

    private static string? Base64Decode(string s)
    {
        try
        {
            var t = s.Replace('-', '+').Replace('_', '/').Trim();
            switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t));
        }
        catch { return null; }
    }

    /// <summary>Case-insensitive query parser that returns null for missing keys.</summary>
    private sealed class QueryDict
    {
        private readonly Dictionary<string, string> _map =
            new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, string value) => _map[key] = value;
        public string? this[string key] => _map.TryGetValue(key, out var v) ? v : null;
    }

    private static QueryDict ParseQuery(string query)
    {
        var q = new QueryDict();
        if (string.IsNullOrEmpty(query)) return q;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                q.Add(Uri.UnescapeDataString(pair), "");
            }
            else
            {
                var k = Uri.UnescapeDataString(pair[..eq]);
                var v = Uri.UnescapeDataString(pair[(eq + 1)..]);
                q.Add(k, v);
            }
        }
        return q;
    }
}
