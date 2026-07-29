using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TunnelDeck.Models;
using DColor = System.Drawing.Color;

namespace TunnelDeck.Services;

/// <summary>
/// Builds the tray icon (as a WPF <see cref="ImageSource"/>) for a connection status:
/// the brand icon inside a bold colored frame — green = connected, red = off,
/// amber = connecting. Using IconSource (not Icon) so H.NotifyIcon reliably refreshes
/// the tray when the status changes.
/// </summary>
public static class TrayIconFactory
{
    private static readonly Dictionary<ConnectionStatus, ImageSource> _cache = new();

    public static ImageSource For(ConnectionStatus status)
    {
        if (_cache.TryGetValue(status, out var cached)) return cached;
        var (png, border) = Map(status);
        var img = Build(png, border);
        _cache[status] = img;
        return img;
    }

    private static (string png, DColor border) Map(ConnectionStatus s) => s switch
    {
        ConnectionStatus.Connected => ("icon-on.png", DColor.FromArgb(0x1F, 0xA9, 0x3B)),   // green
        ConnectionStatus.Connecting => ("icon-off.png", DColor.FromArgb(0xF1, 0xC4, 0x0F)), // amber
        ConnectionStatus.Reconnecting => ("icon-off.png", DColor.FromArgb(0xF1, 0xC4, 0x0F)),
        _ => ("icon-off.png", DColor.FromArgb(0xE0, 0x2B, 0x1A))                             // red (off/error)
    };

    private static ImageSource Build(string resourceName, DColor border)
    {
        const int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(DColor.Transparent);

            using (var br = new SolidBrush(border))
            using (var frame = Rounded(new Rectangle(0, 0, size, size), 16))
                g.FillPath(br, frame);

            var src = LoadBitmap(resourceName);
            if (src is not null)
            {
                using (src)
                {
                    const int inset = 8;
                    using (var plate = new SolidBrush(DColor.White))
                    using (var pp = Rounded(new Rectangle(inset, inset, size - inset * 2, size - inset * 2), 10))
                        g.FillPath(plate, pp);
                    g.DrawImage(src, inset, inset, size - inset * 2, size - inset * 2);
                }
            }
        }
        return ToImageSource(bmp);
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

    private static ImageSource ToImageSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
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
