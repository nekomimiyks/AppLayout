[Setup]
AppId={{83427010-3D2B-4EE6-89D2-77A069E67EDF}
AppName=AppLayout
AppVersion={#AppLayoutVersion}
AppPublisher=nekomimiyks
AppPublisherURL=https://github.com/nekomimiyks/AppLayout
AppUpdatesURL=https://github.com/nekomimiyks/AppLayout/releases/latest
DefaultDirName={localappdata}\Programs\AppLayout
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.19041
AppMutex=Local\AppLayoutGUI-Single
UninstallDisplayIcon={app}\AppLayout.exe
SetupIconFile=..\src\app.ico
OutputBaseFilename=AppLayout-Setup
WizardStyle=modern
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "{#AppLayoutPayload}\AppLayout.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"

[Icons]
Name: "{autoprograms}\AppLayout"; Filename: "{app}\AppLayout.exe"; WorkingDir: "{app}"

[CustomMessages]
english.ConfirmLaunch=Installation is complete. Start AppLayout now?
japanese.ConfirmLaunch=インストールが完了しました。今すぐ AppLayout を起動しますか？
english.LaunchFailed=AppLayout could not start. You can try opening it from the Start menu.
japanese.LaunchFailed=AppLayout を起動できませんでした。スタートメニューから起動してください。

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if (CurStep <> ssDone) or WizardSilent then
    Exit;
  if MsgBox(CustomMessage('ConfirmLaunch'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    Exit;
  if not ExecAsOriginalUser(ExpandConstant('{app}\AppLayout.exe'), '', ExpandConstant('{app}'),
    SW_SHOWNORMAL, ewNoWait, ErrorCode) then
    MsgBox(CustomMessage('LaunchFailed') + #13#10 + SysErrorMessage(ErrorCode), mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  RunKey, Command: String;
begin
  if CurUninstallStep = usUninstall then begin
    RunKey := 'Software\Microsoft\Windows\CurrentVersion\Run';
    if RegQueryStringValue(HKCU, RunKey, 'AppLayout', Command) and
       (CompareText(Command, '"' + ExpandConstant('{app}\AppLayout.exe') + '" /startup') = 0) then
      RegDeleteValue(HKCU, RunKey, 'AppLayout');
  end;
end;
