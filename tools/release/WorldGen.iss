; WorldGen Windows telepítő (WF-INSTALL-001/002, ND-112) — Inno Setup 6.3+.
; Közvetlenül NE ezt fordítsd: a tools/release/build-installer.ps1 a
; release-identity.json-ból adja át a /D értékeket, így egy igazságforrás marad.
;
; FONTOS: az AppId soha nem változhat, különben a frissítés nem ismeri fel
; a meglévő telepítést (két "WorldGen" jelenne meg a Programok listában).

#ifndef AppName
  #define AppName "WorldGen"
#endif
#ifndef CompanyName
  #define CompanyName "WorldGen"
#endif
#ifndef ExeName
  #define ExeName "WorldGen"
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef BuildDir
  #define BuildDir "..\..\artifacts\builds\" + ExeName + "-" + AppVersion + "-win64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\release"
#endif

[Setup]
AppId={{63847F5F-0085-4C94-AD6F-B1882BAE0B56}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#CompanyName}
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#CompanyName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#ExeName}.exe
UninstallDisplayName={#AppName}
OutputDir={#OutputDir}
OutputBaseFilename={#ExeName}-{#AppVersion}-win64-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
; Ikon: amíg nincs végleges ikon-asset (ND-111), a Unity-exe saját ikonja látszik.
; SetupIconFile=WorldGen.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*_BurstDebugInformation_DoNotShip,*_BackUpThisFolder_ButDontShipItInYourBuild"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#ExeName}.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#ExeName}.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
{ A Unity persistentDataPath-ja: %USERPROFILE%\AppData\LocalLow\<Company>\<Product>.
  A telepítési könyvtáron kívül van, ezért az uninstall alapból érintetlenül hagyja. }
function UserDataDir(): String;
begin
  Result := ExpandConstant('{%USERPROFILE}') + '\AppData\LocalLow\{#CompanyName}\{#AppName}';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent()) and DirExists(UserDataDir()) then
  begin
    if MsgBox('Do you also want to delete your {#AppName} saves, settings, screenshots and logs?' + #13#10#13#10 +
              UserDataDir() + #13#10#13#10 + 'Choose No to keep them for a later reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(UserDataDir(), True, True, True);
  end;
end;
