#define MyAppName "Codex Quota Pills"
#define MyAppVersion "0.2.24"
#define MyAppExeName "CodexQuotaPills.exe"

[Setup]
AppId={{B99132EB-0796-4B7D-82E3-7D5D7D4F4C59}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Codex Quota Pills contributors
AppPublisherURL=https://github.com/jinfu0111-gif/CodexQuotaPills
AppSupportURL=https://github.com/jinfu0111-gif/CodexQuotaPills/issues
AppUpdatesURL=https://github.com/jinfu0111-gif/CodexQuotaPills/releases
DefaultDirName={localappdata}\Programs\Codex Quota Pills
DefaultGroupName=Codex Quota Pills
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=CodexQuotaPills-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=installer-assets\app-icon.ico
LicenseFile=LICENSE
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "bin\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "installer-assets\usage-cache.ini"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "startup"; Description: "跟随 Codex 启动与退出（推荐）"; GroupDescription: "启动方式："; Flags: checkedonce

[Icons]
Name: "{group}\Codex 额度胶囊"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\显示设置"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--settings"
Name: "{group}\开源许可证"; Filename: "{app}\LICENSE"
Name: "{group}\第三方来源说明"; Filename: "{app}\THIRD_PARTY_NOTICES.md"
Name: "{userdesktop}\Codex 额度胶囊"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{userstartup}\Codex Quota Pills"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 Codex 额度胶囊"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopCodexQuotaPills"
