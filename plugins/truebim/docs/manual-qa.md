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
- confirm `Нумерация листов` is listed and enabled.

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
- copy icons to `%APPDATA%\TrueBIM\2025\Assets\icons`;
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

Open:

```text
%APPDATA%\TrueBIM\2025\Assets\icons\truebim-logotype.svg
```

Expected:

- the copied TrueBIM logotype asset exists after deploy.

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
- `Нумерация листов` is visible;
- `Нумерация листов` shows as enabled;
- closing the launcher returns to Revit without errors.

## Localization, Tooltips, and Icons

1. Open the `TrueBIM` launcher.
2. Confirm the launcher title area uses Russian labels for `Модули`, `Логи`, `Закрыть`, `Включено`, and `Открыть`.
3. Resize the launcher to its minimum width and confirm `Логи`, `Открыть`, and `Закрыть` are not clipped.
4. Hover `Включено`, `Логи`, and `Открыть`.
5. Confirm each tooltip appears and uses short Russian text.
6. Confirm action buttons show icons next to their labels.
7. Open `Нумерация листов`.
8. Resize the window to its minimum size and confirm the table remains visible and the bottom action panel is not clipped.
9. Confirm labels are Russian: `Префикс`, `Суффикс`, `Стартовый номер`, `Шаг`, `Разрядность`, `Порядок предпросмотра`, `Позиция`, `Предпросмотр`, `Экспорт`, `Применить`, and `Закрыть`.
10. Confirm the bottom panel has separate order controls on the left and preview/apply actions on the right.
11. Hover each numbering input, the placeholder checkbox, the order combo, the sheet table, and action buttons.
12. Confirm short Russian tooltips appear.

## Sheet Numbering Functional QA

1. Open a Revit 2025 sample project with several sheets.
2. Open `TrueBIM`.
3. Confirm `Нумерация листов` is listed from the installed module manifest.
4. Confirm the `Включено` checkbox controls whether `Открыть` is available.
5. Open `Нумерация листов`.
6. Confirm real document sheets are listed.
7. Select 1-2 test sheets.
8. Keep `Включать листы-заглушки` disabled unless specifically testing placeholders.
9. Set numbering rules that should not create duplicates.
10. Click `Предпросмотр`.
11. Confirm preview rows show expected old/new numbers.
12. Click `Экспорт`.
13. Confirm a CSV opens from `%APPDATA%\TrueBIM\Exports\SheetNumbering`.
14. Click `Применить`.
15. Confirm the dialog lists the changed count and first changes.
16. Accept confirmation.
17. Confirm the selected sheet numbers changed.
18. Run Revit Undo once.
19. Confirm all applied number changes revert in one undo step.
20. Repeat with duplicate numbering rules and confirm Apply stays disabled.

## Manual Sheet Order QA

1. Open `Нумерация листов` with at least four sheets.
2. Select a middle row in the table.
3. Click `Вверх`.
4. Confirm the selected row moves up one row, `Позиция` values update, and the status says to run preview again.
5. Click `Вниз`.
6. Confirm the selected row moves down one row and positions update.
7. Enter `1` in `Позиция` and click `К позиции`.
8. Confirm the selected row moves to the first position.
9. Confirm `Порядок предпросмотра` shows `Ручной порядок`.
10. Click `Предпросмотр`.
11. Confirm generated preview numbers follow the visible manual order.
12. Toggle one or more row checkboxes.
13. Confirm the manual row order is preserved.
14. Change `Порядок предпросмотра` to `Текущий номер`.
15. Confirm the table is sorted by current number and preview is invalidated.

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
9. Confirm `Нумерация листов` is listed.
10. Toggle `Включено` off and confirm `Открыть` becomes disabled.
11. Toggle `Включено` on and confirm `Открыть` becomes enabled.
12. Open `Нумерация листов`.
13. Confirm real document sheets are listed.
14. Click `Снять выбор`; expected: Apply disabled with a clear reason.
15. Click `Выбрать все`; expected: rows selected and preview still required.
16. Run `Предпросмотр` with `Включать листы-заглушки` disabled.
17. Confirm placeholder rows stay visible but are marked as excluded unless selected explicitly with the checkbox enabled.
18. Enable `Включать листы-заглушки` and run `Предпросмотр` again.
19. Click `Экспорт`; expected: CSV opens from `%APPDATA%\TrueBIM\Exports\SheetNumbering`.
20. Select only 1-2 safe test sheets.
21. Run `Предпросмотр` with non-conflicting numbering.
22. Click `Применить`; expected: confirmation dialog lists changed count and examples.
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
