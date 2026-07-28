#ifndef AppVersion
  #define AppVersion "0.13.2"
#endif
#ifndef SourceRoot
  #define SourceRoot ".."
#endif

#define AppName "BaChen AI Launcher"
#define AppPublisher "Bachen"
#define AppUrl "https://github.com/Bachen-beep/bachen-ai-launcher"
#define AppExeName "BaChen AI Launcher.exe"

[Setup]
AppId={{74A49458-C253-41BD-85A1-B0D399C4CC57}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#SourceRoot}\artifacts\installer
OutputBaseFilename=BaChen-AI-Launcher-Setup-{#AppVersion}
SetupIconFile={#SourceRoot}\Assets\BaChenLauncherIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#SourceRoot}\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoCopyright=Copyright (c) 2026 Bachen

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "{#SourceRoot}\installer\Languages\ChineseSimplified.isl"

[CustomMessages]
english.RemoveUserDataPrompt=Also remove BaChen AI Launcher settings and the default plugin data directory?%n%nThis permanently deletes files under LocalAppData and Documents. Custom data directories are never deleted automatically.%n%nChoose No to preserve all data (recommended).
chinesesimp.RemoveUserDataPrompt=是否同时删除 BaChen AI Launcher 设置和默认插件数据目录？%n%n这会永久删除 LocalAppData 和 Documents 下的相关文件。自定义数据目录不会被自动删除。%n%n建议选择“否”以保留全部数据。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceRoot}\artifacts\release\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if SuppressibleMsgBox(ExpandConstant('{cm:RemoveUserDataPrompt}'), mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\BaChen AI Launcher'), True, True, True);
      DelTree(ExpandConstant('{userdocs}\BaChen AI Launcher Data'), True, True, True);
    end;
  end;
end;
