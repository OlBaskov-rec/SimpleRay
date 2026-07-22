; Inno Setup script for SimpleRay — packages the portable publish output into a
; per-user installer. Build the portable output first (scripts\publish-portable.ps1),
; then compile:  ISCC.exe scripts\installer.iss
;
; Per-user install (no admin needed to install) so the in-app updater — which runs
; while disconnected, i.e. non-elevated — can replace files in the install folder.
; The app still self-elevates at runtime for TUN.

#define AppName "SimpleRay"
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#define AppPublisher "OlBaskov-rec"
#define AppExe "SimpleRay.exe"
#define AppUrl "https://github.com/OlBaskov-rec/SimpleRay"

[Setup]
AppId={{B7E1F3A2-9C4D-4E6F-8A1B-2D3C4E5F6A7B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=SimpleRay-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\SimpleRay.App\Resources\tray.ico
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "uk"; MessagesFile: "compiler:Languages\Ukrainian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "..\publish\portable\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\portable\core\*"; DestDir: "{app}\core"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\publish\portable\geo\*"; DestDir: "{app}\geo"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
