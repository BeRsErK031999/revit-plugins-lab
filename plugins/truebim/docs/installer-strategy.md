# Installer Strategy

## Goal

Build a modular installer where the user can choose which TrueBIM components to install.

## Recommended path

Use an installer system that supports selectable components and silent installs.

Good candidates:

- Inno Setup for a simple first installer.
- WiX Toolset for a more enterprise-friendly MSI path.

Initial recommendation: start with Inno Setup because it is fast, readable, and enough for the first TrueBIM prototype.

## Installer components

```text
TrueBIM Core
  TrueBIM.App.dll
  TrueBIM.addin
  Shared files

Modules
  Sheet Numbering
  Future modules

Documentation
  Local README files
```

## Revit add-in manifest location

For all users:

```text
C:\ProgramData\Autodesk\Revit\Addins\<year>\TrueBIM.addin
```

For current user:

```text
%APPDATA%\Autodesk\Revit\Addins\<year>\TrueBIM.addin
```

Release installer target: current-user install through `%APPDATA%\TrueBIM` and `%APPDATA%\Autodesk\Revit\Addins\<year>`.
Current-user scripts remain available for local development deploy.

## Module selection model

Installer choices should map to folders:

```text
%APPDATA%\TrueBIM\<year>\
%APPDATA%\TrueBIM\<year>\Modules\SheetNumbering\
```

The `.addin` file should point to the core shell assembly.
The shell then loads installed modules.
