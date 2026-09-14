; WinNutCPlus installer script (Inno Setup 6+).
; Replaces the legacy Setup.vdproj (VS Installer Projects, obsolete/unsupported by modern
; tooling). Packages the self-contained publish output of WinNutCPlus.App.
;
; Build steps (repeat per architecture — x64 and arm64 are separate installers, not one
; combined package, since dotnet publish's self-contained output is architecture-specific):
;   1. dotnet publish ..\src\WinNutCPlus.App\WinNutCPlus.App.csproj -c Release -r win-x64 ^
;        --self-contained true -p:PublishSingleFile=false -o ..\publish\win-x64
;      (swap win-x64 for win-arm64 for the ARM64 build)
;   2. iscc /DMyAppVersion=1.2.3 /DMyAppArch=x64 winnutcplus.iss
;      (or /DMyAppArch=arm64 — omit MyAppArch entirely for a local x64 default build)
;
; The UpdateChecker (WinNutCPlus.Core.Update.UpdateChecker) looks for a GitHub release asset
; ending in ".exe" whose name contains the running process's architecture ("x64"/"arm64"),
; falling back to the first ".exe" asset for older releases that only ever shipped one —
; point release automation at the installer(s) this script produces, one per architecture.

#define MyAppName "WinNutCPlus"
#define MyAppPublisher "RikoDEV"
#define MyAppURL "https://github.com/RikoDEV/WinNutCPlus"
#define MyAppExeName "WinNutCPlus.exe"
; Version is passed in from the build (iscc /DMyAppVersion=1.2.3 winnutcplus.iss); default for local builds:
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
; Architecture is passed in the same way (iscc /DMyAppArch=arm64 winnutcplus.iss); default: x64.
#ifndef MyAppArch
  #define MyAppArch "x64"
#endif
#if MyAppArch == "arm64"
  #define MyAppPublishDir "win-arm64"
  #define MyAppArchAllowed "arm64"
#else
  #define MyAppPublishDir "win-x64"
  #define MyAppArchAllowed "x64compatible"
#endif

[Setup]
AppId={{B6E2C6C8-6E7A-4C7B-9C2E-6B6C2C7A9F31}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Installer output filename; matched by the release-asset ".exe" lookup in UpdateChecker, which
; also keys off the "-x64"/"-arm64" suffix to pick the asset matching the running architecture.
OutputBaseFilename=WinNutCPlus-Setup-{#MyAppVersion}-{#MyAppArch}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#MyAppArchAllowed}
ArchitecturesInstallIn64BitMode={#MyAppArchAllowed}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\WinNutCPlus.App\Assets\Icons\WinNut.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchatstartup"; Description: "Start WinNutCPlus automatically when you sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; "Start with Windows" is normally self-managed by the app's Preferences > Miscellaneous tab
; (writes the same Run key at runtime). This entry only covers the installer's own opt-in
; checkbox for a first-run default; the app's own setting is the source of truth afterward.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinNutCPlus"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: launchatstartup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Settings, logs and crash reports (AppPaths.ResolveDataDirectory's default, unless the user ran
; with -PersistDataInStartupPath, in which case they already live under {app} and are removed
; with it). Uninstall should leave nothing behind, so this is removed unconditionally rather than
; kept around for a future reinstall.
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[Code]
// The app can run minimized to the system tray (no visible window) via MinimizeToTray, so
// Setup/Uninstall's own file-locking detection has nothing to prompt the user to close. Kill it
// outright instead, both before an upgrade overwrites its files and before Uninstall removes
// them — otherwise the locked exe/dlls are silently skipped, leaving stale files behind.
procedure KillRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM "{#MyAppExeName}"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);

  // taskkill returns once the process is reported terminated; give the OS a brief moment to
  // finish tearing down its open file handles before we try to touch the same files.
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  KillRunningApp();
  Result := True;
end;
