#define AppName "TrueBIM"
#define AppVersion "0.2.0"
#define Publisher "TrueBIM"

#if !FileExists("..\..\..\dist\revit\2019\TrueBIM.App.dll")
  #error Missing dist\revit\2019\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2019\TrueBIM.addin")
  #error Missing dist\revit\2019\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2020\TrueBIM.App.dll")
  #error Missing dist\revit\2020\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2020\TrueBIM.addin")
  #error Missing dist\revit\2020\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2021\TrueBIM.App.dll")
  #error Missing dist\revit\2021\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2021\TrueBIM.addin")
  #error Missing dist\revit\2021\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2022\TrueBIM.App.dll")
  #error Missing dist\revit\2022\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2022\TrueBIM.addin")
  #error Missing dist\revit\2022\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2023\TrueBIM.App.dll")
  #error Missing dist\revit\2023\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2023\TrueBIM.addin")
  #error Missing dist\revit\2023\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2024\TrueBIM.App.dll")
  #error Missing dist\revit\2024\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2024\TrueBIM.addin")
  #error Missing dist\revit\2024\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2025\TrueBIM.App.dll")
  #error Missing dist\revit\2025\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2025\TrueBIM.addin")
  #error Missing dist\revit\2025\TrueBIM.addin. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2026\TrueBIM.App.dll")
  #error Missing dist\revit\2026\TrueBIM.App.dll. Run build-installer.ps1 first.
#endif
#if !FileExists("..\..\..\dist\revit\2026\TrueBIM.addin")
  #error Missing dist\revit\2026\TrueBIM.addin. Run build-installer.ps1 first.
#endif

