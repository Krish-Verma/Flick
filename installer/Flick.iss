; Flick per-user Windows installer. Retain user files on uninstall.
#define MyAppName "Flick"
#define MyAppVersion "1.0.0"
#define MyAppExeName "Flick.exe"

[Setup]
AppId={{F3C41F96-EF62-4CB1-A292-91EF3F6C18E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName=Flick 1.0.0 Preview
AppPublisher=Flick Contributors
AppPublisherURL=https://github.com/Krish-Verma/Flick
AppSupportURL=https://github.com/Krish-Verma/Flick/issues
AppUpdatesURL=https://github.com/Krish-Verma/Flick/releases
DefaultDirName={localappdata}\Programs\Flick
DefaultGroupName=Flick
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UninstallDisplayName=Flick
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\dist\Flick.ico
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Flick-Setup-1.0.0-win-x64-preview
PrivilegesRequired=lowest
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AppMutex=LocalClip_SingleInstance_UserLock
MinVersion=10.0

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\Flick-v1.0.0-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Flick"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Flick"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Flick"; Flags: nowait postinstall skipifsilent
