; AudioStudio Installer Script
; Inno Setup 6

#define MyAppName "AudioStudio"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "PRYTEK Vision"
#define MyAppURL "https://github.com/entitiwhole/audio-studio"
#define SourcePath "..\AudioStudio\bin\Release\net10.0-windows\win-x64"

[Setup]
AppId={{7C5E1F2A-8B4D-4E3F-A2B1-C9D0E5F6A7B4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=Output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppName}.exe
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}.0
SetupIconFile=app.ico
AllowNoIcons=yes
DisableWelcomePage=no
DisableDirPage=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Создать ярлык в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; All application files
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "unins000.exe,unins000.dat,version.txt,BUILD_*.txt"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; WorkingDir: "{app}"; Tasks: startmenu
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppName}.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppName}.exe"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
