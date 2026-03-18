#define MyAppName "HA PC Remote"
#define MyAppVersion GetEnv("APP_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.0.0"
#endif
#define MyAppPublisher "NeskireDK"
#define MyAppURL "https://github.com/NeskireDK/ha-pc-remote-service"
#define TrayExeName "HaPcRemote.Tray.exe"
#define ServicePort "5000"

[Setup]
AppId={{7A3B5C1D-9E2F-4A6B-8C0D-1E3F5A7B9C2D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={autopf}\{#MyAppName}
DisableDirPage=no
DisableProgramGroupPage=yes
InfoBeforeFile=app-info.txt
OutputBaseFilename=HaPcRemoteService-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupIconFile=..\resources\windows\pcremote.ico
UninstallDisplayIcon={app}\{#TrayExeName}
WizardStyle=modern

[Files]
Source: "..\publish\win-x64\{#TrayExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\tools"
Name: "{app}\monitor-profiles"

[Icons]
Name: "{commonstartup}\HA PC Remote Tray"; Filename: "{app}\{#TrayExeName}"; Comment: "HA PC Remote system tray"
Name: "{commonstartmenu}\{#MyAppName}"; Filename: "{app}\{#TrayExeName}"; Comment: "HA PC Remote system tray"

[Run]
; Interactive install: pre-checked checkbox "Launch HA PC Remote"
Filename: "{app}\{#TrayExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Silent install: always launch after install
Filename: "{app}\{#TrayExeName}"; Flags: nowait skipifnotsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"

[Code]
const
  DOTNET_DESKTOP_URL = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';
  DOTNET_REG_KEY     = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

procedure KillTrayApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM {#TrayExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function RunPowerShell(const Cmd: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -Command "' + Cmd + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := Result and (ResultCode = 0);
end;

function DownloadAndExtract(const URL, ZipName, DestDir: String): Boolean;
var
  TmpZip: String;
begin
  TmpZip := ExpandConstant('{tmp}\' + ZipName);
  Result := RunPowerShell(
    '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
    'Invoke-WebRequest -UseBasicParsing -Uri ''' + URL + ''' -OutFile ''' + TmpZip + '''; ' +
    'Expand-Archive -LiteralPath ''' + TmpZip + ''' -DestinationPath ''' + DestDir + ''' -Force');
end;

procedure ExecHidden(const FileName, Params: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant(FileName), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function IsDotNet10DesktopInstalled: Boolean;
var
  Keys: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubKeyNames(HKLM, DOTNET_REG_KEY, Keys) then
    for I := 0 to High(Keys) do
      if Copy(Keys[I], 1, 3) = '10.' then begin
        Result := True;
        Break;
      end;
end;

function InstallDotNet10Desktop: Boolean;
var
  TmpExe: String;
  ResultCode: Integer;
begin
  TmpExe := ExpandConstant('{tmp}\dotnet-runtime-win-x64.exe');
  Result := RunPowerShell(
    '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
    'Invoke-WebRequest -UseBasicParsing -Uri ''' + DOTNET_DESKTOP_URL + ''' -OutFile ''' + TmpExe + '''');
  if Result then
    Result := Exec(TmpExe, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
      and (ResultCode = 0);
end;

procedure MigrateConfigIfNeeded;
var
  OldConfig, OldProfilesDir, NewConfigDir, NewConfig, NewProfilesDir: String;
  ProfileRec: TFindRec;
begin
  NewConfigDir := ExpandConstant('{userappdata}\HaPcRemote');
  NewProfilesDir := NewConfigDir + '\monitor-profiles';
  CreateDir(NewConfigDir);
  CreateDir(NewProfilesDir);

  // Migrate config from %ProgramData%\HaPcRemote to %AppData%\HaPcRemote (pre-0.9.4 upgrade)
  OldConfig := ExpandConstant('{commonappdata}\HaPcRemote\appsettings.json');
  NewConfig := NewConfigDir + '\appsettings.json';
  if FileExists(OldConfig) and not FileExists(NewConfig) then
    FileCopy(OldConfig, NewConfig, False);

  // Migrate monitor profiles from {app}\monitor-profiles to %AppData%\HaPcRemote\monitor-profiles (pre-0.9.2 upgrade)
  OldProfilesDir := ExpandConstant('{app}\monitor-profiles');
  if DirExists(OldProfilesDir) then begin
    if FindFirst(OldProfilesDir + '\*.cfg', ProfileRec) then begin
      try
        repeat
          FileCopy(OldProfilesDir + '\' + ProfileRec.Name,
            NewProfilesDir + '\' + ProfileRec.Name, False);
        until not FindNext(ProfileRec);
      finally
        FindClose(ProfileRec);
      end;
    end;
  end;
end;

// --- Install lifecycle ---

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if not IsDotNet10DesktopInstalled then begin
    WizardForm.StatusLabel.Caption := 'Installing .NET 10 Desktop Runtime...';
    if not InstallDotNet10Desktop then begin
      Result := '.NET 10 Desktop Runtime is required but could not be installed. ' +
        'Please install it manually from https://dotnet.microsoft.com/download/dotnet/10.0 and retry.';
      Exit;
    end;
  end;

  KillTrayApp;
  MigrateConfigIfNeeded;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ToolsDir: String;
begin
  if CurStep = ssPostInstall then begin
    ToolsDir := ExpandConstant('{app}\tools');

    WizardForm.StatusLabel.Caption := 'Downloading SoundVolumeView...';
    DownloadAndExtract(
      'https://www.nirsoft.net/utils/soundvolumeview-x64.zip',
      'soundvolumeview-x64.zip', ToolsDir);

    // Firewall rule for Tray's Kestrel server
    WizardForm.StatusLabel.Caption := 'Adding firewall rule...';
    ExecHidden('{sys}\netsh.exe',
      'advfirewall firewall add rule name="{#MyAppName}"' +
      ' dir=in action=allow protocol=TCP localport={#ServicePort}' +
      ' program="' + ExpandConstant('{app}\{#TrayExeName}') + '" enable=yes');
  end;
end;

// --- Uninstall lifecycle ---

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    KillTrayApp;
    ExecHidden('{sys}\netsh.exe',
      'advfirewall firewall delete rule name="{#MyAppName}"');
  end;
end;
