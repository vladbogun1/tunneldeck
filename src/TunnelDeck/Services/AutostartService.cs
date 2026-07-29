using System.Diagnostics;

namespace TunnelDeck.Services;

/// <summary>
/// "Start with Windows" via a logon-triggered scheduled task that runs elevated with
/// no UAC prompt (registry Run entries can't launch the app elevated silently). The
/// app itself is elevated when this runs, so it can create/remove the task.
/// </summary>
public static class AutostartService
{
    public static bool IsEnabled() =>
        Run("schtasks.exe", $"/query /tn \"{ElevationService.StartupTask}\"") == 0;

    public static void SetEnabled(bool enabled)
    {
        if (enabled) Create();
        else Run("schtasks.exe", $"/delete /tn \"{ElevationService.StartupTask}\" /f");
    }

    private static void Create()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) return;

        // Logon trigger, highest privileges, starts minimized to tray.
        var ps =
            "$ErrorActionPreference='Stop';" +
            "$t=New-ScheduledTaskTrigger -AtLogOn;" +
            $"$a=New-ScheduledTaskAction -Execute '{exe}' -Argument '--tray';" +
            "$p=New-ScheduledTaskPrincipal -GroupId 'S-1-5-32-545' -RunLevel Highest;" +
            "$s=New-ScheduledTaskSettingsSet -MultipleInstances Parallel -ExecutionTimeLimit ([TimeSpan]::Zero);" +
            "$s.DisallowStartIfOnBatteries=$false;$s.StopIfGoingOnBatteries=$false;" +
            $"Register-ScheduledTask -TaskName '{ElevationService.StartupTask}' -Trigger $t -Action $a -Principal $p -Settings $s -Force";

        Run("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
    }

    private static int Run(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return -1;
            p.WaitForExit(15000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch { return -1; }
    }
}
