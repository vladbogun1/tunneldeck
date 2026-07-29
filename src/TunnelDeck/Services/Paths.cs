using System.IO;

namespace TunnelDeck.Services;

/// <summary>Central place for all on-disk locations TunnelDeck uses.</summary>
public static class Paths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TunnelDeck");

    public static string CoreDir { get; } = Path.Combine(DataDir, "core");

    public static string SingBoxExe { get; } = Path.Combine(CoreDir, "sing-box.exe");

    public static string StateFile { get; } = Path.Combine(DataDir, "state.json");

    public static string GeneratedConfig { get; } = Path.Combine(DataDir, "config.json");

    public static string LogFile { get; } = Path.Combine(DataDir, "tunneldeck.log");

    public static string SubscriptionLog { get; } = Path.Combine(DataDir, "subscription.log");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CoreDir);
    }
}
