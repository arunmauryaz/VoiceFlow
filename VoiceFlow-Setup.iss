#define MyAppName "VoiceFlow"
#ifndef MyAppVersion
  #define MyAppVersion "1.1.4"
#endif
#define MyAppPublisher "VoiceFlow Contributors"
#define MyAppURL "https://github.com/arunmauryaz/VoiceFlow"
#define MyAppExeName "VoiceFlow.exe"
#ifndef PublishDir
  #define PublishDir SourcePath + "publish\win-x64"
#endif
#ifndef ReleaseDir
  #define ReleaseDir SourcePath + "release"
#endif

[Setup]
AppId={{D37E75D9-38B9-45B0-8041-0D4A198B75AC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile={#SourcePath}LICENSE
OutputDir={#ReleaseDir}
OutputBaseFilename=VoiceFlow-v{#MyAppVersion}-Setup
SetupIconFile={#SourcePath}VoiceFlow\Assets\app-icon.ico
VersionInfoVersion={#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
AppMutex=Local\VoiceFlow.Desktop

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupicon"; Description: "Start VoiceFlow automatically when Windows starts"; GroupDescription: "Windows Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.partial,__pycache__\*,*.pyc"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--background"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsSourceDirectory(const Directory: String): Boolean;
begin
  Result := FileExists(AddBackslash(Directory) + 'VoiceFlow.sln') or
    FileExists(AddBackslash(Directory) + 'VoiceFlow\VoiceFlow.csproj') or
    DirExists(AddBackslash(Directory) + '.git');
end;

procedure InitializeWizard();
begin
  if IsSourceDirectory(WizardDirValue()) then
    WizardForm.DirEdit.Text := AddBackslash(WizardDirValue()) + 'App';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectDir) and IsSourceDirectory(WizardDirValue()) then
  begin
    MsgBox('Choose a separate app folder. This folder contains source code and development files.', mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsSourceDirectory(WizardDirValue()) then
    Result := 'Choose a separate app folder. Installing into a source-code folder is not supported.';
end;
