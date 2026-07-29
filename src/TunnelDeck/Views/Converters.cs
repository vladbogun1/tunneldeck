using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TunnelDeck.Views;

/// <summary>Bool/string/int/null -> Visibility, with an optional Invert flag.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value switch
        {
            bool v => v,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            null => false,
            _ => true
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility vis && vis == Visibility.Visible;
}

/// <summary>
/// Strips emoji / flag / pictograph characters from a string for display.
/// Windows can't render regional-indicator flag emoji (it shows the 2-letter code),
/// so server names like "🇱🇻Латвия" are cleaned to just "Латвия".
/// </summary>
public sealed class EmojiStripConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return value ?? "";
        var sb = new System.Text.StringBuilder(s.Length);
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
        {
            var el = (string)e.Current;
            int cp = char.ConvertToUtf32(el, 0);
            if (IsEmoji(cp)) continue;
            sb.Append(el);
        }
        return sb.ToString().Trim();
    }

    private static bool IsEmoji(int cp) =>
        (cp >= 0x1F1E6 && cp <= 0x1F1FF) || // regional indicators (flags)
        (cp >= 0x1F300 && cp <= 0x1FAFF) || // symbols & pictographs
        (cp >= 0x2600 && cp <= 0x27BF)   || // misc symbols / dingbats
        cp == 0xFE0F || cp == 0x20E3 || cp == 0x200D; // variation selector, keycap, ZWJ

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Visible when the bound enum value equals the ConverterParameter (by name).</summary>
public sealed class EnumMatchToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
