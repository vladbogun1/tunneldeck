using System.Text;
using TunnelDeck.Models;
using TunnelDeck.Services;

int failures = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}");
    if (!ok) failures++;
}

Console.WriteLine("== VLESS parser ==");

// A syntactically valid Reality public key (base64url of 32 bytes) so `sing-box check` fully validates.
var reality = "vless://11111111-2222-3333-4444-555555555555@1.2.3.4:443" +
    "?type=tcp&security=reality&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&fp=chrome&sni=www.microsoft.com&sid=abcd1234&flow=xtls-rprx-vision#NL-Reality";

var cfg = VlessParser.TryParse(reality)!;
Check("parsed non-null", cfg is not null);
Check("uuid", cfg!.Uuid == "11111111-2222-3333-4444-555555555555");
Check("server", cfg.Server == "1.2.3.4");
Check("port", cfg.Port == 443);
Check("security reality", cfg.Security == "reality");
Check("sni", cfg.Sni == "www.microsoft.com");
Check("pbk", cfg.PublicKey == "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
Check("sid", cfg.ShortId == "abcd1234");
Check("flow", cfg.Flow == "xtls-rprx-vision");
Check("name", cfg.Name == "NL-Reality");

// base64 subscription with two URIs
var raw = reality + "\n" +
    "vless://99999999-8888-7777-6666-555555555555@example.com:8443?type=ws&security=tls&sni=example.com&path=%2Fws&host=example.com#WS-TLS";
var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
var many = VlessParser.ParseMany(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
Check("parsed 2 servers", many.Count == 2);
Check("second is ws", many[1].Transport == "ws");
Check("second ws path", many[1].WsPath == "/ws");

Console.WriteLine("\n== sing-box config builder ==");
var apps = new List<TunneledApp>
{
    new() { ProcessName = "chrome.exe", DisplayName = "Chrome", Enabled = true },
    new() { ProcessName = "discord.exe", DisplayName = "Discord", Enabled = true },
    new() { ProcessName = "vlc.exe", DisplayName = "VLC", Enabled = false }, // disabled -> excluded
};
var json = SingBoxConfigBuilder.Build(cfg!, apps, new AppSettings());
Console.WriteLine(json);

var outPath = Environment.GetEnvironmentVariable("SELFTEST_CONFIG_OUT");
if (!string.IsNullOrWhiteSpace(outPath))
{
    System.IO.File.WriteAllText(outPath, json);
    Console.WriteLine($"[wrote config to {outPath}]");
}

Check("has tun inbound", json.Contains("\"type\": \"tun\""));
Check("has vless outbound", json.Contains("\"type\": \"vless\""));
Check("has reality block", json.Contains("\"reality\""));
Check("routes chrome", json.Contains("chrome.exe"));
Check("routes discord", json.Contains("discord.exe"));
Check("excludes disabled vlc", !json.Contains("vlc.exe"));
Check("final direct", json.Contains("\"final\": \"direct\""));
Check("dns hijack rule", json.Contains("\"protocol\": \"dns\""));

// Validate it is well-formed JSON
try { using var _ = System.Text.Json.JsonDocument.Parse(json); Check("valid JSON", true); }
catch { Check("valid JSON", false); }

Console.WriteLine("\n== Xray JSON subscription parser (real provider format) ==");
var xray = """
[
  {"remarks":"🇱🇻 Latvia","outbounds":[
    {"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"lv01.tranzit-net.ru","port":443,"users":[{"id":"11111111-2222-3333-4444-555555555555","encryption":"none","flow":"xtls-rprx-vision"}]}]},
     "streamSettings":{"network":"tcp","security":"reality","realitySettings":{"serverName":"d.tranzit-net.ru","publicKey":"IAdOYYmEMPFAhpVUhqfML58AVtYPdAb71R_HAPf0N0k","fingerprint":"firefox"},"tcpSettings":{}}},
    {"tag":"direct","protocol":"freedom"}]},
  {"remarks":"please download Happ","outbounds":[
    {"tag":"proxy","protocol":"vless","settings":{"vnext":[{"address":"0.0.0.0","port":1,"users":[{"id":"x","encryption":"none","flow":""}]}]},
     "streamSettings":{"network":"tcp","security":"none"}}]}
]
""";
var xServers = XrayJsonParser.ParseMany(xray);
Check("xray: 1 real server (decoy filtered)", xServers.Count == 1);
Check("xray: name (emoji stripped)", xServers[0].Name == "Latvia");
Check("xray: server", xServers[0].Server == "lv01.tranzit-net.ru");
Check("xray: reality", xServers[0].Security == "reality");
Check("xray: sni", xServers[0].Sni == "d.tranzit-net.ru");
Check("xray: pubkey", xServers[0].PublicKey == "IAdOYYmEMPFAhpVUhqfML58AVtYPdAb71R_HAPf0N0k");
Check("xray: fingerprint firefox", xServers[0].Fingerprint == "firefox");
Check("xray: flow", xServers[0].Flow == "xtls-rprx-vision");

// Build a sing-box config from the real-shaped server and export for `sing-box check`.
var xJson = SingBoxConfigBuilder.Build(xServers[0], apps, new AppSettings());
var xOut = Environment.GetEnvironmentVariable("SELFTEST_XRAY_CONFIG_OUT");
if (!string.IsNullOrWhiteSpace(xOut))
{
    System.IO.File.WriteAllText(xOut, xJson);
    Console.WriteLine($"[wrote xray-derived config to {xOut}]");
}

// ---- Optional LIVE end-to-end test against the real subscription URL ----
var liveUrl = Environment.GetEnvironmentVariable("SELFTEST_LIVE_URL");
if (!string.IsNullOrWhiteSpace(liveUrl))
{
    Console.WriteLine("\n== LIVE subscription end-to-end ==");
    try
    {
        var svc = new SubscriptionService();
        var hwid = Environment.GetEnvironmentVariable("SELFTEST_HWID") ?? "td-selftest-hwid-0001";
        var live = await svc.FetchAsync(liveUrl!, hwid);
        Check("live: got servers", live.Count > 0);
        Console.WriteLine($"  servers ({live.Count}): " + string.Join(", ", live.Select(s => s.Name)));

        var wantPick = Environment.GetEnvironmentVariable("SELFTEST_PICK");
        var pick = (wantPick is not null
                        ? live.FirstOrDefault(s => s.Name.Contains(wantPick, StringComparison.OrdinalIgnoreCase))
                        : null)
                   ?? live.FirstOrDefault(s => s.Server != "0.0.0.0") ?? live[0];
        Console.WriteLine($"  picked: {pick.Name} ({pick.Server}:{pick.Port})");

        var tunnelProc = Environment.GetEnvironmentVariable("SELFTEST_TUNNEL_PROC");
        var liveApps = tunnelProc is not null
            ? new List<TunneledApp> { new() { ProcessName = tunnelProc, Enabled = true } }
            : apps;
        var liveJson = SingBoxConfigBuilder.Build(pick, liveApps, new AppSettings());
        var liveOut = Environment.GetEnvironmentVariable("SELFTEST_LIVE_CONFIG_OUT");
        if (!string.IsNullOrWhiteSpace(liveOut))
        {
            System.IO.File.WriteAllText(liveOut, liveJson);
            Console.WriteLine($"  [wrote live config for '{pick.Name}' to {liveOut}]");
        }
    }
    catch (Exception ex)
    {
        Check("live: fetch ok", false);
        Console.WriteLine("  live error: " + ex.Message);
    }
}

Console.WriteLine($"\n{(failures == 0 ? "ALL PASSED" : failures + " FAILED")}");
return failures;
