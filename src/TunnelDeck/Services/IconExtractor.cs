using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TunnelDeck.Services;

/// <summary>Extracts (and caches) executable icons as WPF <see cref="ImageSource"/>.</summary>
public static class IconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;
        return Cache.GetOrAdd(exePath, Extract);
    }

    private static ImageSource? Extract(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            var src = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }
}
