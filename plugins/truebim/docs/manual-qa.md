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

Close Revit before deploying. From the repository root, run the full preflight:

```powershell
.\plugins\truebim\scripts\qa-preflight-2025.ps1
```

The preflight script builds Release, runs tests, builds artifacts, compiles the installer when `ISCC.exe` is available, deploys locally, and verifies the installed add-in manifest and module manifest.
It fails before deploy if Revit is still running, because Revit can lock `TrueBIM.App.dll`.

If you only need to redeploy after the full preflight already passed, run:

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

## Full Manual QA Scenario

1. Close Revit before deploy.
2. Run:

   ```powershell
   .\plugins\truebim\scripts\qa-preflight-2025.ps1
   ```

3. Confirm the preflight summary is all `PASS`.
4. Open Revit 2025.
5. Open a sample project with sheets.
6. Confirm the `TrueBIM` ribbon tab and button are visible.
7. Click the `TrueBIM` button.
8. Click `Logs` and confirm the log file opens.
9. Confirm `Sheet Numbering` is listed.
10. Toggle `Enabled` off and confirm `Open` becomes disabled.
11. Toggle `Enabled` on and confirm `Open` becomes enabled.
12. Open `Sheet Numbering`.
13. Confirm real document sheets are listed.
14. Click `Clear Selection`; expected: Apply disabled with a clear reason.
15. Click `Select All`; expected: rows selected and preview still required.
16. Run `Preview` with `Include placeholders` disabled.
17. Confirm placeholder rows stay visible but are marked as excluded unless selected explicitly with the checkbox enabled.
18. Enable `Include placeholders` and run `Preview` again.
19. Click `Export Preview`; expected: CSV opens from `%APPDATA%\TrueBIM\Exports\SheetNumbering`.
20. Select only 1-2 safe test sheets.
21. Run `Preview` with non-conflicting numbering.
22. Click `Apply`; expected: confirmation dialog lists changed count and examples.
23. Accept confirmation.
24. Confirm selected sheet numbers changed.
25. Run one Revit Undo.
26. Confirm all changes from Apply are reverted in one undo step.
27. Run a duplicate/conflict scenario and confirm Apply is disabled.
28. Review `%APPDATA%\TrueBIM\Logs\truebim.log`.

If cleanup is needed, close Revit and run:

```powershell
.\plugins\truebim\scripts\clean-local-2025.ps1
```

This preserves logs, exports, and `module-settings.json`. To remove user data too:

```powershell
.\plugins\truebim\scripts\clean-local-2025.ps1 -IncludeUserData
```

## Failure Report

When reporting a QA failure, include:

- screenshot of the Revit/TrueBIM state;
- `%APPDATA%\TrueBIM\Logs\truebim.log`;
- exported preview CSV from `%APPDATA%\TrueBIM\Exports\SheetNumbering`, if preview/export was involved;
- exact preflight summary output if `qa-preflight-2025.ps1` failed.

## Known Limitations

- Automated UI screenshot through the Windows capture API failed with `SetIsBorderRequired failed: 0x80004002`; a passive desktop screenshot was used to confirm the loaded ribbon tab.
- Revit accessibility automation did not expose ribbon contents, and coordinate input was unavailable without a successful automation screenshot, so the launcher click still requires manual visual confirmation.
- Sheet Numbering functional QA still needs manual Revit verification for preview, export, apply, and undo.
