#define AppName "TrueBIM"
#define AppVersion "0.1.0"
#define Publisher "TrueBIM"

[Setup]
AppId={{8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={userappdata}\TrueBIM\2025
DefaultGroupName=TrueBIM
OutputBaseFilename=TrueBIM-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "TrueBIM Core"; Types: full custom; Flags: fixed
Name: "modules"; Description: "Modules"; Types: full custom
Name: "modules\sheetnumbering"; Description: "Нумератор листов"; Types: full custom
Name: "modules\schedulecolumncollapse"; Description: "Свернуть ВРС"; Types: full custom
Name: "assets"; Description: "Assets"; Types: full custom
Name: "docs"; Description: "Documentation"; Types: full custom

[Dirs]
Name: "{userappdata}\Autodesk\Revit\Addins\2025"
Name: "{app}\Core"; Components: core
Name: "{app}\Modules\SheetNumbering"; Components: modules\sheetnumbering
Name: "{app}\Modules\ScheduleColumnCollapse"; Components: modules\schedulecolumncollapse
Name: "{app}\Assets"; Components: assets
Name: "{app}\Docs"; Components: docs

[Files]
Source: "..\artifacts\Core\*"; DestDir: "{app}\Core"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\Modules\SheetNumbering\*"; DestDir: "{app}\Modules\SheetNumbering"; Components: modules\sheetnumbering; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\Modules\ScheduleColumnCollapse\*"; DestDir: "{app}\Modules\ScheduleColumnCollapse"; Components: modules\schedulecolumncollapse; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\Assets\*"; DestDir: "{app}\Assets"; Components: assets; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\Docs\*"; DestDir: "{app}\Docs"; Components: docs; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TrueBIM Documentation"; Filename: "{app}\Docs\README.md"; Components: docs

[Run]
Filename: "{cmd}"; Parameters: "/c echo TrueBIM installed"; Flags: runhidden; Components: core

[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ManifestPath: string;
  AssemblyPath: string;
  ManifestText: string;
begin
  if CurStep = ssPostInstall then
  begin
    ManifestPath := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin');
    AssemblyPath := ExpandConstant('{app}\Core\TrueBIM.App.dll');
    ManifestText :=
      '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
      '<RevitAddIns>' + #13#10 +
      '  <AddIn Type="Application">' + #13#10 +
      '    <Name>TrueBIM</Name>' + #13#10 +
      '    <Assembly>' + AssemblyPath + '</Assembly>' + #13#10 +
      '    <AddInId>8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7</AddInId>' + #13#10 +
      '    <FullClassName>TrueBIM.App.App</FullClassName>' + #13#10 +
      '    <VendorId>TRBM</VendorId>' + #13#10 +
      '    <VendorDescription>TrueBIM</VendorDescription>' + #13#10 +
      '  </AddIn>' + #13#10 +
      '</RevitAddIns>' + #13#10;
    SaveStringToFile(ManifestPath, ManifestText, False);
  end;
end;
