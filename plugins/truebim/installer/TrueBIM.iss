#define AppName "TrueBIM"
#define AppVersion "0.2.45"
#define Publisher "TrueBIM"

#ifndef ExcludeRevit2019
  #if !FileExists("..\..\..\dist\revit\2019\TrueBIM.App.dll")
    #error Missing dist\revit\2019\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2019\TrueBIM.addin")
    #error Missing dist\revit\2019\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2020
  #if !FileExists("..\..\..\dist\revit\2020\TrueBIM.App.dll")
    #error Missing dist\revit\2020\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2020\TrueBIM.addin")
    #error Missing dist\revit\2020\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2021
  #if !FileExists("..\..\..\dist\revit\2021\TrueBIM.App.dll")
    #error Missing dist\revit\2021\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2021\TrueBIM.addin")
    #error Missing dist\revit\2021\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2022
  #if !FileExists("..\..\..\dist\revit\2022\TrueBIM.App.dll")
    #error Missing dist\revit\2022\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2022\TrueBIM.addin")
    #error Missing dist\revit\2022\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2023
  #if !FileExists("..\..\..\dist\revit\2023\TrueBIM.App.dll")
    #error Missing dist\revit\2023\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2023\TrueBIM.addin")
    #error Missing dist\revit\2023\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2024
  #if !FileExists("..\..\..\dist\revit\2024\TrueBIM.App.dll")
    #error Missing dist\revit\2024\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2024\TrueBIM.addin")
    #error Missing dist\revit\2024\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2025
  #if !FileExists("..\..\..\dist\revit\2025\TrueBIM.App.dll")
    #error Missing dist\revit\2025\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2025\TrueBIM.addin")
    #error Missing dist\revit\2025\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif
#ifndef ExcludeRevit2026
  #if !FileExists("..\..\..\dist\revit\2026\TrueBIM.App.dll")
    #error Missing dist\revit\2026\TrueBIM.App.dll. Run build-installer.ps1 first.
  #endif
  #if !FileExists("..\..\..\dist\revit\2026\TrueBIM.addin")
    #error Missing dist\revit\2026\TrueBIM.addin. Run build-installer.ps1 first.
  #endif
#endif

