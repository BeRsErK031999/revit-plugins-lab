# TrueBIM Technical Specification

## Goal

Create a modular Revit add-in platform where the user installs TrueBIM once and chooses which tools to install or enable.

The platform starts with sheet numbering and schedule column collapse modules.

## Product principles

- Keep Revit ribbon clean.
- Avoid installing many independent add-ins with separate tabs.
- Let the user choose modules during installation.
- Let the user enable or disable modules later.
- Keep module logic isolated so modules can be developed, tested, and released independently.

## First module: sheet-numbering

### Initial scope

- Read sheets from the active Revit document.
- Allow renumbering by ordered sheet list.
- Support prefix, suffix, start number, and numeric padding.
- Show preview before applying changes.
- Detect duplicate sheet numbers before commit.
- Write changes only inside a Revit transaction.

### Out of initial scope

- Cloud licensing.
- Online marketplace.
- Auto-update.
- Complex company standard rule engine.
- Installer-finalized multi-target release beyond local Revit 2022 and Revit 2025 deploy scripts.

These can be added after the first stable local Revit 2022 and Revit 2025 workflows.

## Module: schedule-column-collapse

### Initial scope

- Work from an active schedule, a selected schedule on a sheet, or a sheet with exactly one placed schedule.
- Duplicate the source schedule before changing field visibility.
- Analyze displayed schedule body values without relying on company-specific field names.
- Hide numeric fields when all numeric values in the column are zero.
- Keep text, service, and total columns visible.
- Write changes only to the duplicated schedule inside one Revit transaction.

### Out of initial scope

- Batch processing multiple schedules from one sheet.
- Replacing a schedule instance on a sheet with the collapsed copy.
- Replacing the current Revit 2022 and Revit 2025 local deploy scripts with a finalized multi-version installer.

## Installer scope

The installer should install the TrueBIM shell and selected modules.

Planned sections:

- TrueBIM Core
- Sheet Numbering
- Schedule Column Collapse
- Future modules
- Desktop shortcuts and documentation

## Runtime scope

TrueBIM should store enabled modules in a local settings file.

Suggested path:

```text
%APPDATA%\TrueBIM\settings.json
```

The Revit add-in manifest should load only the TrueBIM shell. The shell decides which modules are visible and enabled.
