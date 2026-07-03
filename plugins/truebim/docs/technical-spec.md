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

These can be added after the first stable multi-version installer workflow.

## Module: schedule-column-collapse

### Initial scope

- Let the user choose the target schedule from a picker.
- Change the selected schedule directly; users duplicate the schedule themselves if they need to preserve the original.
- Analyze displayed schedule body values without relying on company-specific field names.
- Hide numeric fields when all numeric values in the column are zero.
- Ignore numeric-looking column headings such as `-10` and `-12` while analyzing body values.
- Keep text and service columns visible.
- Hide zero total columns; keep total columns visible when they contain non-zero values.
- Write changes only to the selected schedule inside one Revit transaction.

### Out of initial scope

- Batch processing multiple schedules from one sheet.

## Core command: BIM View Visibility

### Completed scope

- Add the `Видимость` command to the `TrueBIM` ribbon tab, `БИМ` panel.
- Work against the active Revit view only.
- Show controllable categories with their current active-view visibility state.
- Group categories by category type and support search by category name.
- Apply visibility changes inside one Revit transaction.
- Keep the command in `TrueBIM.App` core code instead of installing it as a `module.json` module.
- Cover ribbon metadata with automated smoke tests so the button stays on the expected panel with the expected command and icon.

### Manual verification scope

- Revit 2022 and Revit 2025 local preflight must pass before manual UI QA.
- Manual UI QA should confirm the `БИМ > Видимость` button opens, toggles safe categories, supports undo, and reports clear status text.

## Installer scope

The release installer should install the TrueBIM shell and selected modules for one or more Revit versions from 2019 through 2025.

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
