#ifndef AppVersion
  #error AppVersion must be supplied
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied
#endif
#ifndef OutputDir
  #error OutputDir must be supplied
#endif
[Setup]
AppId={{9F6C58D1-F911-5792-988B-0F59140C0E7C}
AppName=Batchwork
AppVersion={#AppVersion}
AppPublisher=informent
AppPublisherURL=https://github.com/informent/Batchwork
DefaultDirName={localappdata}\Programs\Batchwork
DefaultGroupName=Batchwork
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=Batchwork-v{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Batchwork.exe
CloseApplications=yes
CloseApplicationsFilter=Batchwork.exe
RestartApplications=no
SetupLogging=yes
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\Batchwork"; Filename: "{app}\Batchwork.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Batchwork"; Filename: "{app}\Batchwork.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
Filename: "{app}\Batchwork.exe"; Description: "Open Batchwork"; Flags: nowait postinstall skipifsilent unchecked
