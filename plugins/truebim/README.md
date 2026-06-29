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

- `sheet-numbering` - sheet numbering and renumbering tools.

## Target Revit version

Initial development target: Revit 2025.

Technical target:

- `net8.0-windows`
- `RevitAPI.dll` and `RevitAPIUI.dll` from Revit 2025
