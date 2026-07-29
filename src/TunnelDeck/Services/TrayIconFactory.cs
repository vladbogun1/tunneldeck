using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Builds the tray icon for a connection status: the brand icon inside a bold
/// colored frame — green = connected, red = off, amber = connecting — so the state
/// is obvious even at 16px.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<ConnectionStatus, Icon> _cache = new();

    public static Icon For(ConnectionStatus status)
    {
        if (_cache.TryGetValue(status, out var cached)) return cached;
        var (png, border) = Map(status);
        var icon = Build(png, border);
        _cache[status] = icon;
        return icon;
    }

    private static (string png, Color border) Map(ConnectionStatus s) => s switch
    {
        ConnectionStatus.Connected => ("icon-on.png", Color.FromArgb(0x1F, 0xA9, 0x3B)),   // green
        ConnectionStatus.Connecting => ("icon-off.png", Color.FromArgb(0xF1, 0xC4, 0x0F)), // amber
        ConnectionStatus.Reconnecting => ("icon-off.png", Color.FromArgb(0xF1, 0xC4, 0x0F)),
        _ => ("icon-off.png", Color.FromArgb(0xE0, 0x2B, 0x1A))                             // red (off/error)
    };

    private static Icon Build(string resourceName, Color border)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // Solid colored rounded frame (the status indicator).
            using (var br = new SolidBrush(border))
            using (var frame = Rounded(new Rectangle(0, 0, size, size), 8))
                g.FillPath(br, frame);

            // Brand icon inset, leaving a ~4px colored border all around.
            var src = LoadBitmap(resourceName);
            if (src is not null)
            {
                using (src)
                {
                    const int inset = 4;
                    // white rounded plate behind the icon so it reads on any frame color
                    using (var plate = new SolidBrush(Color.White))
                    using (var pp = Rounded(new Rectangle(inset, inset, size - inset * 2, size - inset * 2), 5))
                        g.FillPath(plate, pp);
                    g.DrawImage(src, inset, inset, size - inset * 2, size - inset * 2);
                }
            }
        }
        return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
    }

    private static Bitmap? LoadBitmap(string resourceName)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{resourceName}", UriKind.Absolute);
            using var stream = Application.GetResourceStream(uri)!.Stream;
            return new Bitmap(stream);
        }
        catch { return null; }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
