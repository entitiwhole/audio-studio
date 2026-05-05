; BF^Studio Installer Script
; Inno Setup 6

#define MyAppName "BF^Studio"
#define MyAppVersion "1.0.2"
#define MyAppPublisher "PRYTEK Vision"
#define SourcePath "E:\Atest1.0\AudioStudio\AudioStudio\bin\Release\App"

[Setup]
AppId=7C5E1F2A-8B4D-4E3F-A2B1-C9D0E5F6A7B3
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={pf}\BF^Studio
DisableProgramGroupPage=yes
DefaultGroupName=BF^Studio
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=.\Output
OutputBaseFilename=BFStudio-Setup-{#MyAppVersion}
PrivilegesRequired=admin
UninstallDisplayIcon={app}\BFStudio.exe
SetupIconFile=E:\Atest1.0\AudioStudio\AudioStudio\hd_067e60e1d37959fea8c10910f1bec3f4-_1_-1.ico
VersionInfoVersion=1.0.0.0
VersionInfoCompany=PRYTEK Vision
VersionInfoDescription=BF^Studio Installer
VersionInfoProductName=BF^Studio
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
Name: "{autoprograms}\BF^Studio"; Filename: "{app}\BFStudio.exe"; Tasks: startmenu
Name: "{autodesktop}\BF^Studio"; Filename: "{app}\BFStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\BFStudio.exe"; Description: "Запустить BF^Studio"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
