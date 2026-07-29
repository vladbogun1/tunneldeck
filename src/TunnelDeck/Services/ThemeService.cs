using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TunnelDeck.Services;

/// <summary>
/// Follows the Windows light/dark setting. It recolors the shared brush resources
/// in place (they're referenced via StaticResource, so mutating their Color updates
/// every control live) and reacts to the user flipping the system theme.
/// </summary>
public static class ThemeService
{
    private static bool _dark;
    public static bool IsDark => _dark;

    // (key, lightColor, darkColor) — colors as 0xRRGGBB.
    private static readonly (string key, int light, int dark)[] Palette =
    {
        ("WindowBg",      0xF3F3F3, 0x202020),
        ("Card",          0xFFFFFF, 0x2B2B2B),
        ("CardAlt",       0xFBFBFB, 0x262626),
        ("CardHover",     0xF5F5F5, 0x333333),
        ("Stroke",        0xE5E5E5, 0x3A3A3A),
        ("StrokeStrong",  0xD2D2D2, 0x4A4A4A),
        ("TextPrimary",   0x1B1B1B, 0xF3F3F3),
        ("TextSecondary", 0x5D5D5D, 0xC6C6C6),
        ("TextTertiary",  0x8A8A8A, 0x949494),
        ("Green",         0x0F7B0F, 0x3FB33F),
        ("AccentSoft",    0xE7F1FF, 0x123048),
        ("Hover",         0xEDEDED, 0x383838),
        ("HoverStrong",   0xE4E4E4, 0x414141),
        ("SwitchOff",     0xE8E8E8, 0x4A4A4A),
        ("ScrollThumb",   0xC2C2C2, 0x555555),
    };

    public static void Init()
    {
        Apply(ResolveInitial());
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    private static bool ResolveInitial()
    {
        var env = Environment.GetEnvironmentVariable("TUNNELDECK_THEME");
        if (string.Equals(env, "dark", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(env, "light", StringComparison.OrdinalIgnoreCase)) return false;
        return IsSystemDark();
    }

    public static void Shutdown()
    {
        try { SystemEvents.UserPreferenceChanged -= OnPreferenceChanged; } catch { }
    }

    private static void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() => Apply(IsSystemDark()));
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    private static void Apply(bool dark)
    {
        _dark = dark;
        var res = Application.Current?.Resources;
        if (res is null) return;

        foreach (var (key, light, darkc) in Palette)
        {
            var rgb = dark ? darkc : light;
            var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            brush.Freeze();
            // Referenced via DynamicResource, so replacing the entry updates the UI live.
            res[key] = brush;
        }
    }
}
