#ifndef MyAppVersion
#define MyAppVersion "0.2.0"
#endif

#define MyAppName "TwinQuota"
#define MyAppPublisher "myagmb28Dev"
#define MyAppURL "https://github.com/myagmb28Dev/TwinQuota"
#define MyAppExeName "TwinQuota.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{E1B3A368-2B9B-4D1D-A103-6254AC1B7B02}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\win-x64
OutputBaseFilename=TwinQuota-Setup-v{#MyAppVersion}
SetupIconFile=..\src\TwinQuota.Windows\Assets\TwinQuota.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
CloseApplicationsFilter=TwinQuota.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Automatically start TwinQuota on Windows login"; GroupDescription: "Other tasks:"; Flags: unchecked

[Files]
Source: "..\artifacts\win-x64\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
