#define AppVersion "1.0.2"

[Setup]
AppId={{58C1AEB9-88B9-4DFC-A8FA-93C632214B71}
AppName=Вернадского
AppVersion={#AppVersion}
AppPublisher=Вернадского
DefaultDirName={userappdata}\Vernadskogo\2024
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\..\..\dist\vernadskogo\installer
OutputBaseFilename=Vernadskogo-Revit2024-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName=Вернадского — Revit 2024

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\..\..\dist\vernadskogo\2024\Vernadskogo.App.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2024\Vernadskogo.addin"

[Code]
function EscapeXml(Value: String): String;
begin
  StringChangeEx(Value, '&', '&amp;', True);
  StringChangeEx(Value, '<', '&lt;', True);
  StringChangeEx(Value, '>', '&gt;', True);
  StringChangeEx(Value, '"', '&quot;', True);
  Result := Value;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Manifest, ManifestPath, AddinDirectory: String;
begin
  if CurStep = ssPostInstall then
  begin
    AddinDirectory := ExpandConstant('{userappdata}\Autodesk\Revit\Addins\2024');
    if not ForceDirectories(AddinDirectory) then
      RaiseException('Не удалось создать папку надстроек Revit 2024.');
    ManifestPath := AddinDirectory + '\Vernadskogo.addin';
    Manifest := '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
      '<RevitAddIns><AddIn Type="Application">' + #13#10 +
      '<Name>Vernadskogo</Name>' + #13#10 +
      '<Assembly>' + EscapeXml(ExpandConstant('{app}\Vernadskogo.App.dll')) + '</Assembly>' + #13#10 +
      '<AddInId>58C1AEB9-88B9-4DFC-A8FA-93C632214B71</AddInId>' + #13#10 +
      '<FullClassName>Vernadskogo.App.App</FullClassName>' + #13#10 +
      '<VendorId>VRND</VendorId>' + #13#10 +
      '<VendorDescription>Vernadskogo</VendorDescription>' + #13#10 +
      '</AddIn></RevitAddIns>';
    if not SaveStringToFile(ManifestPath, Utf8Encode(Manifest), False) then
      RaiseException('Не удалось записать манифест надстройки Revit 2024.');
  end;
end;
