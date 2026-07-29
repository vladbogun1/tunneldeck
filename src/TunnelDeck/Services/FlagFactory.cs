using System.Windows;
using System.Windows.Media;

namespace TunnelDeck.Services;

/// <summary>
/// Builds small country-flag images as vector drawings (no network, no bundled
/// bitmaps). Covers the flag layouts common to European VPN exits — horizontal /
/// vertical tricolours, Nordic crosses and the Swiss cross. Unknown or complex
/// flags (US, GB, GE, …) return null so the UI can fall back to the country code.
/// </summary>
public static class FlagFactory
{
    private const double W = 60, H = 40;   // 3:2 canvas
    private static readonly Dictionary<string, ImageSource?> _cache = new();

    /// <summary>Flag image for a 2-letter ISO code, or null if we can't draw it.</summary>
    public static ImageSource? For(string? cc)
    {
        var key = (cc ?? "").Trim().ToUpperInvariant();
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var img = Build(key);
        img?.Freeze();
        _cache[key] = img;
        return img;
    }

    private static DrawingImage? Build(string cc) => cc switch
    {
        "NL" => H3(C(0xAE, 0x1C, 0x28), Colors.White, C(0x21, 0x46, 0x8B)),
        "DE" => H3(Colors.Black, C(0xDD, 0x00, 0x00), C(0xFF, 0xCE, 0x00)),
        "RU" => H3(Colors.White, C(0x00, 0x39, 0xA6), C(0xD5, 0x2B, 0x1E)),
        "AT" => H3(C(0xED, 0x29, 0x39), Colors.White, C(0xED, 0x29, 0x39)),
        "HU" => H3(C(0xCE, 0x25, 0x39), Colors.White, C(0x47, 0x70, 0x50)),
        "BG" => H3(Colors.White, C(0x00, 0x96, 0x6E), C(0xD6, 0x26, 0x12)),
        "LT" => H3(C(0xFD, 0xB9, 0x13), C(0x00, 0x6A, 0x44), C(0xC1, 0x27, 0x2D)),
        "PL" => H2(Colors.White, C(0xDC, 0x14, 0x3C)),
        "UA" => H2(C(0x00, 0x57, 0xB7), C(0xFF, 0xD7, 0x00)),
        "ID" => H2(C(0xCE, 0x11, 0x26), Colors.White),
        "LV" => Lv(),
        "FR" => V3(C(0x00, 0x55, 0xA4), Colors.White, C(0xEF, 0x41, 0x35)),
        "IT" => V3(C(0x00, 0x92, 0x46), Colors.White, C(0xCE, 0x2B, 0x37)),
        "RO" => V3(C(0x00, 0x2B, 0x7F), C(0xFC, 0xD1, 0x16), C(0xCE, 0x11, 0x26)),
        "IE" => V3(C(0x16, 0x9B, 0x62), Colors.White, C(0xFF, 0x88, 0x3E)),
        "BE" => V3(Colors.Black, C(0xFD, 0xDA, 0x24), C(0xEF, 0x33, 0x40)),
        "SE" => Nordic(C(0x00, 0x6A, 0xA7), C(0xFE, 0xCC, 0x00)),
        "FI" => Nordic(Colors.White, C(0x00, 0x35, 0x80)),
        "DK" => Nordic(C(0xC6, 0x0C, 0x30), Colors.White),
        "NO" => Nordic(C(0xEF, 0x2B, 0x2D), Colors.White),
        "IS" => Nordic(C(0x02, 0x52, 0x9C), Colors.White),
        "CH" => Swiss(),
        _ => null
    };

    // ---- layouts ---------------------------------------------------------

    private static DrawingImage H2(Color a, Color b) =>
        Img(Rect(a, 0, 0, W, H / 2), Rect(b, 0, H / 2, W, H / 2));

    private static DrawingImage H3(Color a, Color b, Color c) =>
        Img(Rect(a, 0, 0, W, H / 3), Rect(b, 0, H / 3, W, H / 3), Rect(c, 0, 2 * H / 3, W, H / 3));

    private static DrawingImage V3(Color a, Color b, Color c) =>
        Img(Rect(a, 0, 0, W / 3, H), Rect(b, W / 3, 0, W / 3, H), Rect(c, 2 * W / 3, 0, W / 3, H));

    // Latvia: maroon / white(thin) / maroon (2:1:2 bands).
    private static DrawingImage Lv() =>
        Img(Rect(C(0x9E, 0x30, 0x39), 0, 0, W, H * 0.4),
            Rect(Colors.White, 0, H * 0.4, W, H * 0.2),
            Rect(C(0x9E, 0x30, 0x39), 0, H * 0.6, W, H * 0.4));

    // Offset Nordic cross.
    private static DrawingImage Nordic(Color field, Color cross) =>
        Img(Rect(field, 0, 0, W, H),
            Rect(cross, 0, H * 0.4, W, H * 0.2),            // horizontal bar
            Rect(cross, W * 0.30, 0, W * 0.14, H));         // vertical bar (offset left)

    // Centred Swiss cross on red.
    private static DrawingImage Swiss() =>
        Img(Rect(C(0xD5, 0x2B, 0x1E), 0, 0, W, H),
            Rect(Colors.White, W * 0.42, H * 0.22, W * 0.16, H * 0.56),
            Rect(Colors.White, W * 0.30, H * 0.40, W * 0.40, H * 0.20));

    // ---- primitives ------------------------------------------------------

    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Drawing Rect(Color c, double x, double y, double w, double h) =>
        new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(new Rect(x, y, w, h)));

    private static DrawingImage Img(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        // white base so partial layouts never show transparency
        g.Children.Add(Rect(Colors.White, 0, 0, W, H));
        foreach (var p in parts) g.Children.Add(p);
        return new DrawingImage(g);
    }
}
