; MdViewer Inno Setup installer
; Built by build/Build-Release.ps1 after dotnet publish

#define MyAppName "MdViewer"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "MdViewer"
#define MyAppExeName "MdViewer.exe"
#define MyCleanupExeName "MdViewer-Cleanup.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef CleanupDir
  #define CleanupDir "..\artifacts\cleanup"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{A7C3E8F1-4B2D-4E9A-9C1F-6D8B0A2E5F47}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=MdViewer-{#MyAppVersion}-Setup
SetupIconFile=..\src\MdViewer\Assets\MdViewer.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "fileassoc"; Description: "Register &Explorer integration (.md files + Open with MdViewer on folders)"; GroupDescription: "Windows Explorer:"; Flags: checkedonce

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#CleanupDir}\{#MyCleanupExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{group}\Full cleanup (remove settings & cache)"; Filename: "{app}\{#MyCleanupExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Catch leftover / hidden files under the install folder
Type: filesandordirs; Name: "{app}\*"
Type: dirifempty; Name: "{app}"

[Registry]
; --- .md / .markdown association ---
Root: HKCU; Subkey: "Software\Classes\.md"; ValueType: string; ValueName: ""; ValueData: "MdViewer.md"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.md"; ValueType: string; ValueName: "Content Type"; ValueData: "text/markdown"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueName: ""; ValueData: "MdViewer.md"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueName: "Content Type"; ValueData: "text/markdown"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\MdViewer.md"; ValueType: string; ValueName: ""; ValueData: "Markdown Document"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\MdViewer.md\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

; --- Folder context menus ---
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MdViewer"; ValueType: string; ValueName: ""; ValueData: "Open with MdViewer"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MdViewer"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MdViewer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MdViewer"; ValueType: string; ValueName: ""; ValueData: "Open with MdViewer"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MdViewer"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"",0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MdViewer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%V"""; Tasks: fileassoc

[Code]
function KillMdViewer(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM MdViewer.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

function ReadDefaultRegValue(const RootKey: Integer; const SubKeyName: String): String;
begin
  if not RegQueryStringValue(RootKey, SubKeyName, '', Result) then
    Result := '';
end;

procedure ClearProgIdIfOwned(const ExtKey: String);
begin
  if ReadDefaultRegValue(HKCU, ExtKey) = 'MdViewer.md' then
    RegDeleteValue(HKCU, ExtKey, '');
end;

procedure RemoveExplorerIntegrationAlways();
begin
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\MdViewer.md');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shell\MdViewer');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\Background\shell\MdViewer');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Folder\shell\MdViewer');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Applications\MdViewer.exe');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\MdViewer');
  ClearProgIdIfOwned('Software\Classes\.md');
  ClearProgIdIfOwned('Software\Classes\.markdown');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillMdViewer();

  if CurUninstallStep = usPostUninstall then
  begin
    { User data: settings, logs, WebView2 cache (hidden files included) }
    DelTree(ExpandConstant('{localappdata}\MdViewer'), True, True, True);
    DelTree(ExpandConstant('{userappdata}\MdViewer'), True, True, True);
    DelTree(ExpandConstant('{commonappdata}\MdViewer'), True, True, True);
    RemoveExplorerIntegrationAlways();
  end;
end;
