; Inno Setup script for TunnelDeck.
; Build:  ISCC.exe installer\TunnelDeck.iss
; Expects:
;   ..\dist\TunnelDeck.exe                     (published single-file app)
;   assets\proxifyre\*                          (ProxiFyre binaries, bundled)
;   assets\Windows.Packet.Filter.x64.msi        (network filter driver)

#define AppName "TunnelDeck"
#define AppVersion "1.3.2"
#define AppPublisher "vladbogun1"
#define AppExe "TunnelDeck.exe"

[Setup]
AppId={{D3A7C1E2-9F4B-4C8A-B6D2-7E1F0A9C3B55}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=Output
OutputBaseFilename=TunnelDeck-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Admin required: installs a network-filter driver and the app runs elevated.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Запускать TunnelDeck при входе в Windows"; GroupDescription: "Автозапуск:"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "assets\proxifyre\*"; DestDir: "{app}\proxifyre"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "assets\Windows.Packet.Filter.x64.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "assets\register-tasks.ps1"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
; Shortcuts point at the exe (asInvoker). Double-click starts a non-elevated launcher
; that triggers the scheduled task → an elevated instance, with no UAC prompt.
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Autostart is a scheduled task now — drop any stale Run entry from pre-1.3.0 installs.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "TunnelDeck"; Flags: deletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "TunnelDeck"; Flags: deletevalue

[Run]
; Silently install the Windows Packet Filter driver (required by ProxiFyre).
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\Windows.Packet.Filter.x64.msi"" /qn /norestart"; \
  StatusMsg: "Установка сетевого драйвера…"; Flags: waituntilterminated
; Register the elevation tasks (on-demand + optional logon autostart) so launches skip UAC.
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{tmp}\register-tasks.ps1"" -ExePath ""{app}\{#AppExe}""{code:AutostartArg}"; \
  StatusMsg: "Настройка запуска без запроса прав…"; Flags: runhidden waituntilterminated
; First launch (elevated child of the installer, so no prompt).
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the app and remove the scheduled tasks before uninstalling.
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExe} /t"; Flags: runhidden; RunOnceId: "killapp"
Filename: "taskkill.exe"; Parameters: "/f /im ProxiFyre.exe /t"; Flags: runhidden; RunOnceId: "killpf"
Filename: "taskkill.exe"; Parameters: "/f /im sing-box.exe /t"; Flags: runhidden; RunOnceId: "killsb"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""TunnelDeck"" /f"; Flags: runhidden; RunOnceId: "deltask1"
Filename: "schtasks.exe"; Parameters: "/delete /tn ""TunnelDeck-Startup"" /f"; Flags: runhidden; RunOnceId: "deltask2"

[Messages]
russian.WelcomeLabel2=Программа установит [name/ver] на ваш компьютер.%n%nTunnelDeck направляет через VPN только выбранные вами приложения. Будет установлен сетевой драйвер (Windows Packet Filter), необходимый для работы.

[Code]
// Appends " -Autostart" to the task-registration command when the autostart task is ticked.
function AutostartArg(Param: String): String;
begin
  if WizardIsTaskSelected('autostart') then
    Result := ' -Autostart'
  else
    Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  // Close a running instance BEFORE copying files, otherwise the locked .exe would
  // not be replaced and the user would keep running the old version.
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/f /im TunnelDeck.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/f /im ProxiFyre.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill.exe', '/f /im sing-box.exe /t', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(700);
  end;
end;
