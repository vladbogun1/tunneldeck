using System.Diagnostics;
using System.Security.Principal;

namespace TunnelDeck.Services;

/// <summary>
/// The app needs admin rights (ProxiFyre + the packet-filter driver) but must not
/// prompt for UAC on every launch. The installer registers a "highest privileges"
/// scheduled task whose action is this exe; a non-elevated launch simply triggers
/// that task, which starts an elevated instance with no prompt.
/// </summary>
public static class ElevationService
{
    /// <summary>On-demand task used by shortcuts / manual launches.</summary>
    public const string OnDemandTask = "TunnelDeck";

    /// <summary>Logon-triggered task used for "start with Windows" (runs with --tray).</summary>
    public const string StartupTask = "TunnelDeck-Startup";

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>
    /// Trigger the on-demand scheduled task to relaunch elevated (no UAC). Returns
    /// true if the task was started; false if it doesn't exist / couldn't run, in
    /// which case the caller should keep running as-is.
    /// </summary>
    public static bool RelaunchElevated()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/run /tn \"{OnDemandTask}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Last-resort elevation if the scheduled task is missing/broken: relaunch the exe
    /// via ShellExecute "runas" (a normal UAC prompt). Better than running unprivileged
    /// and failing later. Returns true if a new elevated process was launched.
    /// </summary>
    public static bool RelaunchViaUac(string[] args)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(' ', args),
                UseShellExecute = true,
                Verb = "runas"
            };
            return Process.Start(psi) is not null;
        }
        catch { return false; }   // user declined the UAC prompt, etc.
    }
}
