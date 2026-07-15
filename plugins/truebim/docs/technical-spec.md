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

## Module: print

### Completed first-release scope

- Add the `Печать` module as installed module `truebim.print`.
- Open the print window from the `TrueBIM` launcher and the `БИМ` ribbon panel.
- Read printable sheets from open Revit documents.
- Preserve sheet selection while switching the source filter.
- Build filenames from supported tokens and normalize invalid Windows filename characters.
- Export selected sheets to separate PDF files.
- Export selected sheets to one combined PDF per source document.
- Support PDF color, raster quality, and raster/vector settings.
- Export selected sheets to DWG and DXF.
- Read saved Revit CAD export setup names and use selected DWG/DXF predefined options.
- Fall back to default DWG/DXF options when no setup is selected or no setups are available.
- Persist basic window settings in `%APPDATA%\TrueBIM\<RevitVersion>\print-settings.json`.
- Expose one `Печать` ribbon entry for PDF/DWG/DXF/DWF and keep detailed format settings collapsible in the same window.
- Persist named customer presets in `%APPDATA%\TrueBIM\<RevitVersion>\print-presets.json`, including PDF settings, CAD selections, and the active DWG profile snapshot.
- Cover clean print logic with unit tests.
- Pass Revit 2022 and Revit 2025 local preflight for build, tests, local deploy, and installer artifacts.

### Backlog

- Source tabs instead of only the current source filter.
- Linked model sheet sources.
- Print sets.
- Sheet-parameter filters and grouping.
- Advanced CAD parameters beyond saved Revit export setups.

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

The release installer should install the TrueBIM shell and selected modules for one or more Revit versions from 2019 through 2026.

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
