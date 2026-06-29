# TrueBIM Technical Specification

## Goal

Create a modular Revit add-in platform where the user installs TrueBIM once and chooses which tools to install or enable.

The first practical module is sheet numbering.

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
- Revit 2024 and older multi-target builds.

These can be added after the first stable Revit 2025 version.

## Installer scope

The installer should install the TrueBIM shell and selected modules.

Planned sections:

- TrueBIM Core
- Sheet Numbering
- Future modules
- Desktop shortcuts and documentation

## Runtime scope

TrueBIM should store enabled modules in a local settings file.

Suggested path:

```text
%APPDATA%\TrueBIM\settings.json
```

The Revit add-in manifest should load only the TrueBIM shell. The shell decides which modules are visible and enabled.
