# TunnelDeck

A lightweight Windows **tray app for per-application VPN** over a VLESS/Reality
subscription (the kind of key that ships with "Happ" clients).

Unlike Happ, TunnelDeck lets you route **only specific apps** through the VPN:
pick them from the running-processes list or browse to any `.exe`, toggle each
on/off, and everything else keeps using your normal connection. While connected,
each app shows its live ↓/↑ speed (via the sing-box Clash API `/connections`).

<p align="center"><i>Tray → flyout → list of apps → per-app switch → add app.</i></p>

---

## How it works

TunnelDeck is a thin, friendly shell around **[sing-box](https://sing-box.sagernet.org/)**,
which does the actual tunneling. The trick that Happ can't do:

1. sing-box brings up a **TUN** virtual adapter and captures all traffic.
2. A route rule matches the **process** that owns each connection:
   - process is in your list → **`proxy`** outbound (your VLESS/Reality server)
   - anything else → **`direct`** (normal internet, untouched)
3. DNS for tunneled apps is resolved through the proxy (anti-leak); everything
   else resolves locally.

Because tunneled apps can *only* leave through the `proxy` outbound, they never
leak to the direct connection while the core is running.

```
Subscription key ──▶ SubscriptionService ──▶ VlessParser ──▶ ServerConfig
                                                                   │
   TunneledApps (chrome.exe, discord.exe, …) ──────────────┐      │
                                                            ▼      ▼
                                            SingBoxConfigBuilder → config.json
                                                            │
                                                 CoreController → sing-box.exe (TUN)
```

## Subscription formats & the Happ-lock

Providers deliver keys in different ways. TunnelDeck's `SubscriptionService`
fetches with a **`Happ/<version>` User-Agent plus an `x-hwid` header** and accepts
three body formats: Xray/V2Ray **JSON** (array of per-server configs), base64
vless lists, and plaintext `vless://`.

Some panels (Remnawave configured "Happ-only", e.g. `my-keyboards.shop`)
deliberately hide the real config: a browser UA gets the HTML panel, other client
UAs get `404`, and a `Happ/x` UA *without* `x-hwid` gets a **decoy** server
(`0.0.0.0:1` + a "download Happ" remark). Only `Happ/x` **with** an `x-hwid`
header returns the real servers. TunnelDeck sends exactly that and filters decoy
entries, so the same key that works in Happ works here.

## Requirements

- Windows 10/11 (x64)
- **Administrator** rights — creating a TUN adapter requires elevation, so the
  app requests it at launch (UAC prompt). This is inherent to any per-app VPN.
- .NET 8 Desktop Runtime (bundled if you publish self-contained).

The sing-box core (**v1.11.15**, pinned) is downloaded automatically on first
run into `%LOCALAPPDATA%\TunnelDeck\core`.

## Build & run

```bash
dotnet build TunnelDeck.sln -c Release
```

Run `src/TunnelDeck/bin/Release/net8.0-windows/TunnelDeck.exe` (accept the UAC
prompt). A single self-contained executable:

```bash
dotnet publish src/TunnelDeck/TunnelDeck.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Using it

1. Launch → the flyout opens. Click **Paste subscription key** and paste the
   link your VPN bot sent you (`https://…` or a raw `vless://…`).
2. TunnelDeck fetches the servers. Pick one from the dropdown.
3. **+ Add application** → choose from running apps or browse to an `.exe`.
4. Flip **Connect**. Only your listed apps now go through the VPN.
5. Per-app switches add/remove an app from the tunnel live; the core hot-reloads.

State lives in `%LOCALAPPDATA%\TunnelDeck\state.json`; core logs in
`tunneldeck.log`.

## Project layout

```
src/TunnelDeck/
  Models/            ServerConfig, TunneledApp, AppSettings, AppState, ConnectionStatus
  Services/
    Paths            on-disk locations
    StateStore       persist AppState as JSON
    SubscriptionService  fetch + base64-decode the sub link (spoofs Happ UA)
    VlessParser      vless:// URI  -> ServerConfig
    SingBoxConfigBuilder  ServerConfig + apps -> sing-box config.json
    CoreBootstrapper download/extract pinned sing-box
    CoreController   start/stop/supervise sing-box, kill-switch auto-restart
    ProcessService   enumerate running apps
    IconExtractor    exe icon -> WPF ImageSource
    AutostartService HKCU Run entry
    TrayIconFactory  runtime-drawn status icons
  ViewModels/        MainViewModel (in-app page navigation), AddAppViewModel, AppEntryViewModel
  Views/             FlyoutWindow (single window, 4 in-app panels: Main/AddApp/
                     Settings/Subscription), FileDialogService, Converters
  App.xaml(.cs)      tray, single-instance, lifecycle, Windows 11 LIGHT theme,
                     global exception handlers (no hard crashes)
tests/TunnelDeck.SelfTest/   offline checks for the parser + config builder
```

## Verified

- vless:// parser, Xray-JSON parser (with decoy filtering) and config builder
  covered by `tests/TunnelDeck.SelfTest` (all pass).
- Generated config validated against the real **sing-box 1.11.15** binary:
  `sing-box check -c config.json` → exit 0, no warnings.
- **Live end-to-end**: the real subscription was fetched through the compiled
  `SubscriptionService` → 6 servers parsed → sing-box config built from the real
  credentials → `sing-box check` exit 0. (Only the elevated live TUN connection
  is left for you to confirm on your machine.)

## Known limitations / roadmap

- **UAC every launch** (required for TUN). A future version can install a small
  elevated helper service or a Task Scheduler entry (highest privileges) so the
  UI itself runs unelevated and autostart doesn't prompt.
- Only VLESS is parsed today (Reality/TLS/ws/grpc). vmess/trojan/ss are easy to
  add in `VlessParser` + `SingBoxConfigBuilder`.
- Latency/health probe and a per-app data-usage view are planned.
- Multiple concurrent server groups / rule profiles.

## Why sing-box (and not Happ)

Happ is a fine general client but has no per-process split tunneling on Windows.
The VLESS/Reality protocol is an open standard, so your existing key works with
sing-box directly — TunnelDeck just generates the right config and manages the
process.
