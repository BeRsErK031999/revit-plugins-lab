# TrueBIM Manual QA

## Revit 2025 UI Verification

Status: partial UI verification complete; launcher click verification pending.

Last verification date: 2026-06-29.

Verified on 2026-06-29:

- Revit 2025 launched with a sample project.
- The `TrueBIM` ribbon tab was visible.
- The `Tools` panel was visible on the `TrueBIM` tab.

Pending manual check:

- click the `TrueBIM` button;
- confirm the launcher window opens;
- confirm `Sheet Numbering` is listed and enabled.

## Local Deploy

From the repository root, run:

```powershell
.\plugins\truebim\scripts\deploy-local-2025.ps1 -Configuration Debug
```

The script should:

- build `TrueBIM.sln` with `C:\Program Files\dotnet\dotnet.exe`;
- copy `TrueBIM.App.dll` to `%APPDATA%\TrueBIM\2025\Core`;
- copy `module.json` to `%APPDATA%\TrueBIM\2025\Modules\SheetNumbering`;
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

Open:

```text
%APPDATA%\TrueBIM\2025\Modules\SheetNumbering\module.json
```

Expected:

- `id` is `truebim.sheet-numbering`;
- `revitVersions` contains `2025`.

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

## Sheet Numbering Functional QA

1. Open a Revit 2025 sample project with several sheets.
2. Open `TrueBIM`.
3. Confirm `Sheet Numbering` is listed from the installed module manifest.
4. Confirm the `Enabled` checkbox controls whether `Open` is available.
5. Open `Sheet Numbering`.
6. Confirm real document sheets are listed.
7. Select 1-2 test sheets.
8. Keep `Include placeholders` disabled unless specifically testing placeholders.
9. Set numbering rules that should not create duplicates.
10. Click `Preview`.
11. Confirm preview rows show expected old/new numbers.
12. Click `Export Preview`.
13. Confirm a CSV opens from `%APPDATA%\TrueBIM\Exports\SheetNumbering`.
14. Click `Apply`.
15. Confirm the dialog lists the changed count and first changes.
16. Accept confirmation.
17. Confirm the selected sheet numbers changed.
18. Run Revit Undo once.
19. Confirm all applied number changes revert in one undo step.
20. Repeat with duplicate numbering rules and confirm Apply stays disabled.

Expected logs:

- manifest loading path and counts;
- Sheet Numbering preview start/result;
- export preview path;
- Apply confirmation accepted/cancelled;
- final Apply result.

## Known Limitations

- Automated UI screenshot through the Windows capture API failed with `SetIsBorderRequired failed: 0x80004002`; a passive desktop screenshot was used to confirm the loaded ribbon tab.
- Revit accessibility automation did not expose ribbon contents, and coordinate input was unavailable without a successful automation screenshot, so the launcher click still requires manual visual confirmation.
- Sheet Numbering functional QA still needs manual Revit verification for preview, export, apply, and undo.
