; AudioStudio Installer Script
; Inno Setup 6

#define MyAppName "AudioStudio"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "AudioStudio"
#define SourcePath "E:\Atest1.0\AudioStudio\AudioStudio\bin\Release\App"

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
OutputDir=.\Output
OutputBaseFilename=AudioStudio-Setup-{#MyAppVersion}
PrivilegesRequired=admin
UninstallDisplayIcon={app}\AudioStudio.exe
SetupIconFile=E:\Atest1.0\AudioStudio\AudioStudio\hd_067e60e1d37959fea8c10910f1bec3f4-_1_-1.ico
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
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AudioStudio"; Filename: "{app}\AudioStudio.exe"; Tasks: startmenu
Name: "{autodesktop}\AudioStudio"; Filename: "{app}\AudioStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AudioStudio.exe"; Description: "Запустить AudioStudio"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
