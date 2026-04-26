; AudioStudio Installer Script
; Inno Setup 6

#define MyAppName "AudioStudio"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "AudioStudio"
#define SourcePath "E:\Atest1.0\AudioStudio\AudioStudio\publish"

[Setup]
AppId=7C5E1F2A-8B4D-4E3F-A2B1-C9D0E5F6A7B3
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={pf}\AudioStudio
DisableProgramGroupPage=yes
DefaultGroupName=AudioStudio
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=.
OutputBaseFilename=AudioStudio-Setup
PrivilegesRequired=admin
UninstallDisplayIcon={app}\AudioStudio.exe
SetupIconFile=..\AudioStudio\app.ico
VersionInfoVersion=1.0.0.0
VersionInfoCompany=AudioStudio
VersionInfoDescription=AudioStudio Installer
VersionInfoProductName=AudioStudio
VersionInfoProductVersion=1.0.0.0

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Создать ярлык в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\AudioStudio"; Filename: "{app}\AudioStudio.exe"; Tasks: startmenu
Name: "{autodesktop}\AudioStudio"; Filename: "{app}\AudioStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AudioStudio.exe"; Description: "Запустить AudioStudio"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
