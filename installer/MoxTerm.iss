; MoxTerm 安装包脚本（Inno Setup 6）
; 用法：ISCC.exe d:\MoxTerm\installer\MoxTerm.iss
; 前置：已执行 dotnet publish 到 ..\dist\app（自包含、含 Windows App SDK 运行时）

#define MyAppName "MoxTerm"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MoxTerm"
#define MyAppExeName "MoxTerm.exe"

[Setup]
AppId={{6E1B7B52-1D0E-4C2B-9A15-3C7F2D4A8E60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 每用户安装：无需管理员权限，装到 %LOCALAPPDATA%\Programs
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=MoxTerm-Setup-{#MyAppVersion}
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
