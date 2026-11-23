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

[Run]
Filename: "{app}\AppLayout.exe"; Description: "{cm:LaunchProgram,AppLayout}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
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
