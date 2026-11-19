#ifndef AppVersion
  #define AppVersion "1.4.3"
#endif

[Setup]
AppId={{5994E9B7-78BF-4D21-B5B6-C3256F1E2C04}
AppName=Sutra Island
AppVersion={#AppVersion}
AppPublisher=Sutra Island
AppPublisherURL=https://github.com/tanujranjith/Sutra-Island
DefaultDirName={localappdata}\Programs\Sutra Island
DefaultGroupName=Sutra Island
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Sutra-Island-Setup-v{#AppVersion}-win-x64
SetupIconFile=..\DynamicIsland.Windows\app.ico
UninstallDisplayIcon={app}\SutraIsland.exe
WizardStyle=modern
WizardSizePercent=110
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\root-app-update\DynamicIsland.Windows.exe"; DestDir: "{app}"; DestName: "SutraIsland.exe"; Flags: ignoreversion
Source: "..\artifacts\kokoro-int8-multi-lang-v1_0\*"; DestDir: "{app}\SutraVoice"; Excludes: "espeak-ng-data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Sutra Island"; Filename: "{app}\SutraIsland.exe"
Name: "{autodesktop}\Sutra Island"; Filename: "{app}\SutraIsland.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SutraIsland.exe"; Description: "Open Sutra Island"; Flags: nowait postinstall skipifsilent
