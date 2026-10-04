; Astra installer — built by build.ps1 (Inno Setup 6/7).
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif
#ifndef IconFile
  #define IconFile ""
#endif

[Setup]
AppId={{7C2D9E0B-4A51-4E8B-9B7E-A57A00000001}
AppName=Astra
AppVersion={#AppVersion}
AppVerName=Astra {#AppVersion}
AppPublisher=Astra
DefaultDirName={autopf}\Astra
DefaultGroupName=Astra
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=Astra-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\Astra.exe
; Setup refuses to run (or asks to close Astra) while the app is open.
AppMutex=Astra.SingleInstance.v1
CloseApplications=yes
#if IconFile != ""
SetupIconFile={#IconFile}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Astra when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Astra"; Filename: "{app}\Astra.exe"
Name: "{autodesktop}\Astra"; Filename: "{app}\Astra.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Astra"; ValueData: """{app}\Astra.exe"" --minimized"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Astra.exe"; Description: "{cm:LaunchProgram,Astra}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Make sure a running Astra doesn't block removal of its own files.
Filename: "{cmd}"; Parameters: "/C taskkill /IM Astra.exe /F"; Flags: runhidden; RunOnceId: "StopAstra"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  // Settings, the local index, memories and downloaded speech models live in %AppData%\Astra.
  // They are only deleted when the user explicitly agrees.
  // Never delete user data during a silent uninstall (e.g. scripted upgrades): there is nobody to ask.
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
  begin
    DataDir := ExpandConstant('{userappdata}\Astra');
    if DirExists(DataDir) then
      if MsgBox('Also delete Astra''s settings, index, memories and downloaded speech models?' + #13#10 + DataDir,
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
