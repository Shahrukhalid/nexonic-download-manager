; Builds NexonicDownloadManager-Setup-<version>.exe (per-user install, no admin rights needed).
#define Version GetEnv("NDM_VERSION")
[Setup]
AppId={{6F1C2E4B-8B7D-4C55-9E2A-3D9B1F0A7C21}
AppName=Nexonic Download Manager
AppVersion={#Version}
AppPublisher=Shahrukh Khalid
AppPublisherURL=https://www.shahrukhalid.com
AppSupportURL=https://github.com/Shahrukhalid/nexonic-download-manager/issues
AppUpdatesURL=https://github.com/Shahrukhalid/nexonic-download-manager/releases/latest
AppCopyright=Copyright (c) 2026 Shahrukh Khalid. MIT OR Apache-2.0.
LicenseFile=..\packaging\TERMS.txt
DefaultDirName={localappdata}\Programs\Nexonic Download Manager
DefaultGroupName=Nexonic Download Manager
PrivilegesRequired=lowest
OutputDir=..\build
OutputBaseFilename=NexonicDownloadManager-Setup-{#Version}
SetupIconFile=App\app.ico
UninstallDisplayIcon={app}\NexonicDownloadManager.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes

[Files]
Source: "..\build\win\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Nexonic Download Manager"; Filename: "{app}\NexonicDownloadManager.exe"
Name: "{userdesktop}\Nexonic Download Manager"; Filename: "{app}\NexonicDownloadManager.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"
Name: "addtopath"; Description: "Add the ndm command to the command line"

[Registry]
; Open .torrent files and magnet links with the app. Another program's existing handler is never replaced: then the app
; only appears under "Open with".
Root: HKCU; Subkey: "Software\Classes\NexonicDownloadManager.torrent"; ValueType: string; ValueData: "BitTorrent file"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\NexonicDownloadManager.torrent\DefaultIcon"; ValueType: string; ValueData: "{app}\NexonicDownloadManager.exe,0"
Root: HKCU; Subkey: "Software\Classes\NexonicDownloadManager.torrent\shell\open\command"; ValueType: string; ValueData: """{app}\NexonicDownloadManager.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\.torrent\OpenWithProgids"; ValueType: string; ValueName: "NexonicDownloadManager.torrent"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.torrent"; ValueType: string; ValueData: "NexonicDownloadManager.torrent"; Check: TorrentFree; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\magnet"; ValueType: string; ValueData: "URL:Magnet link"; Check: MagnetFree; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\magnet"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""; Check: MagnetFree
Root: HKCU; Subkey: "Software\Classes\magnet\shell\open\command"; ValueType: string; ValueData: """{app}\NexonicDownloadManager.exe"" ""%1"""; Check: MagnetFree
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
var
  TorrentIsFree, MagnetIsFree: Boolean;

{ Decided once before anything is written: free when nothing handles it yet, or when it is already this app. }
function InitializeSetup(): Boolean;
var
  S: string;
begin
  TorrentIsFree := True;
  if RegQueryStringValue(HKEY_CLASSES_ROOT, '.torrent', '', S) then
    TorrentIsFree := (S = '') or (CompareText(S, 'NexonicDownloadManager.torrent') = 0);
  MagnetIsFree := True;
  if RegQueryStringValue(HKEY_CLASSES_ROOT, 'magnet\shell\open\command', '', S) then
    MagnetIsFree := (S = '') or (Pos('nexonicdownloadmanager', Lowercase(S)) > 0);
  Result := True;
end;

function TorrentFree(): Boolean;
begin
  Result := TorrentIsFree;
end;

function MagnetFree(): Boolean;
begin
  Result := MagnetIsFree;
end;

function NeedsAddPath(Dir: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Path) then
  begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Path) + ';') = 0;
end;

[Run]
Filename: "{app}\NexonicDownloadManager.exe"; Description: "Start Nexonic Download Manager"; Flags: nowait postinstall skipifsilent
; In-app updates run the setup quietly: open the new version again afterwards.
Filename: "{app}\NexonicDownloadManager.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[Messages]
FinishedLabel=Thank you for installing Nexonic Download Manager!%n%nMore tools and updates:%ngithub.com/Shahrukhalid%nwww.shahrukhalid.com
