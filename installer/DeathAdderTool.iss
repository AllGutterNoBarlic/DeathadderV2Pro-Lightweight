; Inno Setup script for DeathAdderTool.
; Build:  iscc /DAppVersion=1.0.0 installer\DeathAdderTool.iss
; Expects the self-contained folder build in dist\self-contained\ (see "Building from source" in README.md).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "DeathAdderTool"
#define AppExe "DeathAdderTool.exe"

[Setup]
AppId={{68294FA9-8556-436B-96A7-3585FD0D1F2A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName} contributors
AppPublisherURL=https://github.com/AllGutterNoBarlic/DeathadderV2Pro-Lightweight
AppSupportURL=https://github.com/AllGutterNoBarlic/DeathadderV2Pro-Lightweight/issues
AppUpdatesURL=https://github.com/AllGutterNoBarlic/DeathadderV2Pro-Lightweight/releases
AppComments=Unofficial utility for the Razer DeathAdder V2 Pro. Not affiliated with Razer.
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppName} contributors
VersionInfoDescription={#AppName} Setup (unofficial DeathAdder V2 Pro utility)
VersionInfoProductName={#AppName}
VersionInfoCopyright=Copyright (c) 2026 {#AppName} contributors. MIT License.
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install: no admin rights needed.
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=DeathAdderTool-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Tasks]
Name: "autostart"; Description: "Start {#AppName} when Windows starts"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
; Plain folder build (Microsoft-signed runtime files + small app exe), not a self-extracting bundle.
Source: "..\dist\self-contained\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Restore DPI buttons"; Filename: "{app}\{#AppExe}"; Parameters: "--restore"; Comment: "Put the factory DPI up/down function back on the mouse"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Start with Windows" toggle uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: autostart; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent
; Silent installs/upgrades: start it quietly in the tray so the DPI buttons keep working.
Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Flags: nowait skipifnotsilent

[UninstallRun]
; Ask the running tray app to exit (no force-kill), then give the DPI buttons their normal function back.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall"; Flags: waituntilterminated; RunOnceId: "Uninstall"

[Code]
// Upgrading over a running copy: ask it to exit politely first (the tray app has no window for Restart Manager).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\{#AppName}"
