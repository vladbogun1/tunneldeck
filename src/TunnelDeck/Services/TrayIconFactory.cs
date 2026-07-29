using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Provides the tray icon for a given connection status using the app's brand icons:
/// green arrow when connected, red arrow otherwise.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<bool, Icon> _cache = new();

    public static Icon For(ConnectionStatus status)
    {
        bool connected = status == ConnectionStatus.Connected;
        if (_cache.TryGetValue(connected, out var cached)) return cached;
        var icon = Load(connected ? "icon-on.png" : "icon-off.png");
        _cache[connected] = icon;
        return icon;
    }

    private static Icon Load(string resourceName)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{resourceName}", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)!.Stream;
            using var src = new Bitmap(stream);
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, 0, 0, 32, 32);
            }
            return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
        }
        catch
        {
            // Fallback: a plain colored square so the tray still shows something.
            using var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
                g.Clear(resourceName.Contains("on") ? Color.SeaGreen : Color.Firebrick);
            return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
        }
    }
}
