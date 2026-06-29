# Module Architecture

## Approach

Use one Revit add-in entry point and a modular internal registry.

This avoids polluting Revit with many unrelated add-ins while still allowing TrueBIM to grow into multiple tools.

## Layers

```text
TrueBIM.App
  Revit entry point
  Ribbon bootstrap
  Main command
  Module registry

TrueBIM.Modules
  SheetNumbering
  Future modules

TrueBIM.Shared
  Revit context helpers
  Settings
  Logging
  UI contracts
```

## Module contract

Each module should expose:

- Stable id.
- Display name.
- Description.
- Installed state.
- Enabled state.
- Command entry point.

Example module id:

```text
truebim.sheet-numbering
```

## Runtime enable/disable

The installer controls what is present on disk.
Runtime settings control what is visible and usable inside TrueBIM.

This gives two levels of control:

- Installation: what files exist.
- Runtime: what tools are enabled.

## Future split strategy

Keep modules inside this repository while TrueBIM is young.
Move a module to its own repository only when it has independent release, licensing, or support boundaries.
