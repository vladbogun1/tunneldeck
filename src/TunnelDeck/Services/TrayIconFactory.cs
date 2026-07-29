using System.Windows.Media;
using System.Windows.Media.Imaging;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Supplies the tray icon (as a URI-based <see cref="ImageSource"/>) for a connection
/// status: the brand icon inside a bold colored frame — green = connected, red = off,
/// amber = connecting. Uses pre-rendered pack-resource PNGs because H.NotifyIcon's
/// IconSource path requires an ImageSource with a UriSource.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<ConnectionStatus, ImageSource> _cache = new();

    public static ImageSource For(ConnectionStatus status)
    {
        if (_cache.TryGetValue(status, out var cached)) return cached;
        var name = status switch
        {
            ConnectionStatus.Connected => "tray-on.ico",
            ConnectionStatus.Connecting => "tray-connecting.ico",
            ConnectionStatus.Reconnecting => "tray-connecting.ico",
            _ => "tray-off.ico"
        };
        var img = new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute));
        img.Freeze();
        _cache[status] = img;
        return img;
    }
}
