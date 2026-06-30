# TrueBIM

TrueBIM is planned as a modular Revit add-in platform.

The first module is `sheet-numbering`.

## Product direction

- One Revit ribbon entry point: TrueBIM.
- Multiple optional modules.
- Installer-level module selection.
- Runtime module enable/disable settings.
- Shared infrastructure for logging, Revit context access, UI shell, and future licensing/update logic.

## Initial modules

- `sheet-numbering` - sheet numbering and renumbering tools (`Нумератор листов`).
- `schedule-column-collapse` - copies a schedule and hides zero-only numeric columns (`Свернуть ВРС`).

## Target Revit versions

Supported local development targets: Revit 2022 and Revit 2025.

Technical targets:

- Revit 2022: `net48`, `RevitAPI.dll`, and `RevitAPIUI.dll` from Revit 2022.
- Revit 2025: `net8.0-windows`, `RevitAPI.dll`, and `RevitAPIUI.dll` from Revit 2025.

## Build And Package

Build Release:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build TrueBIM.sln --configuration Release --nologo --verbosity:minimal
```

Build installer-ready artifacts:

```powershell
.\plugins\truebim\scripts\build-artifacts-2022.ps1
.\plugins\truebim\scripts\build-artifacts-2025.ps1
```

Artifacts are generated under `plugins/truebim/artifacts-2022/` or `plugins/truebim/artifacts/` and are not committed.

Compile the installer draft with Inno Setup after artifacts are built:

```powershell
& 'C:\Users\Borodin_Artem\AppData\Local\Programs\Inno Setup 6\ISCC.exe' .\plugins\truebim\installer\TrueBIM.iss
```

Packaging details are documented in `docs/packaging.md`.
