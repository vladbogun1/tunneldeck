using System.Text.Json;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Builds a sing-box (v1.11.x schema) configuration that implements per-app VPN:
///
///   * A TUN interface captures all traffic (auto_route).
///   * Traffic whose owning process is in the tunneled list  -> "proxy" outbound.
///   * Everything else                                        -> "direct" outbound.
///   * DNS of tunneled apps is (optionally) resolved through the proxy to prevent
///     DNS leaks; all other DNS resolves locally.
///
/// Because tunneled apps can ONLY exit through the proxy outbound, their traffic
/// cannot leak to the direct connection while the core is running — the kill-switch
/// concern is therefore reduced to "keep the core alive", handled by CoreController.
/// </summary>
public static class SingBoxConfigBuilder
{
    /// <summary>Local Clash-API endpoint used to read per-process traffic stats.</summary>
    public const string ClashApiListen = "127.0.0.1:9797";
    public const string ClashApiBaseUrl = "http://127.0.0.1:9797";

    /// <summary>Local SOCKS/HTTP proxy that ProxiFyre redirects selected apps into.</summary>
    public const string SocksHost = "127.0.0.1";
    public const int SocksPort = 24808;
    public const string SocksEndpoint = "127.0.0.1:24808";

    /// <summary>Second local proxy for "site mode": browsers go here; only the chosen
    /// domains take the VPN, the rest goes direct.</summary>
    public const int SplitPort = 24809;
    public const string SplitEndpoint = "127.0.0.1:24809";

    /// <summary>
    /// Proxy-mode config (no TUN): a local mixed (SOCKS+HTTP) inbound that forwards
    /// everything it receives to the VPN. ProxiFyre redirects only the chosen apps
    /// here, so the system routing table is never touched — connecting/disconnecting
    /// cannot disrupt other apps (e.g. online games).
    /// </summary>
    public static string BuildProxyMode(ServerConfig server, AppSettings settings, IReadOnlyList<string>? sites = null)
    {
        var siteList = (sites ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var inbounds = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "mixed", ["tag"] = "in", ["listen"] = SocksHost, ["listen_port"] = SocksPort
            }
        };
        if (siteList.Count > 0)
        {
            inbounds.Add(new Dictionary<string, object?>
            {
                ["type"] = "mixed", ["tag"] = "split", ["listen"] = SocksHost, ["listen_port"] = SplitPort,
                ["sniff"] = true   // read TLS SNI / HTTP host so we can route by domain
            });
        }

