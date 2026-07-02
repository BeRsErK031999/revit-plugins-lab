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

Release installer targets: Revit 2019, 2020, 2021, 2022, 2023, 2024, and 2025.

Technical targets:

- Revit 2019-2024: `net48`, `RevitAPI.dll`, and `RevitAPIUI.dll` from the matching Revit version.
- Revit 2025: `net8.0-windows`, `RevitAPI.dll`, and `RevitAPIUI.dll` from Revit 2025.

## Build And Package

Build Release:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' build TrueBIM.sln --configuration Release --nologo --verbosity:minimal
```

Build installer-ready artifacts:

```powershell
.\plugins\truebim\scripts\build-installer.ps1
```

Artifacts are generated under `dist/revit/<year>/` and are not committed. The setup is generated at:

```text
dist/installer/TrueBIM-Setup.exe
```

Packaging details are documented in `docs/packaging.md`.
