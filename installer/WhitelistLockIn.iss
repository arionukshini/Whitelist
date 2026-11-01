#define AppName "Whitelist Lock In"
#define AppVersion "1.0.0"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{0F9D9889-AF76-441F-8E23-0420D99B4E91}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Whitelist Lock In
DefaultGroupName=Whitelist Lock In
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=WhitelistLockIn-Setup-{#AppVersion}-x64
SetupIconFile=..\src\FocusGuard.UI\Assets\WhitelistLockIn.ico
UninstallDisplayIcon={app}\WhitelistLockIn.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion=1.0.0.0
VersionInfoProductName=Whitelist Lock In
VersionInfoProductVersion={#AppVersion}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.whitelistlockin.host"; ValueType: string; ValueName: ""; ValueData: "{app}\NativeMessagingHost.json"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.whitelistlockin.host"; ValueType: string; ValueName: ""; ValueData: "{app}\NativeMessagingHost.json"; Flags: uninsdeletekey

[Icons]
Name: "{group}\Whitelist Lock In"; Filename: "{app}\WhitelistLockIn.exe"
Name: "{autodesktop}\Whitelist Lock In"; Filename: "{app}\WhitelistLockIn.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WhitelistLockIn.exe"; Parameters: "--register-native-host"; StatusMsg: "Registering browser integration..."; Flags: runhidden waituntilterminated
Filename: "{app}\WhitelistLockIn.exe"; Description: "Launch Whitelist Lock In"; Flags: postinstall nowait skipifsilent
