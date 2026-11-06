; Inno Setup script for the verified GitHub Windows Lite release.
#ifndef SourceRoot
  #error SourceRoot is required.
#endif
#ifndef AppVersion
  #define AppVersion "3.2.95"
#endif
#ifndef OutputFolder
  #define OutputFolder "."
#endif

[Setup]
AppId={{C9A8F6E7-312B-4DF7-AE6F-2C714031B299}
AppName=Mabi Auto
AppVersion={#AppVersion}
AppPublisher=Mabi Auto
DefaultDirName={autopf}\MabiAuto
DefaultGroupName=Mabi Auto
OutputDir={#OutputFolder}
OutputBaseFilename=MabiAuto_Setup_V{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\release\FishingAutomation.exe
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Files]
; Never overwrite user's configuration/templates on a reinstall.
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Excludes: "release\config.json,release\notification.json,release\templates\*,release\dungeon\config\*,release\dungeon\templates\*,release\abyss\config\*,release\abyss\templates\*"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "{#SourceRoot}\release\config.json"; DestDir: "{app}\release"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\notification.json"; DestDir: "{app}\release"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\templates\*"; DestDir: "{app}\release\templates"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\dungeon\config\*"; DestDir: "{app}\release\dungeon\config"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\dungeon\templates\*"; DestDir: "{app}\release\dungeon\templates"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\abyss\config\*"; DestDir: "{app}\release\abyss\config"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall
Source: "{#SourceRoot}\release\abyss\templates\*"; DestDir: "{app}\release\abyss\templates"; Flags: recursesubdirs createallsubdirs onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autoprograms}\Mabi Auto"; Filename: "{app}\START.cmd"; WorkingDir: "{app}"
Name: "{autoprograms}\Mabi Auto\Back up settings"; Filename: "{app}\BACKUP_SETTINGS.cmd"; WorkingDir: "{app}"
Name: "{autoprograms}\Mabi Auto\Restore settings"; Filename: "{app}\RESTORE_SETTINGS.cmd"; WorkingDir: "{app}"
Name: "{autodesktop}\Mabi Auto"; Filename: "{app}\START.cmd"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
; Driver installation is opt-in, requires YES and reboot.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\support\Check-Prerequisites.ps1"" -InstallRoot ""{app}"""; Description: "Check Korean OCR, Interception and game CLI"; Flags: postinstall skipifsilent unchecked
Filename: "powershell.exe"; Parameters: "-NoProfile -NoExit -ExecutionPolicy Bypass -File ""{app}\support\InstallInterception.ps1"""; Description: "Install Interception driver (admin permission and reboot required)"; Flags: postinstall skipifsilent unchecked nowait
Filename: "{app}\START.cmd"; Description: "Start Mabi Auto after prerequisites are installed"; Flags: postinstall skipifsilent unchecked nowait
