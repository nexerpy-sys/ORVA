; =============================================================================
; ORVA — Pharmacy Billing & Inventory (by Rindle Works)
; Inno Setup 6 Release Installer Script
; =============================================================================
; Safety guarantees:
;   - Installs application binaries only to {autopf}\Rindle Works\ORVA
;   - Verifies .NET Framework 4.8 (Release >= 528040) before installation
;   - Checks Microsoft Edge WebView2 Runtime (installs if bootstrapper bundled,
;     or informs the user cleanly while native WPF fallback stays 100% ready)
;   - NEVER deletes or overwrites %ProgramData%\Rindle Works\ORVA\ (SQLite DB)
;   - NEVER deletes or overwrites user Documents\<Shop Name>\ (backups & exports)
;   - Supports silent/clean upgrade over previous versions while preserving data
; =============================================================================

#define MyAppName "ORVA"
#define MyAppFullName "ORVA — Pharmacy Billing & Inventory"
#define MyAppVersion "4.0.0"
#define MyAppPublisher "Rindle Works"
#define MyAppExeName "ORVA.exe"

; Relative path to SDK-style net48 Release build output (override via /DSourceBinDir=... in ISCC)
#ifndef SourceBinDir
  #define SourceBinDir "..\src\Orva.Desktop\bin\Release\net48"
#endif

[Setup]
AppId={{8D4F6E2A-9B3C-4E1F-A7D2-5C8E1B4A9F03}
AppName={#MyAppFullName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppPublisher}\{#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=ORVA_Setup_{#MyAppVersion}_x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
UninstallDisplayName={#MyAppFullName}
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Application executable, config, managed assemblies, SQLite interop, and bundled assets
Source: "{#SourceBinDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.xml,*.db,*.db-wal,*.db-shm,*.shopbackup,*.sqlite,*.sqlite-wal,*.sqlite-shm"
; Optional bundled WebView2 Evergreen Bootstrapper (place MicrosoftEdgeWebview2Setup.exe in installer\ to auto-install offline/online)
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist; Check: not IsWebView2Installed

[Dirs]
; Ensure shared local data folder exists with standard Users modify permissions
; Marked uninsneveruninstall so shop database and logs are NEVER removed on uninstall
Name: "{commonappdata}\{#MyAppPublisher}\{#MyAppName}"; Permissions: users-modify; Flags: uninsneveruninstall

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppFullName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppFullName}"

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated skipifdoesntexist; Check: not IsWebView2Installed
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Checks for .NET Framework 4.8 or later (Release DWORD >= 528040)
function IsDotNet48OrLaterInstalled(): Boolean;
var
  ReleaseVersion: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', ReleaseVersion) then
  begin
    Result := (ReleaseVersion >= 528040);
  end;
end;

// Checks official Microsoft Edge WebView2 Runtime registry keys (per-machine 64/32-bit and per-user)
function IsWebView2Installed(): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
  begin
    Result := True;
    Exit;
  end;
  if RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
  begin
    Result := True;
    Exit;
  end;
  if RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0') then
  begin
    Result := True;
    Exit;
  end;
end;

function InitializeSetup(): Boolean;
begin
  if not IsDotNet48OrLaterInstalled() then
  begin
    MsgBox(
      'ORVA requires Microsoft .NET Framework 4.8 or later.' + #13#10 + #13#10 +
      'Please install .NET Framework 4.8 (or run Windows Update on Windows 10) and re-run this installer.',
      mbCriticalError,
      MB_OK
    );
    Result := False;
    Exit;
  end;

  if (not IsWebView2Installed()) and (not WizardSilent()) then
  begin
    MsgBox(
      'Microsoft Edge WebView2 Runtime was not detected on this PC.' + #13#10 + #13#10 +
      'ORVA will install normally and all offline billing, inventory, GST, and backup screens will work immediately using the built-in native WPF fallback.' + #13#10 +
      'If a WebView2 installer is bundled, setup will install it automatically; otherwise you can install Microsoft Edge WebView2 Runtime later to enable the animated Home and About Us showcase.',
      mbInformation,
      MB_OK
    );
  end;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    MsgBox(
      'ORVA has been uninstalled.' + #13#10 + #13#10 +
      'Your shop database (%ProgramData%\Rindle Works\ORVA) and backups in your Documents folder were preserved and NOT deleted.',
      mbInformation,
      MB_OK
    );
  end;
end;
