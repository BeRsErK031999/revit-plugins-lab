# TrueBIM Packaging

TrueBIM currently targets Revit 2025 and `net8.0-windows`.

## Build Release

From the repository root:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build TrueBIM.sln --configuration Release --nologo --verbosity:minimal
```

## Build Artifacts

Create installer-ready artifacts:

```powershell
.\plugins\truebim\scripts\build-artifacts-2025.ps1
```

The script rebuilds `TrueBIM.sln` in Release by default, clears `plugins/truebim/artifacts`, and creates:

```text
plugins/truebim/artifacts/Core/TrueBIM.App.dll
plugins/truebim/artifacts/Core/TrueBIM.App.pdb
plugins/truebim/artifacts/Core/TrueBIM.App.deps.json
plugins/truebim/artifacts/Modules/SheetNumbering/module.json
plugins/truebim/artifacts/Modules/SheetNumbering/README.md
plugins/truebim/artifacts/Docs/*.md
```

`plugins/truebim/artifacts/` is generated output and is ignored by Git.
The module manifest is part of the runtime contract: installer component selection controls which module manifests are copied to the installed `Modules` folder, and the launcher only shows modules with installed manifests.

## Build Installer

The installer draft is:

```text
plugins/truebim/installer/TrueBIM.iss
```

Compile it with Inno Setup after building artifacts:

```powershell
& 'C:\Users\Borodin_Artem\AppData\Local\Programs\Inno Setup 6\ISCC.exe' .\plugins\truebim\installer\TrueBIM.iss
```

Do not run the produced installer during automated packaging checks. The compile step is safe; installation changes `%APPDATA%` and should be handled as manual QA.

## Local QA Preflight

Before manual Revit QA, run:

```powershell
.\plugins\truebim\scripts\qa-preflight-2025.ps1
```

This is the primary local smoke check before opening Revit. It builds Release, runs Release tests, builds artifacts, compiles the installer when `ISCC.exe` is present, performs local deploy, and verifies the installed `.addin` and Sheet Numbering module manifest.
Revit must be closed before running this script because local deploy overwrites the installed add-in DLL.

Installer compile is not installer install QA. The preflight does not run the installer and does not launch Revit.

## Expected Installer Inputs

The installer consumes:

```text
plugins/truebim/artifacts/Core/*
plugins/truebim/artifacts/Modules/SheetNumbering/*
plugins/truebim/artifacts/Docs/*
```

When the Sheet Numbering component is selected, the installer copies:

```text
%APPDATA%\TrueBIM\2025\Modules\SheetNumbering\module.json
%APPDATA%\TrueBIM\2025\Modules\SheetNumbering\README.md
```

If the component is not selected, the manifest is not installed and the module is not available in the launcher.

## Revit Add-In Manifest Strategy

Revit does not reliably expand `%APPDATA%` inside `.addin` `Assembly` paths. The source manifest in `plugins/truebim/manifests/2025/TrueBIM.addin` remains reusable for local deploy, where `deploy-local-2025.ps1` writes an absolute path.

For installer flow, `TrueBIM.iss` generates the installed manifest at install time and writes an expanded absolute path to:

```text
%APPDATA%\Autodesk\Revit\Addins\2025\TrueBIM.addin
```

The generated `Assembly` points to:

```text
%APPDATA%\TrueBIM\2025\Core\TrueBIM.App.dll
```

## Known Limitations

- Revit 2025 manual QA is still required before release.
- Installer install/uninstall behavior has not been manually validated.
- The installer targets current-user installation only.
- No support is included for older Revit versions.
- Runtime module enable/disable settings are stored in `%APPDATA%\TrueBIM\2025\module-settings.json`.
