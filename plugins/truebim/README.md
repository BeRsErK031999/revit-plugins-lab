# TrueBIM

TrueBIM is planned as a modular Revit add-in platform.

The first installed module is `sheet-numbering`. The app also contains core ribbon commands that do not require a separate module manifest.

## Product direction

- One Revit ribbon entry point: TrueBIM.
- Multiple optional modules.
- Installer-level module selection.
- Runtime module enable/disable settings.
- Shared infrastructure for logging, Revit context access, UI shell, and future licensing/update logic.

## Initial modules

- `sheet-numbering` - sheet numbering and renumbering tools (`Нумератор листов`).
- `schedule-column-collapse` - copies a schedule and hides zero-only numeric columns (`Свернуть ВРС`).
- `print` - sheet batch print/export module (`Печать`) for PDF, DWG, and DXF export with filename templates, CAD export setups, multi-document sheet sources, saved settings, and Revit 2022/2025 preflight coverage.

## Core ribbon commands

- `БИМ > Видимость` - active-view category visibility control. It lets users turn controllable Revit categories on and off in the current view, with grouping, search, status text, logging, manual QA notes, and automated ribbon metadata smoke tests.

## Current completion status

- `Печать`: first working release is complete as a release candidate. Completed work and remaining backlog are tracked in `docs/print-module-plan.md` and `modules/print/README.md`.
- `Видимость`: planned implementation tasks are complete; remaining work is manual visual QA in Revit following `docs/manual-qa.md`.

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