        var routeRules = new List<object>();
        routeRules.AddRange(ServerDomainRule(server, "outbound", "direct"));
        if (siteList.Count > 0)
        {
            // Force browser QUIC (HTTP/3, UDP 443) to fall back to TCP so the destination
            // domain can be read reliably for domain-based routing.
            routeRules.Add(new Dictionary<string, object?>
            {
                ["inbound"] = new[] { "split" }, ["network"] = "udp", ["port"] = 443, ["action"] = "reject"
            });
            // Only the chosen domains from the "split" (browser) inbound take the VPN.
            routeRules.Add(new Dictionary<string, object?>
            {
                ["inbound"] = new[] { "split" }, ["domain_suffix"] = siteList, ["outbound"] = "proxy"
            });
            // Everything else from the browser goes direct.
            routeRules.Add(new Dictionary<string, object?>
            {
                ["inbound"] = new[] { "split" }, ["outbound"] = "direct"
            });
        }

        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = string.IsNullOrWhiteSpace(settings.LogLevel) ? "warn" : settings.LogLevel,
                ["timestamp"] = true
            },
            ["dns"] = BuildProxyModeDns(server, siteList),
            ["inbounds"] = inbounds,
            ["outbounds"] = BuildOutbounds(server),
            ["route"] = new Dictionary<string, object?>
            {
                ["rules"] = routeRules,
                ["final"] = "proxy"   // the full-tunnel "in" inbound sends everything to the VPN
            },
            ["experimental"] = new Dictionary<string, object?>
            {
                ["clash_api"] = new Dictionary<string, object?> { ["external_controller"] = ClashApiListen }
            }
        };
        return JsonSerializer.Serialize(config, Json);
    }

    private static object BuildProxyModeDns(ServerConfig server, List<string> sites)
    {
        var rules = new List<object>();
        rules.AddRange(ServerDomainRule(server, "server", "dns-direct"));
        if (sites.Count > 0)
            rules.Add(new Dictionary<string, object?> { ["domain_suffix"] = sites, ["server"] = "dns-proxy" });

        return new Dictionary<string, object?>
        {
            ["servers"] = new object[]
            {
                new Dictionary<string, object?> { ["tag"] = "dns-proxy", ["address"] = "https://1.1.1.1/dns-query", ["detour"] = "proxy" },
                new Dictionary<string, object?> { ["tag"] = "dns-direct", ["address"] = "https://8.8.8.8/dns-query", ["detour"] = "direct" }
            },
            ["rules"] = rules,
            ["final"] = "dns-proxy",
            ["strategy"] = "ipv4_only",
            ["independent_cache"] = true
        };
    }

    /// <summary>
    /// A rule forcing the proxy server's own hostname to resolve/route DIRECTLY
    /// (via dns-direct / direct outbound) — otherwise dialing the proxy would try to go
    /// through the proxy itself (DNS/route loopback). <paramref name="key"/> is
    /// "server" for a DNS rule or "outbound" for a route rule; <paramref name="target"/>
    /// is "dns-direct" or "direct" respectively.
    /// </summary>
    private static List<object> ServerDomainRule(ServerConfig server, string key, string target)
    {
        var domains = new List<string>();
        if (!string.IsNullOrWhiteSpace(server.Server) && !IsIpAddress(server.Server))
            domains.Add(server.Server);
        if (!string.IsNullOrWhiteSpace(server.Sni) && !IsIpAddress(server.Sni) && server.Sni != server.Server)
            domains.Add(server.Sni);

        var rules = new List<object>();
        if (domains.Count > 0)
            rules.Add(new Dictionary<string, object?> { ["domain"] = domains, [key] = target });
        return rules;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Build(ServerConfig server, IReadOnlyList<TunneledApp> apps, AppSettings settings)
    {
        var tunneledNames = apps
            .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.ProcessName))
            .Select(a => a.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var config = new Dictionary<string, object?>
        {
            ["log"] = new Dictionary<string, object?>
            {
                ["level"] = string.IsNullOrWhiteSpace(settings.LogLevel) ? "warn" : settings.LogLevel,
                ["timestamp"] = true
            },
            ["dns"] = BuildDns(server, tunneledNames, settings),
            ["inbounds"] = new object[] { BuildTun() },
            ["outbounds"] = BuildOutbounds(server),
            ["route"] = BuildRoute(tunneledNames),
            ["experimental"] = new Dictionary<string, object?>
            {
                ["clash_api"] = new Dictionary<string, object?>
                {
                    ["external_controller"] = ClashApiListen
                }
            }
        };

        return JsonSerializer.Serialize(config, Json);
    }

    private static object BuildTun() => new Dictionary<string, object?>
    {
        ["type"] = "tun",
        ["tag"] = "tun-in",
        ["interface_name"] = "TunnelDeck",
        // IPv4-only address + ipv4_only DNS strategy avoids a flood of AAAA timeouts.
        ["address"] = new[] { "172.19.0.1/30" },
        ["mtu"] = 1500,
        ["auto_route"] = true,
        ["strict_route"] = true,
        // gVisor stack is the most compatible userspace stack on Windows.
        ["stack"] = "gvisor",
        ["sniff"] = true,
        ["sniff_override_destination"] = false
    };

    private static object BuildDns(ServerConfig server, List<string> tunneledNames, AppSettings settings)
    {
        var rules = new List<object>();

        // 1) The proxy server's own hostname MUST resolve via direct DNS, otherwise
        //    dialing the proxy would try to resolve it *through* the proxy -> loopback.
        var serverDomains = new List<string>();
        if (!string.IsNullOrWhiteSpace(server.Server) && !IsIpAddress(server.Server))
            serverDomains.Add(server.Server);
        if (!string.IsNullOrWhiteSpace(server.Sni) && !IsIpAddress(server.Sni) && server.Sni != server.Server)
            serverDomains.Add(server.Sni);
        if (serverDomains.Count > 0)
            rules.Add(new Dictionary<string, object?> { ["domain"] = serverDomains, ["server"] = "dns-direct" });

        // 2) Tunneled apps resolve through the proxy (anti-leak).
        if (settings.ProxyDnsForTunneledApps && tunneledNames.Count > 0)
            rules.Add(new Dictionary<string, object?> { ["process_name"] = tunneledNames, ["server"] = "dns-proxy" });

        return new Dictionary<string, object?>
        {
            ["servers"] = new object[]
            {
                // DoH (HTTPS) — NOT plain :53 — so these queries are never caught by the
                // DNS hijack rule and never loop back through the TUN.
                new Dictionary<string, object?>
                {
                    ["tag"] = "dns-proxy",
                    ["address"] = "https://1.1.1.1/dns-query",
                    ["detour"] = "proxy"
                },
                new Dictionary<string, object?>
                {
                    ["tag"] = "dns-direct",
                    ["address"] = "https://8.8.8.8/dns-query",
                    ["detour"] = "direct"
                }
            },
            ["rules"] = rules,
            ["final"] = "dns-direct",
            ["strategy"] = "ipv4_only",
            ["independent_cache"] = true
        };
    }

    private static bool IsIpAddress(string host) =>
        System.Net.IPAddress.TryParse(host, out _);

    private static object[] BuildOutbounds(ServerConfig server) => new object[]
    {
        BuildProxyOutbound(server),
        new Dictionary<string, object?> { ["type"] = "direct", ["tag"] = "direct" }
        // DNS hijack and blocking are handled via route rule actions (sing-box 1.11+),
        // so no legacy "dns"/"block" special outbounds are needed.
    };

    private static object BuildProxyOutbound(ServerConfig s)
    {
        var outbound = new Dictionary<string, object?>
        {
            ["type"] = "vless",
            ["tag"] = "proxy",
            ["server"] = s.Server,
            ["server_port"] = s.Port,
            ["uuid"] = s.Uuid,
            ["packet_encoding"] = "xudp"
        };

        if (!string.IsNullOrWhiteSpace(s.Flow))
            outbound["flow"] = s.Flow;

        // TLS / Reality
        if (s.Security is "reality" or "tls")
        {
            var tls = new Dictionary<string, object?>
            {
                ["enabled"] = true,
                ["server_name"] = string.IsNullOrWhiteSpace(s.Sni) ? s.Server : s.Sni,
                ["utls"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["fingerprint"] = string.IsNullOrWhiteSpace(s.Fingerprint) ? "chrome" : s.Fingerprint
                }
            };

            if (s.Security == "reality")
            {
                tls["reality"] = new Dictionary<string, object?>
                {
                    ["enabled"] = true,
                    ["public_key"] = s.PublicKey,
                    ["short_id"] = s.ShortId
                };
            }

            outbound["tls"] = tls;
        }

        // Transport
        switch (s.Transport)
        {
            case "ws":
                outbound["transport"] = new Dictionary<string, object?>
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrWhiteSpace(s.WsPath) ? "/" : s.WsPath,
                    ["headers"] = string.IsNullOrWhiteSpace(s.WsHost)
                        ? null
                        : new Dictionary<string, object?> { ["Host"] = s.WsHost }
                };
                break;
            case "grpc":
                outbound["transport"] = new Dictionary<string, object?>
                {
                    ["type"] = "grpc",
                    ["service_name"] = s.GrpcServiceName
                };
                break;
            // "tcp" and unknown -> no transport block (raw TCP)
        }

        return outbound;
    }

    private static object BuildRoute(List<string> tunneledNames)
    {
        var rules = new List<object>
        {
            // Hijack DNS queries into the internal DNS resolver (modern rule action).
            new Dictionary<string, object?> { ["protocol"] = "dns", ["action"] = "hijack-dns" }
        };

        if (tunneledNames.Count > 0)
        {
            rules.Add(new Dictionary<string, object?>
            {
                ["process_name"] = tunneledNames,
                ["outbound"] = "proxy"
            });
        }

        return new Dictionary<string, object?>
        {
            ["rules"] = rules,
            ["final"] = "direct",
            ["auto_detect_interface"] = true
        };
    }
}
