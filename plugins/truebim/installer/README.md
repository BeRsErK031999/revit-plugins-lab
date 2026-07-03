# Installer

TrueBIM uses Inno Setup for the release installer.

The active installer script is:

```text
plugins/truebim/installer/TrueBIM.iss
```

Build all Revit-version payloads and compile the installer from the repository root:

```powershell
.\plugins\truebim\scripts\build-installer.ps1
```

The script emits:

```text
dist/revit/2019 ... dist/revit/2025
dist/installer/TrueBIM-Setup.exe
```

The release installer is current-user and does not require admin privileges:

```text
%APPDATA%\TrueBIM\<year>\
%APPDATA%\Autodesk\Revit\Addins\<year>\TrueBIM.addin
```

The installer defaults to Revit versions detected on the PC. A user can select an undetected version only after explicit confirmation.

Local compiler path used on the development workstation:

```text
C:\Users\Borodin_Artem\AppData\Local\Programs\Inno Setup 6\ISCC.exe
```
