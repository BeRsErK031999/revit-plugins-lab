#define AppName "TrueBIM"
#define AppVersion "0.1.1"
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

[Setup]
AppId={{8F8E8CC7-D3C9-49BA-8F40-AD0F2F8D32F7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={commonpf}\TrueBIM
DefaultGroupName=TrueBIM
OutputDir=..\..\..\dist\installer
OutputBaseFilename=TrueBIM-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes

[Files]
Source: "..\..\..\dist\revit\2019\*"; DestDir: "{app}\2019"; Check: ShouldInstallYear('2019'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2020\*"; DestDir: "{app}\2020"; Check: ShouldInstallYear('2020'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2021\*"; DestDir: "{app}\2021"; Check: ShouldInstallYear('2021'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2022\*"; DestDir: "{app}\2022"; Check: ShouldInstallYear('2022'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2023\*"; DestDir: "{app}\2023"; Check: ShouldInstallYear('2023'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2024\*"; DestDir: "{app}\2024"; Check: ShouldInstallYear('2024'); Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\..\dist\revit\2025\*"; DestDir: "{app}\2025"; Check: ShouldInstallYear('2025'); Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\2019"; Check: ShouldInstallYear('2019')
Type: filesandordirs; Name: "{app}\2020"; Check: ShouldInstallYear('2020')
Type: filesandordirs; Name: "{app}\2021"; Check: ShouldInstallYear('2021')
Type: filesandordirs; Name: "{app}\2022"; Check: ShouldInstallYear('2022')
Type: filesandordirs; Name: "{app}\2023"; Check: ShouldInstallYear('2023')
Type: filesandordirs; Name: "{app}\2024"; Check: ShouldInstallYear('2024')
Type: filesandordirs; Name: "{app}\2025"; Check: ShouldInstallYear('2025')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2019\TrueBIM.addin"; Check: ShouldInstallYear('2019')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2020\TrueBIM.addin"; Check: ShouldInstallYear('2020')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2021\TrueBIM.addin"; Check: ShouldInstallYear('2021')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2022\TrueBIM.addin"; Check: ShouldInstallYear('2022')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\TrueBIM.addin"; Check: ShouldInstallYear('2023')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\TrueBIM.addin"; Check: ShouldInstallYear('2024')
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin"; Check: ShouldInstallYear('2025')

[UninstallDelete]
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2019\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2020\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2021\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2022\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2023\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2024\TrueBIM.addin"
Type: files; Name: "{commonappdata}\Autodesk\Revit\Addins\2025\TrueBIM.addin"
Type: filesandordirs; Name: "{app}\2019"
Type: filesandordirs; Name: "{app}\2020"
Type: filesandordirs; Name: "{app}\2021"
Type: filesandordirs; Name: "{app}\2022"
Type: filesandordirs; Name: "{app}\2023"
Type: filesandordirs; Name: "{app}\2024"
Type: filesandordirs; Name: "{app}\2025"
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

function IsRevitInstalled(Year: String): Boolean;
begin
  Result :=
    FileExists(ExpandConstant('{commonpf}\Autodesk\Revit ' + Year + '\Revit.exe')) or
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
    ShouldInstallYear('2025');
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
  ManifestPath: String;
  ManifestText: String;
begin
  if not ShouldInstallYear(Year) then
    exit;

  AddinDir := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\' + Year);
  AssemblyPath := ExpandConstant('{app}\' + Year + '\TrueBIM.App.dll');
  ManifestPath := AddinDir + '\TrueBIM.addin';
  ForceDirectories(AddinDir);

  ManifestText :=
    '<?xml version="1.0" encoding="utf-8" standalone="no"?>' + #13#10 +
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
  end;
end;
