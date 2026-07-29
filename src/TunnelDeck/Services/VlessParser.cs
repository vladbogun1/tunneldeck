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
            if (!line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) continue;

            var cfg = TryParse(line);
            if (cfg is not null)
                result.Add(cfg);
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
