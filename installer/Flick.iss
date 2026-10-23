; Flick per-user Windows installer. Retain user files on uninstall.
#define MyAppName "Flick"
#define MyAppVersion "1.0.5"
#define MyAppExeName "Flick.exe"

[Setup]
AppId={{F3C41F96-EF62-4CB1-A292-91EF3F6C18E8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoProductName=Flick
VersionInfoProductTextVersion=1.0.5-preview.1
AppVerName=Flick 1.0.5 — Windows GPU Compatibility Candidate
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
; Official signed builds invoke the host-provided flicksign SignTool through
; the Inno compiler and sign Setup AND Uninstaller. No signing certificate is
; embedded in this repository. Explicit unsigned-preview builds stay unsigned.
#ifdef FLICK_REQUIRE_TRUSTED_SIGNING
SignTool=flicksign
SignedUninstaller=yes
#endif
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=Flick-Setup
PrivilegesRequired=lowest
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AppMutex=LocalClip_SingleInstance_UserLock
MinVersion=10.0
; Only native Windows x64 is supported. ARM and 32-bit Windows cannot
; execute the current x64 capture host and bundled encoder runtime.
ArchitecturesAllowed=x64os
InfoBeforeFile=PREVIEW-SYSTEM-REQUIREMENTS.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\dist\Flick-v1.0.5-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Flick"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\Uninstall Flick"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Flick"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Flick"; Flags: nowait postinstall skipifsilent; Check: IsNormalInstall

Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsFlickUpdate

[Code]
var
  RemoveFlickProfile: Boolean;

// The updater never kills or closes capture processes. It launches Setup,
// then Flick exits cleanly; Setup waits only for that exact parent PID.
function OpenProcess(Access: LongWord; Inherit: Boolean; Pid: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function IsFlickUpdate(): Boolean;
begin
  Result := ExpandConstant('{param:FLICKUPDATE|0}') = '1';
end;

function IsNormalInstall(): Boolean;
begin
  Result := not IsFlickUpdate();
end;

function InitializeSetup(): Boolean;
var
  ParentPid: Integer;
  ParentHandle: THandle;
  WaitResult: LongWord;
begin
  Result := True;
  if not IsFlickUpdate() then Exit;
  ParentPid := StrToIntDef(ExpandConstant('{param:FLICKWAITPID|0}'), 0);
  if ParentPid <= 0 then begin
    Result := False;
    Exit;
  end;
  ParentHandle := OpenProcess($00100000, False, ParentPid);
  if ParentHandle = 0 then Exit; // Already exited; AppMutex still protects files.
  Log('Flick update: waiting for parent PID ' + IntToStr(ParentPid));
      WaitResult := WaitForSingleObject(ParentHandle, 120000);
  CloseHandle(ParentHandle);
  if WaitResult <> 0 then begin
    MsgBox('Flick is still closing. No update was installed. Try again after Flick has quit.', mbError, MB_OK);
    Result := False;
  end;
  if Result then Log('Flick update: parent exited.');
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  RemoveFlickProfile := False;
  // Silent/unattended Windows removal keeps all user data.
  // A human may separately opt in to removing app-owned metadata/cache.
  if not UninstallSilent then
    RemoveFlickProfile :=
      MsgBox('Also remove Flick settings, the clip-library index, thumbnails and temporary cache?' +
        Chr(13) + Chr(10) + Chr(13) + Chr(10) +
        'This cannot be undone. Original recordings, exports, and imported clips will NOT be deleted.' +
        Chr(13) + Chr(10) + Chr(13) + Chr(10) +
        'Choose No to keep your preferences and library for a later reinstall.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupValue: String;
  RunKey: String;
  ProfileDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  // Remove only a startup entry belonging to this installed executable.
  RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
  if RegQueryStringValue(HKCU32, RunKey, 'Flick', StartupValue) then
    if Pos(Lowercase(ExpandConstant('{app}\Flick.exe')),
      Lowercase(StartupValue)) > 0 then
      RegDeleteValue(HKCU32, RunKey, 'Flick');
  if RegQueryStringValue(HKCU64, RunKey, 'Flick', StartupValue) then
    if Pos(Lowercase(ExpandConstant('{app}\Flick.exe')),
      Lowercase(StartupValue)) > 0 then
      RegDeleteValue(HKCU64, RunKey, 'Flick');

  if not RemoveFlickProfile then
    Exit;

  ProfileDir := ExpandConstant('{localappdata}\LocalClip');
  // Explicit allowlist. NEVER delete the full profile recursively, because
  // it can include unknown user files or old installations.
  DeleteFile(ProfileDir + '\settings.json');
  DeleteFile(ProfileDir + '\settings.json.tmp');
  DeleteFile(ProfileDir + '\clips_index.json');
  DeleteFile(ProfileDir + '\clips_index.json.tmp');
  DeleteFile(ProfileDir + '\connected_sources.json');
  DeleteFile(ProfileDir + '\connected_sources.json.tmp');
  DelTree(ProfileDir + '\Thumbnails', True, True, True);
  DelTree(ProfileDir + '\Temp', True, True, True);
  DelTree(ProfileDir + '\PreviewCache', True, True, True);
  RemoveDir(ProfileDir); // Succeeds only when empty; preserves unknown data.
end;
