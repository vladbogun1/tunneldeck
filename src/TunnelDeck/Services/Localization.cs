using System.Globalization;
using System.Linq;
using System.Windows;

namespace TunnelDeck.Services;

/// <summary>
/// Runtime UI language switching via swappable string ResourceDictionaries.
/// XAML uses {DynamicResource S.Key}; code uses Localization.T("S.Key").
/// </summary>
public static class Loc
{
    public static string Current { get; private set; } = "en";
    public static event EventHandler? Changed;

    /// <summary>Resolve a stored setting ("system" | "ru" | "en") to a concrete language.</summary>
    public static string Resolve(string? setting)
    {
        if (setting is "ru" or "en") return setting;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase)
            ? "ru" : "en";
    }

    public static void Apply(string language)
    {
        var app = Application.Current;
        if (app is null) return;
        language = language is "ru" or "en" ? language : "en";

        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Strings.{language}.xaml", UriKind.Absolute) };

        var old = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (old is not null) app.Resources.MergedDictionaries.Remove(old);
        app.Resources.MergedDictionaries.Add(dict);

        Current = language;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string key) => Application.Current?.TryFindResource(key) as string ?? key;
}
