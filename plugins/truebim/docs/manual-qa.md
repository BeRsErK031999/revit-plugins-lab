# TrueBIM Manual QA

## Revit 2025 UI Verification

Status: manual UI verification pending.

Last non-UI verification date: 2026-06-29.

## Local Deploy

From the repository root, run:

```powershell
.\plugins\truebim\scripts\deploy-local-2025.ps1 -Configuration Debug
```

The script should:

- build `TrueBIM.sln` with `C:\Program Files\dotnet\dotnet.exe`;
- copy `TrueBIM.App.dll` to `%APPDATA%\TrueBIM\2025\Core`;
- generate `%APPDATA%\Autodesk\Revit\Addins\2025\TrueBIM.addin`;
- write an absolute `Assembly` path in the installed `.addin` file.

## Manifest Check

After deploy, open:

```text
%APPDATA%\Autodesk\Revit\Addins\2025\TrueBIM.addin
```

Expected:

- `Assembly` is an absolute path, not `%APPDATA%`;
- the referenced `TrueBIM.App.dll` exists;
- `FullClassName` is `TrueBIM.App.App`.

## Revit UI Check

1. Open Revit 2025.
2. Wait for Revit to finish loading the start screen or a model.
3. Confirm the ribbon contains a `TrueBIM` tab.
4. Open the `TrueBIM` tab.
5. Confirm the `Tools` panel contains a `TrueBIM` button.
6. Click the `TrueBIM` button.

Expected result:

- a `TrueBIM` launcher window opens;
- the launcher lists installed modules;
- `Sheet Numbering` is visible;
- `Sheet Numbering` shows as enabled;
- closing the launcher returns to Revit without errors.

## Known Limitations

- Automated UI verification was attempted on 2026-06-29, but the Windows capture API failed to screenshot the Revit window with `SetIsBorderRequired failed: 0x80004002`.
- Revit accessibility automation exposed only the top-level window, not ribbon contents, so the `TrueBIM` tab and launcher still require manual visual confirmation.
- No functional sheet-numbering workflow exists yet; the launcher only verifies shell/module discovery.
