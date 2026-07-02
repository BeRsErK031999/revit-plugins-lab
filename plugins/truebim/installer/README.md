# Installer

Installer work starts after the first buildable TrueBIM shell.

Initial installer target:

- Current-user installation.
- Revit 2022 and Revit 2025 add-in manifests.
- Selectable module components.
- TrueBIM Core is required.
- Sheet Numbering and Schedule Column Collapse are optional but selected by default.
- Upgrade cleanup removes install-owned `Core`, `Modules`, `Assets`, and `Docs` folders before copying the new package.

Recommended first implementation: Inno Setup.

The installer drafts are `TrueBIM.iss` for Revit 2025 and `TrueBIM-2022.iss` for Revit 2022.

Local compiler path:

```text
C:\Users\Borodin_Artem\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```
