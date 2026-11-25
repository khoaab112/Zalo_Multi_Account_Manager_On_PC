#ifndef ManagerBinary
  #error ManagerBinary is required
#endif
#ifndef SandboxieInstaller
  #error SandboxieInstaller is required
#endif
#ifndef SandboxieLicense
  #error SandboxieLicense is required
#endif

[Setup]
AppId=ZaloManager
AppName=Zalo Manager
AppVersion=3.0
DefaultDirName={autopf}\Zalo Manager
DefaultGroupName=Zalo Manager
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputBaseFilename=ZaloManagerSetup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\ZaloManager.exe
Compression=lzma2
SolidCompression=yes
CloseApplications=yes

[Files]
Source: "{#ManagerBinary}"; DestDir: "{app}"; DestName: "ZaloManager.exe"; Flags: ignoreversion
Source: "{#SandboxieInstaller}"; DestName: "Sandboxie-Plus-x64-v1.19.1.exe"; Flags: dontcopy
Source: "{#SandboxieLicense}"; DestDir: "{app}\licenses"; DestName: "Sandboxie-Plus.txt"; Flags: ignoreversion

[Icons]
Name: "{group}\Zalo Manager"; Filename: "{app}\ZaloManager.exe"
Name: "{commondesktop}\Zalo Manager"; Filename: "{app}\ZaloManager.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\ZaloManager.exe"; Description: "Mở Zalo Manager"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
function SandboxieReady: Boolean;
var
  Root: String;
  VersionMS, VersionLS: Cardinal;
begin
  Root := ExpandConstant('{autopf}\Sandboxie-Plus\');
  Result := FileExists(Root + 'SbieIni.exe') and FileExists(Root + 'SbieDll.dll')
    and FileExists(Root + 'SbieDrv.sys')
    and GetVersionNumbers(Root + 'Start.exe', VersionMS, VersionLS);
  if Result then
    Result := (VersionMS > ((5 shl 16) or 74)) or
      ((VersionMS = ((5 shl 16) or 74)) and (VersionLS >= (1 shl 16)));
end;

function DisableSandboxieControlAutoStart: Boolean;
var
  ConfigTool: String;
  ExitCode: Integer;
begin
  ConfigTool := ExpandConstant('{autopf}\Sandboxie-Plus\SbieIni.exe');
  Result := ExecAsOriginalUser(ConfigTool,
    'set UserSettings_Current SbieCtrl_EnableAutoStart n', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if not Result or (ExitCode <> 0) then
  begin
    Result := False;
    Exit;
  end;
  Result := ExecAsOriginalUser(ConfigTool,
    'set UserSettings_Current SbieCtrl_EnableLogonStart n', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := Result and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstallerPath: String;
  ExitCode: Integer;
begin
  Result := '';
  if not SandboxieReady then
  begin
    if FileExists(ExpandConstant('{autopf}\Sandboxie-Plus\Start.exe')) then
    begin
      Result := 'Hãy cập nhật hoặc sửa bản cài Sandboxie-Plus hiện có trước khi cài Zalo Manager.';
      Exit;
    end;
    ExtractTemporaryFile('Sandboxie-Plus-x64-v1.19.1.exe');
    InstallerPath := ExpandConstant('{tmp}\Sandboxie-Plus-x64-v1.19.1.exe');
    if not Exec(InstallerPath, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS /OPEN_AGENT=0 /MERGETASKS="!DesktopIcon"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := 'Không mở được bộ cài Sandboxie-Plus. Mã lỗi Windows ' + IntToStr(ExitCode) + '.';
      Exit;
    end;
    if (ExitCode <> 0) or not SandboxieReady then
    begin
      Result := 'Chưa cài được Sandboxie-Plus. Mã thoát ' + IntToStr(ExitCode) + '. Xem nhật ký của bộ cài.';
      Exit;
    end;
  end;
  if not DisableSandboxieControlAutoStart then
    Result := 'Chưa lưu được tùy chọn tắt tự mở giao diện Sandboxie. Kiểm tra quyền cấu hình Sandboxie rồi thử lại.';
end;
