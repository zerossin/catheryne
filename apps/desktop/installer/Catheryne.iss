#ifndef Payload
  #error Payload is required
#endif
#ifndef Version
  #error Version is required
#endif
#ifndef ProductId
  #define ProductId "Catheryne"
#endif
#ifndef OutputPath
  #define OutputPath "."
#endif
#if ProductId == "Catheryne"
  #define ProductName "Catheryne"
  #define ProductMutex "Local\GenshinCompanion"
  #define ToastActivator 'AppUserModelToastActivatorCLSID: "CE0A4BCD-DB1B-4B55-8B77-964DAFC41A73";'
#else
  #define ProductName "Catheryne Installation Fixture"
  #define ProductMutex "Local\" + ProductId
  #define ToastActivator ""
#endif
[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#Version}
AppPublisher=Catheryne
DefaultDirName={code:DefaultInstallDir}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
DisableDirPage=no
UninstallDisplayIcon={app}\launcher.ico
OutputDir={#OutputPath}
OutputBaseFilename=Catheryne-Setup-{#Version}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex={#ProductMutex}
SetupLogging=yes
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Registry]
Root: HKCU; Subkey: "Software\{#ProductId}"; ValueType: string; ValueName: "InstallLocation"; ValueData: "{app}"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Catheryne.Desktop"; ValueType: string; ValueName: "DisplayName"; ValueData: "Catheryne"; Flags: uninsdeletekey; Check: IsCatheryne
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Catheryne.Desktop"; ValueType: string; ValueName: "IconUri"; ValueData: "{app}\branding\launcher.png"; Check: IsCatheryne
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Catheryne.Desktop"; ValueType: string; ValueName: "CustomActivator"; ValueData: "{{CE0A4BCD-DB1B-4B55-8B77-964DAFC41A73}"; Check: IsCatheryne
Root: HKCU; Subkey: "Software\Classes\CLSID\{{CE0A4BCD-DB1B-4B55-8B77-964DAFC41A73}\LocalServer32"; ValueType: string; ValueName: ""; ValueData: """{app}\GenshinLauncher.exe"" --notification-server"; Flags: uninsdeletekey; Check: IsCatheryne
Root: HKCU; Subkey: "Software\Classes\CLSID\{{CE0A4BCD-DB1B-4B55-8B77-964DAFC41A73}"; Flags: uninsdeletekeyifempty; Check: IsCatheryne
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\GenshinLauncher.exe"; Parameters: "--preview"; WorkingDir: "{app}"; IconFilename: "{app}\launcher.ico"; {#ToastActivator} AppUserModelID: "{#ProductId}.Desktop"
Name: "{userdesktop}\{#ProductName}"; Filename: "{app}\GenshinLauncher.exe"; Parameters: "--preview"; WorkingDir: "{app}"; IconFilename: "{app}\launcher.ico"; {#ToastActivator} AppUserModelID: "{#ProductId}.Desktop"; Tasks: desktopicon
[Run]
Filename: "{app}\GenshinLauncher.exe"; Parameters: "--preview"; Description: "{cm:LaunchProgram,Catheryne}"; Flags: nowait postinstall skipifsilent unchecked
[Code]
var
  PreviousManifest: TArrayOfString;
function IsCatheryne: Boolean;
begin Result := '{#ProductId}' = 'Catheryne'; end;
function DefaultInstallDir(Param: String): String;
var Shell, Link: Variant; Target, Saved: String; I: Integer;
begin
  Result := ExpandConstant('{localappdata}\Programs\{#ProductName}');
  if RegQueryStringValue(HKCU, 'Software\{#ProductId}', 'InstallLocation', Saved) and DirExists(Saved) then begin Result := Saved; exit; end;
  if '{#ProductId}' <> 'Catheryne' then exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    for I := 0 to 1 do begin
      if I=0 then Target:=ExpandConstant('{userprograms}\Catheryne.lnk') else Target:=ExpandConstant('{userdesktop}\Catheryne.lnk');
      if FileExists(Target) then begin
        Link := Shell.CreateShortcut(Target);Target := Link.TargetPath;
        if (CompareText(ExtractFileName(Target),'GenshinLauncher.exe')=0) and FileExists(ExtractFileDir(Target)+'\components.json') then begin Result:=ExtractFileDir(Target);exit;end;
      end;
    end;
  except Log('No legacy shortcut path'); end;
end;
procedure RemoveLegacyShortcuts;
var Shell, Link: Variant; Name, Target: String; I: Integer;
begin
  if '{#ProductId}' <> 'Catheryne' then exit;
  try
    Shell := CreateOleObject('WScript.Shell');
    for I:=0 to 1 do begin
      if I=0 then Name:=ExpandConstant('{userprograms}\Catheryne Settings.lnk') else Name:=ExpandConstant('{userdesktop}\Catheryne Settings.lnk');
      if FileExists(Name) then begin
        Link:=Shell.CreateShortcut(Name);Target:=Link.TargetPath;
        if CompareText(Target,ExpandConstant('{app}\GenshinLauncher.exe'))=0 then DeleteFile(Name);
      end;
    end;
  except Log('Legacy shortcut cleanup skipped'); end;
end;
function OpenEvent(Access: LongWord; Inherit: Boolean; Name: String): THandle;
external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Boolean;
external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
external 'CloseHandle@kernel32.dll stdcall';
procedure StopDaily;
var Handle: THandle;
begin
  Handle := OpenEvent($0002, False, 'Local\Catheryne.Daily.Stop');
  if Handle <> 0 then begin SetEvent(Handle); CloseHandle(Handle); end;
end;
function Busy: Boolean;
var Locator, Service, Processes: Variant;
begin
  Result := True;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\cimv2');
    Processes := Service.ExecQuery('SELECT Name FROM Win32_Process WHERE Name="GenshinImpact.exe" OR Name="YuanShen.exe" OR Name="AkashaScanner.exe" OR Name="InventoryKamera.exe" OR Name="unlockfps_nc.exe" OR Name="unlockfps.exe"');
    Result := Processes.Count > 0;
  except
    Log('Cannot verify active game/scanner processes');
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Busy then begin Result := '원신과 실행 중인 수집 작업을 종료한 뒤 다시 시도해 주세요.'; exit; end;
  StopDaily;
  LoadStringsFromFile(ExpandConstant('{app}\install-manifest.txt'), PreviousManifest);
end;
function SafeRelative(Path: String): Boolean;
begin
  Result := (Path <> '') and (Pos('..', Path) = 0) and (Pos(':', Path) = 0) and (Copy(Path, 1, 1) <> '\') and (Copy(Path, 1, 1) <> '/');
end;
procedure RemoveObsolete;
var Current: TArrayOfString; I, J, Split: Integer; Path, Hash, FullPath: String; Found: Boolean;
begin
  if not LoadStringsFromFile(ExpandConstant('{app}\install-manifest.txt'), Current) then exit;
  for I := 0 to GetArrayLength(PreviousManifest)-1 do begin
    Split := Pos('|', PreviousManifest[I]);
    if Split > 0 then begin
      Path := Copy(PreviousManifest[I], 1, Split-1);
      Hash := Copy(PreviousManifest[I], Split+1, MaxInt);
      if SafeRelative(Path) then begin
        Found := False;
        for J := 0 to GetArrayLength(Current)-1 do
          if Pos(Path+'|', Current[J]) = 1 then Found := True;
        FullPath := ExpandConstant('{app}\') + Path;
        if not Found and FileExists(FullPath) then begin
          if CompareText(GetSHA256OfFile(FullPath), Hash) = 0 then DeleteFile(FullPath);
        end;
      end;
    end;
  end;
end;
procedure CurStepChanged(Step: TSetupStep);
begin
  if Step = ssPostInstall then begin RemoveObsolete;RemoveLegacyShortcuts;end;
end;
function InitializeUninstall: Boolean;
begin
  Result := not Busy;
  if not Result then MsgBox('원신과 실행 중인 수집 작업을 종료한 뒤 다시 시도해 주세요.', mbError, MB_OK)
  else StopDaily;
end;
procedure CurUninstallStepChanged(Step: TUninstallStep);
var Command: String;
begin
  if Step = usUninstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CatheryneDaily', Command) then
      if Pos(Lowercase(ExpandConstant('{app}\')), Lowercase(Command)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CatheryneDaily');
  end;
end;


