using System.Diagnostics;
using System.Security.Principal;

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

        var user = WindowsIdentity.GetCurrent().Name;               // DOMAIN\user
        var task = ElevationService.StartupTask;
        const string sddl = "D:(A;;GRGX;;;BU)(A;;GA;;;BA)(A;;GA;;;SY)";

        // Logon trigger, current user with highest privileges, starts minimized to
        // tray; the SD lets a non-elevated launch trigger it (no UAC prompt).
        var ps =
            "$ErrorActionPreference='Stop';" +
            "$t=New-ScheduledTaskTrigger -AtLogOn;" +
            $"$a=New-ScheduledTaskAction -Execute '{exe}' -Argument '--tray';" +
            $"$p=New-ScheduledTaskPrincipal -UserId '{user}' -LogonType Interactive -RunLevel Highest;" +
            "$s=New-ScheduledTaskSettingsSet -MultipleInstances Parallel -ExecutionTimeLimit ([TimeSpan]::Zero);" +
            "$s.DisallowStartIfOnBatteries=$false;$s.StopIfGoingOnBatteries=$false;" +
            $"Register-ScheduledTask -TaskName '{task}' -Trigger $t -Action $a -Principal $p -Settings $s -Force;" +
            "$svc=New-Object -ComObject Schedule.Service;$svc.Connect();" +
            $"$svc.GetFolder('\\').GetTask('{task}').SetSecurityDescriptor('{sddl}',0)";

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
