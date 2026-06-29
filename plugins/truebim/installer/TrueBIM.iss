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
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Types]
Name: "full"; Description: "Full installation"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "core"; Description: "TrueBIM Core"; Types: full custom; Flags: fixed
Name: "modules"; Description: "Modules"; Types: full custom
Name: "modules\sheetnumbering"; Description: "Sheet Numbering"; Types: full custom
Name: "docs"; Description: "Documentation"; Types: full custom

[Dirs]
Name: "{userappdata}\Autodesk\Revit\Addins\2025"
Name: "{app}\Core"; Components: core
Name: "{app}\Modules\SheetNumbering"; Components: modules\sheetnumbering

[Files]
Source: "..\manifests\2025\TrueBIM.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: core; Flags: ignoreversion
Source: "..\artifacts\Core\*"; DestDir: "{app}\Core"; Components: core; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\Modules\SheetNumbering\*"; DestDir: "{app}\Modules\SheetNumbering"; Components: modules\sheetnumbering; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}\Docs"; Components: docs; Flags: ignoreversion

[Icons]
Name: "{group}\TrueBIM Documentation"; Filename: "{app}\Docs\README.md"; Components: docs

[Run]
Filename: "{cmd}"; Parameters: "/c echo TrueBIM installed"; Flags: runhidden; Components: core
