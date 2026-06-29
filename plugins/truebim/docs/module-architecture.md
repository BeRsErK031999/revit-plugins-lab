# Module Architecture

## Approach

Use one Revit add-in entry point and a modular internal registry backed by installed module manifests.

This avoids polluting Revit with many unrelated add-ins while still allowing TrueBIM to grow into multiple tools.

## Layers

```text
TrueBIM.App
  Revit entry point
  Ribbon bootstrap
  Main command
  Module registry
  Installed module manifest loader
  Module settings

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
- Version.
- Supported Revit versions.
- Installed state.
- Enabled state.
- Command entry point.

Example module id:

```text
truebim.sheet-numbering
```

## Installed manifests

Installer component selection is represented at runtime by installed `module.json` files:

```text
%APPDATA%\TrueBIM\2025\Modules\*\module.json
```

The launcher loads these manifests for Revit 2025 and only includes modules whose ids map to known runtime implementations. Invalid manifests and unknown module ids are logged and skipped without breaking the launcher.

For local development, if the installed modules folder is missing entirely, the registry falls back to built-in development modules and logs that fallback. Once the folder exists, installed manifests are the source of runtime availability.

## Runtime enable/disable

The installer controls what is present on disk.
Runtime settings control whether installed modules are enabled and openable inside TrueBIM.

This gives two levels of control:

- Installation: what files exist.
- Runtime: what tools are enabled.

Runtime settings are stored in:

```text
%APPDATA%\TrueBIM\2025\module-settings.json
```

If a module has no user setting, the launcher uses `enabledByDefault` from the installed manifest. Disabled modules remain visible, but their `Open` button is disabled.

## Future split strategy

Keep modules inside this repository while TrueBIM is young.
Move a module to its own repository only when it has independent release, licensing, or support boundaries.