[Setup]
AppId={{8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={userappdata}\TrueBIM
UsePreviousAppDir=no
UsePreviousSetupType=no
DefaultGroupName=TrueBIM
OutputDir=..\..\..\dist\installer
OutputBaseFilename=TrueBIM-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
WizardSelectComponents=Выберите версии Revit
SelectComponentsDesc=Для каких версий Revit установить TrueBIM?
SelectComponentsLabel2=По умолчанию выбраны все версии. Снимите отметки с тех, для которых установка не нужна.

[Types]
Name: "full"; Description: "Все версии Revit"
Name: "custom"; Description: "Выбранные версии Revit"; Flags: iscustom

[Components]
#ifndef ExcludeRevit2019
Name: "revit2019"; Description: "Revit 2019"; Types: full
#endif
#ifndef ExcludeRevit2020
Name: "revit2020"; Description: "Revit 2020"; Types: full
#endif
#ifndef ExcludeRevit2021
Name: "revit2021"; Description: "Revit 2021"; Types: full
#endif
#ifndef ExcludeRevit2022
Name: "revit2022"; Description: "Revit 2022"; Types: full
#endif
#ifndef ExcludeRevit2023
Name: "revit2023"; Description: "Revit 2023"; Types: full
#endif
#ifndef ExcludeRevit2024
Name: "revit2024"; Description: "Revit 2024"; Types: full
#endif
#ifndef ExcludeRevit2025
Name: "revit2025"; Description: "Revit 2025"; Types: full
#endif
#ifndef ExcludeRevit2026
Name: "revit2026"; Description: "Revit 2026"; Types: full
#endif

[Files]
#ifndef ExcludeRevit2019
Source: "..\..\..\dist\revit\2019\*"; DestDir: "{app}\2019"; Excludes: "*.pdb"; Check: ShouldInstallYear('2019'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2020
Source: "..\..\..\dist\revit\2020\*"; DestDir: "{app}\2020"; Excludes: "*.pdb"; Check: ShouldInstallYear('2020'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2021
Source: "..\..\..\dist\revit\2021\*"; DestDir: "{app}\2021"; Excludes: "*.pdb"; Check: ShouldInstallYear('2021'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2022
Source: "..\..\..\dist\revit\2022\*"; DestDir: "{app}\2022"; Excludes: "*.pdb"; Check: ShouldInstallYear('2022'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2023
Source: "..\..\..\dist\revit\2023\*"; DestDir: "{app}\2023"; Excludes: "*.pdb"; Check: ShouldInstallYear('2023'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2024
Source: "..\..\..\dist\revit\2024\*"; DestDir: "{app}\2024"; Excludes: "*.pdb"; Check: ShouldInstallYear('2024'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2025
Source: "..\..\..\dist\revit\2025\*"; DestDir: "{app}\2025"; Excludes: "*.pdb"; Check: ShouldInstallYear('2025'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif
#ifndef ExcludeRevit2026
Source: "..\..\..\dist\revit\2026\*"; DestDir: "{app}\2026"; Excludes: "*.pdb"; Check: ShouldInstallYear('2026'); Flags: ignoreversion recursesubdirs createallsubdirs
#endif

[InstallDelete]
Type: files; Name: "{app}\2019\*.dll"; Check: ShouldInstallYear('2019')
Type: files; Name: "{app}\2019\*.pdb"; Check: ShouldInstallYear('2019')
Type: files; Name: "{app}\2019\TrueBIM.addin"; Check: ShouldInstallYear('2019')
Type: files; Name: "{app}\2019\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2019')
Type: files; Name: "{app}\2019\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2019\Core"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2019\Modules"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2019\Assets"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2019\Docs"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2019\tools"; Check: ShouldInstallYear('2019')
Type: files; Name: "{app}\2020\*.dll"; Check: ShouldInstallYear('2020')
Type: files; Name: "{app}\2020\*.pdb"; Check: ShouldInstallYear('2020')
Type: files; Name: "{app}\2020\TrueBIM.addin"; Check: ShouldInstallYear('2020')
Type: files; Name: "{app}\2020\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2020')
Type: files; Name: "{app}\2020\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2020\Core"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2020\Modules"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2020\Assets"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2020\Docs"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2020\tools"; Check: ShouldInstallYear('2020')
Type: files; Name: "{app}\2021\*.dll"; Check: ShouldInstallYear('2021')
Type: files; Name: "{app}\2021\*.pdb"; Check: ShouldInstallYear('2021')
Type: files; Name: "{app}\2021\TrueBIM.addin"; Check: ShouldInstallYear('2021')
Type: files; Name: "{app}\2021\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2021')
Type: files; Name: "{app}\2021\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2021\Core"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2021\Modules"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2021\Assets"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2021\Docs"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2021\tools"; Check: ShouldInstallYear('2021')
Type: files; Name: "{app}\2022\*.dll"; Check: ShouldInstallYear('2022')
Type: files; Name: "{app}\2022\*.pdb"; Check: ShouldInstallYear('2022')
Type: files; Name: "{app}\2022\TrueBIM.addin"; Check: ShouldInstallYear('2022')
Type: files; Name: "{app}\2022\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2022')
Type: files; Name: "{app}\2022\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2022\Core"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2022\Modules"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2022\Assets"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2022\Docs"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2022\tools"; Check: ShouldInstallYear('2022')
Type: files; Name: "{app}\2023\*.dll"; Check: ShouldInstallYear('2023')
Type: files; Name: "{app}\2023\*.pdb"; Check: ShouldInstallYear('2023')
Type: files; Name: "{app}\2023\TrueBIM.addin"; Check: ShouldInstallYear('2023')
Type: files; Name: "{app}\2023\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2023')
Type: files; Name: "{app}\2023\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2023\Core"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2023\Modules"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2023\Assets"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2023\Docs"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2023\tools"; Check: ShouldInstallYear('2023')
Type: files; Name: "{app}\2024\*.dll"; Check: ShouldInstallYear('2024')
Type: files; Name: "{app}\2024\*.pdb"; Check: ShouldInstallYear('2024')
Type: files; Name: "{app}\2024\TrueBIM.addin"; Check: ShouldInstallYear('2024')
Type: files; Name: "{app}\2024\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2024')
Type: files; Name: "{app}\2024\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2024\Core"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2024\Modules"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2024\Assets"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2024\Docs"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2024\tools"; Check: ShouldInstallYear('2024')
Type: files; Name: "{app}\2025\*.dll"; Check: ShouldInstallYear('2025')
Type: files; Name: "{app}\2025\*.pdb"; Check: ShouldInstallYear('2025')
Type: files; Name: "{app}\2025\TrueBIM.addin"; Check: ShouldInstallYear('2025')
Type: files; Name: "{app}\2025\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2025')
Type: files; Name: "{app}\2025\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2025')
Type: filesandordirs; Name: "{app}\2025\Core"; Check: ShouldInstallYear('2025')
Type: filesandordirs; Name: "{app}\2025\Modules"; Check: ShouldInstallYear('2025')
Type: filesandordirs; Name: "{app}\2025\Assets"; Check: ShouldInstallYear('2025')
Type: filesandordirs; Name: "{app}\2025\Docs"; Check: ShouldInstallYear('2025')
Type: filesandordirs; Name: "{app}\2025\tools"; Check: ShouldInstallYear('2025')
Type: files; Name: "{app}\2026\*.dll"; Check: ShouldInstallYear('2026')
Type: files; Name: "{app}\2026\*.pdb"; Check: ShouldInstallYear('2026')
Type: files; Name: "{app}\2026\TrueBIM.addin"; Check: ShouldInstallYear('2026')
Type: files; Name: "{app}\2026\TrueBIM.App.deps.json"; Check: ShouldInstallYear('2026')
Type: files; Name: "{app}\2026\TrueBIM.App.runtimeconfig.json"; Check: ShouldInstallYear('2026')
Type: filesandordirs; Name: "{app}\2026\Core"; Check: ShouldInstallYear('2026')
Type: filesandordirs; Name: "{app}\2026\Modules"; Check: ShouldInstallYear('2026')
Type: filesandordirs; Name: "{app}\2026\Assets"; Check: ShouldInstallYear('2026')
Type: filesandordirs; Name: "{app}\2026\Docs"; Check: ShouldInstallYear('2026')
Type: filesandordirs; Name: "{app}\2026\tools"; Check: ShouldInstallYear('2026')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2019\TrueBIM.addin"; Check: ShouldInstallYear('2019')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2020\TrueBIM.addin"; Check: ShouldInstallYear('2020')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\TrueBIM.addin"; Check: ShouldInstallYear('2021')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\TrueBIM.addin"; Check: ShouldInstallYear('2022')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\TrueBIM.addin"; Check: ShouldInstallYear('2023')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\TrueBIM.addin"; Check: ShouldInstallYear('2024')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin"; Check: ShouldInstallYear('2025')
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\TrueBIM.addin"; Check: ShouldInstallYear('2026')

[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2019\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2020\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2021\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2022\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2023\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin"
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2026\TrueBIM.addin"
Type: filesandordirs; Name: "{app}\2019"
Type: filesandordirs; Name: "{app}\2020"
Type: filesandordirs; Name: "{app}\2021"
Type: filesandordirs; Name: "{app}\2022"
Type: filesandordirs; Name: "{app}\2023"
Type: filesandordirs; Name: "{app}\2024"
Type: filesandordirs; Name: "{app}\2025"
Type: filesandordirs; Name: "{app}\2026"
Type: dirifempty; Name: "{app}"

[Code]
function IsPackagedYear(Year: String): Boolean;
begin
  Result := False;
#ifndef ExcludeRevit2019
  if Year = '2019' then Result := True;
#endif
#ifndef ExcludeRevit2020
  if Year = '2020' then Result := True;
#endif
#ifndef ExcludeRevit2021
  if Year = '2021' then Result := True;
#endif
#ifndef ExcludeRevit2022
  if Year = '2022' then Result := True;
#endif
#ifndef ExcludeRevit2023
  if Year = '2023' then Result := True;
#endif
#ifndef ExcludeRevit2024
  if Year = '2024' then Result := True;
#endif
#ifndef ExcludeRevit2025
  if Year = '2025' then Result := True;
#endif
#ifndef ExcludeRevit2026
  if Year = '2026' then Result := True;
#endif
end;

function ShouldInstallYear(Year: String): Boolean;
begin
  Result := IsPackagedYear(Year) and WizardIsComponentSelected('revit' + Year);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpSelectComponents) and (WizardSelectedComponents(False) = '') then
  begin
    MsgBox('Выберите хотя бы одну версию Revit.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure SaveManifest(Year: String);
var
  AddinDir: String;
  AssemblyPath: String;
  ManifestLines: TArrayOfString;
  ManifestPath: String;
begin
  if not ShouldInstallYear(Year) then
    exit;

  AddinDir := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Year);
  AssemblyPath := ExpandConstant('{app}\' + Year + '\TrueBIM.App.dll');
  ManifestPath := AddinDir + '\TrueBIM.addin';
  ForceDirectories(AddinDir);

  SetArrayLength(ManifestLines, 11);
  ManifestLines[0] := '<?xml version="1.0" encoding="utf-8" standalone="no"?>';
  ManifestLines[1] := '<RevitAddIns>';
  ManifestLines[2] := '  <AddIn Type="Application">';
  ManifestLines[3] := '    <Name>TrueBIM</Name>';
  ManifestLines[4] := '    <Assembly>' + AssemblyPath + '</Assembly>';
  ManifestLines[5] := '    <AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId>';
  ManifestLines[6] := '    <FullClassName>TrueBIM.App.App</FullClassName>';
  ManifestLines[7] := '    <VendorId>TRBM</VendorId>';
  ManifestLines[8] := '    <VendorDescription>TrueBIM</VendorDescription>';
  ManifestLines[9] := '  </AddIn>';
  ManifestLines[10] := '</RevitAddIns>';

  if not SaveStringsToUTF8FileWithoutBOM(ManifestPath, ManifestLines, False) then
    RaiseException('Failed to write add-in manifest: ' + ManifestPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SaveManifest('2019');
    SaveManifest('2020');
    SaveManifest('2021');
    SaveManifest('2022');
    SaveManifest('2023');
    SaveManifest('2024');
    SaveManifest('2025');
    SaveManifest('2026');
  end;
end;
