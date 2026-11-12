; The Windows installer. Built by the release workflow, once per architecture:
;
;   iscc /DAppVersion=1.2.0 /DFileVersion=1.2.0 /DArch=x64 /DSourceDir=<publish dir> /DOutputDir=<dir> ShinyNotch.iss
;
; Per-user, into %LOCALAPPDATA%\Programs\Shiny Notch, so nobody needs to be an administrator to install
; it, and because the first launch turns on open at login by writing this exe's path into the HKCU Run
; key: the path has to stay put across upgrades.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<publish output>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppExe "ShinyNotch.Windows.exe"

[Setup]
; Never change this: it is how an upgrade finds the copy it is replacing.
AppId={{04EE38DC-7C1D-4449-9C64-B366E05CAE22}
AppName=Shiny Notch
AppVersion={#AppVersion}
AppVerName=Shiny Notch {#AppVersion}
AppPublisher=ShinySoft Technologies
AppPublisherURL=https://github.com/ShinySoftware/ShinyNotch
AppSupportURL=https://github.com/ShinySoftware/ShinyNotch/issues
AppUpdatesURL=https://github.com/ShinySoftware/ShinyNotch/releases
VersionInfoVersion={#FileVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\Shiny Notch
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Shiny Notch
OutputDir={#OutputDir}
OutputBaseFilename=ShinyNotch-{#AppVersion}-windows-{#Arch}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The notch keeps running with no window of its own to close (it quits from Settings), so a polite
; Restart Manager close may not take; force it rather than leave the old files held open.
CloseApplications=force
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Shiny Notch"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Shiny Notch"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Not written here - the app writes it (Shiny.Core's startup installer, named by the Identifier in
; ShinyNotchHosting) on first launch and from Settings. Only removed on uninstall, so an uninstalled app
; is not started at the next sign-in.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "net.shiny.notch"; ValueType: none; Flags: uninsdeletevalue

[Run]
; skipifsilent: there's no in-app updater on Windows, so a silent install is someone deploying it, who
; decides when it starts. Add the launch back for silent runs if the app ever updates itself this way.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,Shiny Notch}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The notch is always running, which would hold its files open.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopShinyNotch"

; Settings, notes and photos in the user's profile are left alone: an uninstall to reinstall should not
; take them with it.
