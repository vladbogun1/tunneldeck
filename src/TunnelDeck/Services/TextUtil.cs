using System.Globalization;
using System.Text;

namespace TunnelDeck.Services;

public static class TextUtil
{
    /// <summary>
    /// Removes emoji / flag / pictograph characters. Windows can't render
    /// regional-indicator flag emoji (it shows the 2-letter code, e.g. "lv"),
    /// so server names like "🇱🇻Латвия" become just "Латвия".
    /// </summary>
    public static string StripEmoji(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        var e = StringInfo.GetTextElementEnumerator(s);
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
}
