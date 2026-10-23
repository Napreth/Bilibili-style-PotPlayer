#ifndef RepoRoot
  #define RepoRoot ".."
#endif
#ifndef AppVersion
  #define AppVersion "0.2.0.0"
#endif
[Setup]
AppId={{A558587E-08C1-43EA-B655-CC5228BB134B}
AppName=Bilibili Behaviors for PotPlayer
AppVersion={#AppVersion}
AppPublisher=BilibiliUI for PotPlayer contributors
AppPublisherURL=https://github.com/Napreth/BilibiliUI-for-PotPlayer
DefaultDirName={code:DefaultPlayerDirectory}
DirExistsWarning=no
DisableProgramGroupPage=yes
AppendDefaultDirName=no
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
OutputDir={#RepoRoot}\build\out\dist
OutputBaseFilename=BilibiliBehaviors-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
UninstallFilesDir={app}\Extension\BilibiliBehaviors\setup
UninstallDisplayName=Bilibili Behaviors for PotPlayer
CloseApplications=no
RestartApplications=no
LicenseFile={#RepoRoot}\LICENSE
InfoBeforeFile={#RepoRoot}\build\installer-info.txt
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Messages]
SelectDirLabel3=Select the folder containing your existing 64-bit PotPlayer.
[Files]
Source: "{#RepoRoot}\build\out\package\runtime\*"; DestDir: "{app}\Extension\BilibiliBehaviors"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall
Source: "{#RepoRoot}\build\out\package\BilibiliLite.dsf"; DestDir: "{app}\Skins"; Flags: ignoreversion uninsneveruninstall
Source: "{#RepoRoot}\src\Host\MediaPlayParse - BilibiliBehaviors.as"; DestDir: "{app}\Extension\Media\PlayParse"; Flags: ignoreversion uninsneveruninstall
Source: "{#RepoRoot}\build\install.ps1"; DestDir: "{app}\Extension\BilibiliBehaviors\setup"; Flags: ignoreversion
Source: "{#RepoRoot}\build\uninstall.ps1"; DestDir: "{app}\Extension\BilibiliBehaviors\setup"; Flags: ignoreversion
Source: "{#RepoRoot}\build\installation-common.ps1"; DestDir: "{app}\Extension\BilibiliBehaviors\setup"; Flags: ignoreversion
Source: "{#RepoRoot}\build\install.ps1"; Flags: dontcopy
Source: "{#RepoRoot}\build\installation-common.ps1"; Flags: dontcopy
Source: "{#RepoRoot}\config\input-profile.json"; Flags: dontcopy
[Code]
function DefaultPlayerDirectory(Param: String): String;
var Player: String;
begin
  if RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\App Paths\PotPlayerMini64.exe', '', Player) and FileExists(RemoveQuotes(Player)) then
    Result := ExtractFileDir(RemoveQuotes(Player))
  else if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\App Paths\PotPlayerMini64.exe', '', Player) and FileExists(RemoveQuotes(Player)) then
    Result := ExtractFileDir(RemoveQuotes(Player))
  else if FileExists(ExpandConstant('{pf64}\DAUM\PotPlayer\PotPlayerMini64.exe')) then
    Result := ExpandConstant('{pf64}\DAUM\PotPlayer')
  else if FileExists('C:\Software\Media\PotPlayer\PotPlayerMini64.exe') then
    Result := 'C:\Software\Media\PotPlayer'
  else
    Result := ExpandConstant('{pf64}\DAUM\PotPlayer');
end;
function PlayerFile(): String;
begin
  Result := ExpandConstant('{app}\PotPlayerMini64.exe');
  if not FileExists(Result) then Result := ExpandConstant('{app}\PotPlayer64.exe');
end;
function RunScript(Script, Arguments: String): Boolean;
var Code: Integer; Started: Boolean;
begin
  Started := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + Script + '" ' + Arguments,
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Log(Format('Script %s: started=%d, exit/error=%d', [Script, Ord(Started), Code]));
  Result := Started and (Code = 0);
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Legacy: String;
begin
  Result := '';
  if not FileExists(PlayerFile()) then begin
    Result := 'Select the directory containing 64-bit PotPlayer.'; exit;
  end;
  ExtractTemporaryFile('install.ps1');
  ExtractTemporaryFile('installation-common.ps1');
  ExtractTemporaryFile('input-profile.json');
  Legacy := ExpandConstant('{param:LEGACYBACKUP|}');
  if not RunScript(ExpandConstant('{tmp}\install.ps1'),
    '-PrepareOnly -PlayerPath "' + PlayerFile() + '" -PayloadPath "' + ExpandConstant('{tmp}') + '" -LegacyBackupPath "' + Legacy + '"') then
    Result := 'Preparation failed. Close PotPlayer and check folder write permissions. For an older installation, supply /LEGACYBACKUP="path to original-input.json". Details: %LOCALAPPDATA%\BilibiliPotPlayer\installer-error.txt';
end;
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not RunScript(ExpandConstant('{app}\Extension\BilibiliBehaviors\setup\install.ps1'),
      '-ApplyOnly -PlayerPath "' + PlayerFile() + '" -PayloadPath "' + ExpandConstant('{app}\Extension\BilibiliBehaviors') + '"') then
      RaiseException('Could not apply player preferences. Original settings remain in %LOCALAPPDATA%\BilibiliPotPlayer\installation.');
end;
function InitializeUninstall(): Boolean;
begin
  Result := RunScript(ExpandConstant('{app}\Extension\BilibiliBehaviors\setup\uninstall.ps1'), '-CheckOnly');
  if not Result then MsgBox('Close PotPlayer before uninstalling. The original settings backup is required.', mbError, MB_OK);
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    if not RunScript(ExpandConstant('{app}\Extension\BilibiliBehaviors\setup\uninstall.ps1'), '') then
      RaiseException('Settings could not be restored. Installation files were retained for recovery.');
end;
