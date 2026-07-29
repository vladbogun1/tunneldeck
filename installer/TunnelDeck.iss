; Inno Setup script for TunnelDeck.
; Build:  ISCC.exe installer\TunnelDeck.iss
; Expects:
;   ..\dist\TunnelDeck.exe                     (published single-file app)
;   assets\proxifyre\*                          (ProxiFyre binaries, bundled)
;   assets\Windows.Packet.Filter.x64.msi        (network filter driver)

#define AppName "TunnelDeck"
#define AppVersion "1.2.3"
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

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Удалить {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "TunnelDeck"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Silently install the Windows Packet Filter driver (required by ProxiFyre).
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\Windows.Packet.Filter.x64.msi"" /qn /norestart"; \
  StatusMsg: "Установка сетевого драйвера…"; Flags: waituntilterminated
; shellexec so the requireAdministrator app launches via ShellExecute (UAC) instead of
; CreateProcess (which fails with error 740 "operation requires elevation").
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Stop the app before uninstalling.
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExe} /t"; Flags: runhidden; RunOnceId: "killapp"
Filename: "taskkill.exe"; Parameters: "/f /im ProxiFyre.exe /t"; Flags: runhidden; RunOnceId: "killpf"
Filename: "taskkill.exe"; Parameters: "/f /im sing-box.exe /t"; Flags: runhidden; RunOnceId: "killsb"

[Messages]
russian.WelcomeLabel2=Программа установит [name/ver] на ваш компьютер.%n%nTunnelDeck направляет через VPN только выбранные вами приложения. Будет установлен сетевой драйвер (Windows Packet Filter), необходимый для работы.

[Code]
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
