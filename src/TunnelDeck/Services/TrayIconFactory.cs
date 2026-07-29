using System.Drawing;
using System.Drawing.Drawing2D;
using TunnelDeck.Models;

namespace TunnelDeck.Services;

/// <summary>
/// Draws the tray icon at runtime (no binary asset needed). A small "tunnel"
/// glyph tinted by connection status: green=connected, grey=off, amber=busy, red=error.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<ConnectionStatus, Icon> _cache = new();

    public static Icon For(ConnectionStatus status)
    {
        if (_cache.TryGetValue(status, out var cached)) return cached;
        var icon = Draw(ColorFor(status));
        _cache[status] = icon;
        return icon;
    }

    private static Color ColorFor(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => Color.FromArgb(46, 204, 113),   // green
        ConnectionStatus.Connecting => Color.FromArgb(241, 196, 15),  // amber
        ConnectionStatus.Reconnecting => Color.FromArgb(241, 196, 15),
        ConnectionStatus.Error => Color.FromArgb(231, 76, 60),        // red
        _ => Color.FromArgb(127, 140, 141)                            // grey
    };

    private static Icon Draw(Color color)
    {
        const int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Rounded-square badge
            using var badge = new SolidBrush(Color.FromArgb(30, 34, 45));
            using var path = RoundedRect(new Rectangle(6, 6, size - 12, size - 12), 16);
            g.FillPath(badge, path);

            // Tunnel opening (a filled ellipse in the status color)
            using var ring = new Pen(color, 7f);
            g.DrawEllipse(ring, 18, 18, size - 36, size - 36);

            // Center dot
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, size / 2 - 6, size / 2 - 6, 12, 12);
        }

        var hicon = bmp.GetHicon();
        return (Icon)Icon.FromHandle(hicon).Clone();
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
