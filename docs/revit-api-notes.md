# Revit API Notes

## Core concepts

- `IExternalCommand` is the entry point for a button-style command.
- `IExternalApplication` is used to add ribbon panels, startup logic, and shutdown logic.
- Model changes must run inside a Revit `Transaction`.
- Add-ins are loaded through `.addin` manifest files.
- `RevitAPI.dll` and `RevitAPIUI.dll` should be referenced from the matching Revit installation.

## Local API references

Revit API assemblies are available under:

```text
C:\Program Files\Autodesk\Revit {year}\RevitAPI.dll
C:\Program Files\Autodesk\Revit {year}\RevitAPIUI.dll
```

Installed years found locally: 2017-2025.
