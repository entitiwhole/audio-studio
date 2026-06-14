; Bnote Installer Script
; Inno Setup 6

#define MyAppName "Bnote"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "PRYTEK Vision"
#define SourcePath ".\publish"

[Setup]
AppId=7C5E1F2A-8B4D-4E3F-A2B1-C9D0E5F6A7B3
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={pf}\Bnote
DisableProgramGroupPage=yes
DefaultGroupName=Bnote
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
OutputDir=.\Output
OutputBaseFilename=Bnote-Setup-{#MyAppVersion}
PrivilegesRequired=admin
UninstallDisplayIcon={app}\AudioStudio.exe
VersionInfoVersion=1.1.0.0
VersionInfoCompany=PRYTEK Vision
VersionInfoDescription=Bnote Installer
VersionInfoProductName=Bnote
VersionInfoProductVersion=1.1.0.0
AllowNoIcons=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenu"; Description: "Создать ярлык в меню Пуск"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourcePath}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Bnote"; Filename: "{app}\AudioStudio.exe"; Tasks: startmenu
Name: "{autodesktop}\Bnote"; Filename: "{app}\AudioStudio.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AudioStudio.exe"; Description: "Запустить Bnote"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
