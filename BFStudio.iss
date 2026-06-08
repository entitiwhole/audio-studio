; BF^Studio Installer Script
; Inno Setup 6

#define MyAppName "BF^Studio"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "PRYTEK Vision"
#define SourcePath ".\publish"

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
UninstallDisplayIcon={app}\AudioStudio.exe
VersionInfoVersion=1.0.3.0
VersionInfoCompany=PRYTEK Vision
VersionInfoDescription=BF^Studio Installer
VersionInfoProductName=BF^Studio
VersionInfoProductVersion=1.0.3.0
AllowNoIcons=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Создать ярлык в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\AudioStudio.exe"; DestDir: "{app}"
Source: "publish\AudioStudio.dll"; DestDir: "{app}"
Source: "publish\AudioStudio.deps.json"; DestDir: "{app}"
Source: "publish\AudioStudio.runtimeconfig.json"; DestDir: "{app}"
Source: "publish\NAudio.dll"; DestDir: "{app}"
Source: "publish\AudioBridge.dll"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\BF^Studio"; Filename: "{app}\AudioStudio.exe"; Tasks: startmenu
Name: "{autodesktop}\BF^Studio"; Filename: "{app}\AudioStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AudioStudio.exe"; Description: "Запустить BF^Studio"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
