namespace TunnelDeck.Services;

/// <summary>
/// Best-effort mapping from a server's display name (Russian or English country
/// name, possibly with extra text/numbers) to a 2-letter ISO country code, so the
/// UI can show a flag. Returns "" when nothing matches.
/// </summary>
public static class CountryCodes
{
    // Longer names first so "великобритания" isn't shadowed by a shorter match.
    private static readonly (string needle, string code)[] Map =
    {
        ("нидерланд", "NL"), ("голланд", "NL"), ("netherland", "NL"), ("holland", "NL"), ("amsterdam", "NL"),
        ("герман", "DE"), ("german", "DE"), ("frankfurt", "DE"), ("франкфурт", "DE"),
        ("швейцар", "CH"), ("switzerland", "CH"), ("swiss", "CH"), ("zurich", "CH"), ("цюрих", "CH"),
        ("швец", "SE"), ("швеци", "SE"), ("sweden", "SE"), ("stockholm", "SE"), ("стокгольм", "SE"),
        ("финлянд", "FI"), ("finland", "FI"), ("helsinki", "FI"), ("хельсинки", "FI"),
        ("дани", "DK"), ("denmark", "DK"), ("copenhagen", "DK"),
        ("норвег", "NO"), ("norway", "NO"), ("oslo", "NO"),
        ("исланд", "IS"), ("iceland", "IS"),
        ("франц", "FR"), ("france", "FR"), ("paris", "FR"), ("париж", "FR"),
        ("итали", "IT"), ("italy", "IT"), ("milan", "IT"), ("милан", "IT"),
        ("польш", "PL"), ("poland", "PL"), ("warsaw", "PL"), ("варшав", "PL"),
        ("латв", "LV"), ("latvia", "LV"), ("riga", "LV"), ("рига", "LV"),
        ("литв", "LT"), ("lithuania", "LT"),
        ("эстон", "EE"), ("estonia", "EE"),
        ("украин", "UA"), ("ukraine", "UA"), ("kyiv", "UA"), ("kiev", "UA"),
        ("росси", "RU"), ("russia", "RU"), ("moscow", "RU"), ("москв", "RU"),
        ("австри", "AT"), ("austria", "AT"), ("vienna", "AT"), ("вена", "AT"),
        ("венгр", "HU"), ("hungary", "HU"),
        ("болгар", "BG"), ("bulgaria", "BG"),
        ("румын", "RO"), ("romania", "RO"),
        ("бельг", "BE"), ("belgium", "BE"),
        ("ирланд", "IE"), ("ireland", "IE"),
        ("испан", "ES"), ("spain", "ES"),
        ("португал", "PT"), ("portugal", "PT"),
        ("греци", "GR"), ("greece", "GR"),
        ("велико", "GB"), ("британ", "GB"), ("англи", "GB"), ("uk", "GB"), ("united kingdom", "GB"), ("london", "GB"), ("лондон", "GB"),
        ("сша", "US"), ("америк", "US"), ("usa", "US"), ("united states", "US"),
        ("канад", "CA"), ("canada", "CA"),
        ("груз", "GE"), ("georgia", "GE"),
        ("казах", "KZ"), ("kazakh", "KZ"),
        ("турц", "TR"), ("turkey", "TR"), ("istanbul", "TR"), ("стамбул", "TR"),
        ("эмират", "AE"), ("оаэ", "AE"), ("emirat", "AE"), ("dubai", "AE"), ("дубай", "AE"),
        ("япон", "JP"), ("japan", "JP"), ("tokyo", "JP"), ("токио", "JP"),
        ("сингапур", "SG"), ("singapore", "SG"),
    };

    /// <summary>Extract a 2-letter ISO code from a server name, or "" if unknown.</summary>
    public static string FromName(string? name)
    {
        var s = (name ?? "").ToLowerInvariant();
        if (s.Length == 0) return "";
        foreach (var (needle, code) in Map)
            if (s.Contains(needle)) return code;
        return "";
    }
}
