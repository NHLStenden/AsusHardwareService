#define AppName "ASUS Hardware Service"
#define AppPublisher "NHL Stenden"
#define AppExeName "AsusHardwareService.exe"
#define ServiceName "AsusHardwareService"
#define ServiceDisplayName "ASUS Hardware Service"
#define LegacyServiceName "ASUS Hardware Service"

#ifndef AppVersion
  #error AppVersion must be supplied by build/Build.ps1
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
; Never change AppId after the first public installer release: it is the upgrade identity.
AppId={{9FD9D954-CBC5-43EE-B016-474A69F2C60A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\ASUS Hardware Service
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=AsusHardwareService-{#AppVersion}-win-x64-setup
SetupLogging=yes
CloseApplications=no
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Files]
; Program Files contains replaceable application binaries only.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "appsettings.json"; Flags: ignoreversion recursesubdirs createallsubdirs

; ProgramData owns mutable machine configuration. Seed it only on the first install and preserve it on uninstall.
Source: "{#PublishDir}\appsettings.json"; DestDir: "{commonappdata}\AsusHardwareService"; DestName: "appsettings.json"; Flags: onlyifdoesntexist uninsneveruninstall

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#ServiceName}"""; Flags: runhidden waituntilterminated; RunOnceId: "StopAsusHardwareService"
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#ServiceName}"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteAsusHardwareService"

[Code]
function ServiceExists(const Name: String): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\' + Name);
end;

function Quote(const Value: String): String;
begin
  Result := '"' + Value + '"';
end;

procedure RunSc(const Parameters: String; const AllowFailure: Boolean);
var
  ResultCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\sc.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if not AllowFailure then
      RaiseException('Could not execute Service Control Manager command: ' + Parameters);
    exit;
  end;

  if (ResultCode <> 0) and (not AllowFailure) then
    RaiseException(Format('Service Control Manager command failed (%d): %s', [ResultCode, Parameters]));
end;

procedure StopService(const Name: String);
begin
  if ServiceExists(Name) then
  begin
    RunSc('stop ' + Quote(Name), True);
    Sleep(2000);
  end;
end;

procedure RemoveLegacyService;
begin
  { Earlier/manual installs used the display name as the machine service name. }
  if ('{#LegacyServiceName}' <> '{#ServiceName}') and ServiceExists('{#LegacyServiceName}') then
  begin
    StopService('{#LegacyServiceName}');
    RunSc('delete ' + Quote('{#LegacyServiceName}'), True);
    Sleep(1000);
  end;
end;

procedure InstallOrUpdateService;
var
  ExecutablePath: String;
  QuotedBinaryPath: String;
  Parameters: String;
begin
  ExecutablePath := ExpandConstant('{app}\{#AppExeName}');

  { sc.exe receives a quoted executable path as the value of binPath. }
  QuotedBinaryPath := Quote('\"' + ExecutablePath + '\"');

  if not ServiceExists('{#ServiceName}') then
  begin
    Parameters :=
      'create ' + Quote('{#ServiceName}') +
      ' binPath= ' + QuotedBinaryPath +
      ' start= auto' +
      ' DisplayName= ' + Quote('{#ServiceDisplayName}');
    RunSc(Parameters, False);
  end;

  Parameters :=
    'config ' + Quote('{#ServiceName}') +
    ' binPath= ' + QuotedBinaryPath +
    ' start= auto' +
    ' DisplayName= ' + Quote('{#ServiceDisplayName}');
  RunSc(Parameters, False);

  RunSc(
    'description ' + Quote('{#ServiceName}') + ' ' +
    Quote('Controls supported ASUS laptop hardware features and hotkeys.'),
    False);

  RunSc('start ' + Quote('{#ServiceName}'), False);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopService('{#ServiceName}');
  RemoveLegacyService;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallOrUpdateService;
end;
