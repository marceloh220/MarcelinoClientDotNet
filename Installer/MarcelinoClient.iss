#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef InstallerOutput
  #define InstallerOutput "..\artifacts\installer"
#endif

[Setup]
AppId={{D683153C-5838-4F02-ACFC-ED7F2296300C}
AppName=Marcelino Client
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\MarcelinoClient
DefaultGroupName=Marcelino Client
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=MarcelinoClient-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\MarcelinoClient.exe
CloseApplications=yes
RestartApplications=no
AppMutex=Local\MarcelinoClientSingleInstance
SetupLogging=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; Flags: unchecked
Name: "startup"; Description: "Iniciar com o Windows (na bandeja)"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{userprograms}\Marcelino Client"; Filename: "{app}\MarcelinoClient.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Marcelino Client"; Filename: "{app}\MarcelinoClient.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MarcelinoClient"; ValueData: """{app}\MarcelinoClient.exe"" --tray"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MarcelinoClient.exe"; Description: "Abrir Marcelino Client"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\*.pdb"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MarcelinoClient');
end;