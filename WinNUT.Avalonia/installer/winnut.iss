; WinNUT installer script (Inno Setup 6+).
; Replaces the legacy Setup.vdproj (VS Installer Projects, obsolete/unsupported by modern
; tooling). Packages the self-contained publish output of WinNUT.App.
;
; Build steps:
;   1. dotnet publish ..\src\WinNUT.App\WinNUT.App.csproj -c Release -r win-x64 ^
;        --self-contained true -p:PublishSingleFile=false -o ..\publish\win-x64
;   2. Open this file in the Inno Setup Compiler (or run: iscc winnut.iss)
;
; The UpdateChecker (WinNUT.Core.Update.UpdateChecker) looks for a GitHub release asset
; ending in ".exe" — point release automation at the installer this script produces.

#define MyAppName "WinNutCPlus"
#define MyAppPublisher "RikoDEV"
#define MyAppURL "https://github.com/RikoDEV/WinNutCPlus"
#define MyAppExeName "WinNUT.exe"
; Version is passed in from the build (iscc /DMyAppVersion=1.2.3 winnut.iss); default for local builds:
#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
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
; Installer output filename; matched by the release-asset ".exe" lookup in UpdateChecker.
OutputBaseFilename=WinNUT-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\WinNUT.App\Assets\Icons\WinNut.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchatstartup"; Description: "Start WinNutCPlus automatically when you sign in"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; "Start with Windows" is normally self-managed by the app's Preferences > Miscellaneous tab
; (writes the same Run key at runtime). This entry only covers the installer's own opt-in
; checkbox for a first-run default; the app's own setting is the source of truth afterward.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "WinNUT"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: launchatstartup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; Note: settings/logs under %LocalAppData%\WinNUT are deliberately left in place on uninstall
; (so a reinstall/upgrade doesn't lose the user's configuration) rather than auto-deleted.
