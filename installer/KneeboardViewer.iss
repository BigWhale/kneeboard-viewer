; Inno Setup script for Kneeboard Viewer.
; Build prerequisites (run from the repo root, see installer/README.md):
;   dotnet publish "Kneeboard Viewer/Kneeboard Viewer.csproj" -c Release -r win-x64 --self-contained -o publish\app
;   powershell -NoProfile -File "Kneeboard Viewer.StreamDeck\build-plugin.ps1"

#define AppName "Kneeboard Viewer"
#define AppVersion "0.1.0"
#define AppExe "Kneeboard Viewer.exe"
#define PluginBundle "com.bigwhale.kneeboardviewer.sdPlugin"

[Setup]
AppId={{B6F3A0E2-5C7A-4E9D-9C1A-7D3F2E8B1A40}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=David Klasinc
DefaultDirName={autopf}\Kneeboard Viewer
DefaultGroupName=Kneeboard Viewer
UninstallDisplayIcon={app}\{#AppExe}
OutputBaseFilename=KneeboardViewerSetup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
PrivilegesRequired=lowest

[Components]
Name: "app"; Description: "Kneeboard Viewer"; Types: full custom; Flags: fixed
Name: "plugin"; Description: "Stream Deck plugin (requires Elgato Stream Deck software)"; Types: full custom; Check: IsStreamDeckInstalled

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Launch Kneeboard Viewer when Windows starts"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; App (self-contained publish output).
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Components: app
; Stream Deck plugin bundle -> per-user Stream Deck plugins folder.
Source: "..\Kneeboard Viewer.StreamDeck\bin\sdplugin\{#PluginBundle}\*"; \
    DestDir: "{userappdata}\Elgato\StreamDeck\Plugins\{#PluginBundle}"; \
    Flags: recursesubdirs ignoreversion; Components: plugin

[Icons]
Name: "{group}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"; Components: app
Name: "{autodesktop}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon
Name: "{userstartup}\Kneeboard Viewer"; Filename: "{app}\{#AppExe}"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch Kneeboard Viewer"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Elgato\StreamDeck\Plugins\{#PluginBundle}"

[Code]
function IsStreamDeckInstalled: Boolean;
begin
  Result := DirExists(ExpandConstant('{userappdata}\Elgato\StreamDeck'));
end;

procedure StopStreamDeck;
var
  ResultCode: Integer;
begin
  // Stop Stream Deck so it reloads plugins; it relaunches from its own autostart.
  Exec('taskkill.exe', '/IM StreamDeck.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) and IsComponentSelected('plugin') then
    StopStreamDeck;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopStreamDeck;
end;