[Setup]
AppId={{8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={userappdata}\TrueBIM
UsePreviousAppDir=no
DefaultGroupName=TrueBIM
OutputDir=..\..\..\dist\installer
OutputBaseFilename=TrueBIM-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Files]
Source: "..\..\..\dist\revit\2019\*"; DestDir: "{app}\2019"; Excludes: "*.pdb"; Check: ShouldInstallYear('2019'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2020\*"; DestDir: "{app}\2020"; Excludes: "*.pdb"; Check: ShouldInstallYear('2020'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2021\*"; DestDir: "{app}\2021"; Excludes: "*.pdb"; Check: ShouldInstallYear('2021'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2022\*"; DestDir: "{app}\2022"; Excludes: "*.pdb"; Check: ShouldInstallYear('2022'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2023\*"; DestDir: "{app}\2023"; Excludes: "*.pdb"; Check: ShouldInstallYear('2023'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2024\*"; DestDir: "{app}\2024"; Excludes: "*.pdb"; Check: ShouldInstallYear('2024'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2025\*"; DestDir: "{app}\2025"; Excludes: "*.pdb"; Check: ShouldInstallYear('2025'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2026\*"; DestDir: "{app}\2026"; Excludes: "*.pdb"; Check: ShouldInstallYear('2026'); Flags: ignoreversion recursesubdirs createallsubdirs

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
var
  VersionPage: TWizardPage;
  VersionList: TNewCheckListBox;
  Index2019: Integer;
  Index2020: Integer;
  Index2021: Integer;
  Index2022: Integer;
  Index2023: Integer;
  Index2024: Integer;
  Index2025: Integer;
  Index2026: Integer;

function IsRevitInstalled(Year: String): Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{commonpf}\Autodesk\Revit ' + Year + '\Revit.exe')) or
    DirExists(ExpandConstant('{userappdata}\Autodesk\Revit\Addins\' + Year)) or
    DirExists(ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Year)) or
    RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\Revit\' + Year) or
    RegKeyExists(HKLM64, 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + Year) or
    RegKeyExists(HKLM32, 'SOFTWARE\Autodesk\Revit\' + Year) or
    RegKeyExists(HKLM32, 'SOFTWARE\Autodesk\Revit\Autodesk Revit ' + Year);
end;

function VersionCaption(Year: String): String;
begin
  if IsRevitInstalled(Year) then
    Result := 'Revit ' + Year + ' (found)'
  else
    Result := 'Revit ' + Year + ' (not found; confirmation required)';
end;

function AddVersion(Year: String): Integer;
begin
  Result := VersionList.AddCheckBox(
    VersionCaption(Year),
    '',
    0,
    IsRevitInstalled(Year),
    True,
    False,
    False,
    nil);
end;

procedure InitializeWizard;
var
  Note: TNewStaticText;
begin
  VersionPage := CreateCustomPage(
    wpSelectDir,
    'Select Revit versions',
    'Choose the Revit versions where TrueBIM should be installed.');

  VersionList := TNewCheckListBox.Create(VersionPage);
  VersionList.Parent := VersionPage.Surface;
  VersionList.Left := 0;
  VersionList.Top := 0;
  VersionList.Width := VersionPage.SurfaceWidth;
  VersionList.Height := ScaleY(160);

  Index2019 := AddVersion('2019');
  Index2020 := AddVersion('2020');
  Index2021 := AddVersion('2021');
  Index2022 := AddVersion('2022');
  Index2023 := AddVersion('2023');
  Index2024 := AddVersion('2024');
  Index2025 := AddVersion('2025');
  Index2026 := AddVersion('2026');

  Note := TNewStaticText.Create(VersionPage);
  Note.Parent := VersionPage.Surface;
  Note.Left := 0;
  Note.Top := VersionList.Top + VersionList.Height + ScaleY(12);
  Note.Width := VersionPage.SurfaceWidth;
  Note.Height := ScaleY(48);
  Note.WordWrap := True;
  Note.Caption :=
    'Only versions detected on this PC are selected by default. ' +
    'Selecting a version that is not detected is allowed only after explicit confirmation.';
end;

function ShouldInstallYear(Year: String): Boolean;
begin
  if Year = '2019' then
    Result := VersionList.Checked[Index2019]
  else if Year = '2020' then
    Result := VersionList.Checked[Index2020]
  else if Year = '2021' then
    Result := VersionList.Checked[Index2021]
  else if Year = '2022' then
    Result := VersionList.Checked[Index2022]
  else if Year = '2023' then
    Result := VersionList.Checked[Index2023]
  else if Year = '2024' then
    Result := VersionList.Checked[Index2024]
  else if Year = '2025' then
    Result := VersionList.Checked[Index2025]
  else if Year = '2026' then
    Result := VersionList.Checked[Index2026]
  else
    Result := False;
end;

function AnyYearSelected: Boolean;
begin
  Result :=
    ShouldInstallYear('2019') or
    ShouldInstallYear('2020') or
    ShouldInstallYear('2021') or
    ShouldInstallYear('2022') or
    ShouldInstallYear('2023') or
    ShouldInstallYear('2024') or
    ShouldInstallYear('2025') or
    ShouldInstallYear('2026');
end;

procedure AppendMissingYear(var MissingYears: String; Year: String);
begin
  if ShouldInstallYear(Year) and not IsRevitInstalled(Year) then
  begin
    if MissingYears <> '' then
      MissingYears := MissingYears + ', ';

    MissingYears := MissingYears + Year;
  end;
end;

function MissingSelectedYears: String;
begin
  Result := '';
  AppendMissingYear(Result, '2019');
  AppendMissingYear(Result, '2020');
  AppendMissingYear(Result, '2021');
  AppendMissingYear(Result, '2022');
  AppendMissingYear(Result, '2023');
  AppendMissingYear(Result, '2024');
  AppendMissingYear(Result, '2025');
  AppendMissingYear(Result, '2026');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  MissingYears: String;
begin
  Result := True;

  if CurPageID = VersionPage.ID then
  begin
    if not AnyYearSelected then
    begin
      MsgBox('Select at least one Revit version.', mbError, MB_OK);
      Result := False;
      exit;
    end;

    MissingYears := MissingSelectedYears;
    if MissingYears <> '' then
    begin
      Result :=
        MsgBox(
          'Revit was not detected for these selected versions: ' + MissingYears + '.' + #13#10 + #13#10 +
          'The installer can still copy the add-in manifest for later Revit installation. Continue?',
          mbConfirmation,
          MB_YESNO) = IDYES;
    end;
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
