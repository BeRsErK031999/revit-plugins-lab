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

For Revit 2026, build and deploy the matching API payload with:

```powershell
.\plugins\truebim\scripts\build-artifacts-2026.ps1 -Configuration Release
.\plugins\truebim\scripts\deploy-local-2026.ps1 -Configuration Release -SkipBuild
```

Both scripts use `C:\Program Files\Autodesk\Revit 2026` by default and accept `-RevitApiRoot` when the Revit 2026 API reference assemblies are stored elsewhere.

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

## Print Module QA

Run this check in both Revit 2022 and Revit 2025 after the matching local deploy or QA preflight script passes.

Status: the first working release is implemented and preflight-verified. Repeat this checklist on a safe sample model before handing a build to users.

1. Open a sample project with several printable sheets.
2. Open `TrueBIM`.
3. Confirm the ribbon contains one `Печать` button and does not contain separate `Печать PDF` / `Печать DWG` buttons.
4. Open `Печать` and confirm PDF, DWG, DXF, and DWF are selectable in the same window.
5. Confirm the window lists printable sheets with source, sheet number, sheet name, format, export status, and filename preview columns.
6. If more than one document is open, change the source filter and confirm selected sheets are preserved when switching back.
7. Set an export folder.
8. Click `Настройки...`, change the filename mask, and confirm filename previews update. Collapse the settings and confirm the sheet table and format row remain available.
9. Use a mask containing `{Номер листа}`, `{Имя листа}`, `{Номер проекта}`, `{Имя проекта}`, `{Имя документа}`, `{Дата:yyyy-MM-dd}`, `{Счетчик}`, and `{Счетчик:000}` as needed. Existing masks with `{SheetNumber}` / `{SheetName}` must keep working.
10. Confirm invalid Windows filename characters are normalized in previews.
11. Enable PDF and select separate PDF mode.
12. Choose PDF color, raster quality, and raster/vector settings.
13. Export 1-2 safe sheets and confirm separate PDF files are created.
14. Select the combined PDF mode, enter a combined PDF name, export again, and confirm one PDF is created per source document.
15. Enable DWG and DXF.
16. If DWG/DXF export setups are available, choose a saved setup; otherwise keep the default setup option.
17. Click `Настройки DWG...`, change a safe option such as `FileVersion`, `Colors`, or `SharedCoords`, save the TrueBIM profile, then apply it.
18. Click `Проверить настройки` and confirm the summary shows sheet count, folder, DWG version, color mode, coordinates, and profile.
19. Export 1-2 safe sheets and confirm DWG/DXF files are created or a clear Revit export error is shown.
20. Enter a customer name in the preset field and click `Сохранить`. Change several formats/settings, select the saved preset again, and confirm the complete configuration is restored.
21. Delete a temporary preset and confirm another preset remains selected and usable.
22. Close and reopen `Печать`.
23. Confirm the last window state and selected preset are restored from `%APPDATA%\TrueBIM\<RevitVersion>\print-settings.json`, `%APPDATA%\TrueBIM\<RevitVersion>\print-presets.json`, and `%APPDATA%\TrueBIM\<RevitVersion>\dwg-export-profiles.json`.
24. Review `%APPDATA%\TrueBIM\Logs\truebim.log`.

Expected logs:

- Print module/window startup with sheet/source counts;
- selected PDF mode and PDF settings;
- selected DWG/DXF setup or default fallback;
- applied DWG profile and resulting key `DWGExportOptions`;
- exported file counts and any per-sheet failures.
- selected printer, print setup, document and selected-sheet count;
- submission and result of each sheet sent through the print driver;
- driver/setup errors and restoration of the previous Revit print settings.

### Print Large Sheet List QA

1. Open a project or several sources containing at least 95 visible sheets.
2. Open `Печать` and confirm the vertical scrollbar is visible on the sheet table immediately.
3. Resize the window down to its minimum height and confirm the table keeps a bounded viewport instead of extending below the window.
4. Expand all source and volume groups and scroll from the first sheet to the last with the mouse wheel and scrollbar thumb.
5. Sort by sheet number, switch the source filter, and confirm the scrollbar remains visible and usable.
6. Confirm row virtualization remains responsive while scrolling the full list.

### Print Placeholder Sheet QA

1. Open a project whose sheet list contains at least one Revit placeholder sheet.
2. Open `Печать` with fresh/default settings and confirm placeholder sheets are hidden.
3. Confirm the option is named `Неразмещенные листы (заглушки)` and its tooltip explains that these are preliminary sheet-list rows without a created regular Revit sheet.
4. Enable the option and confirm the placeholder row appears with status `Заглушка — не печатается`.
5. Hover the status and confirm the same explanation is available directly from the row.
6. Confirm the row checkbox is disabled and the placeholder cannot increase the selected or exported sheet count.

### Actual Printer QA

Run this scenario first with one safe sheet and a test printer or paused print queue.

1. Open `Печать` and confirm the window title is `Печать и экспорт`, the initial mode is `Экспорт в файлы`, and the primary action is `Экспортировать`.
2. Switch to `Печать на принтер` and confirm export folder, masks, format switches, and the filename column are hidden.
3. Confirm the printer list contains installed Windows printers and prefers the current Revit printer when it is available.
4. Confirm the range is explicitly shown as `Выбранные листы` and cannot include unselected rows.
5. Keep `Текущая настройка каждого документа` or select a saved Revit print setup, then send one selected sheet to a physical/test printer.
6. Confirm the row status changes from `Печать: в очереди` to `Напечатан`, or to `Ошибка печати` with a clear summary if the driver rejects the job.
7. Open the native Revit print dialog after completion and confirm the printer, print-to-file flag, range, and current print setup were restored.
8. Select a printer whose name contains `PDF`; confirm it is marked as a PDF driver, the warning is visible, and choosing `No` in the confirmation sends no job.
9. If corporate QA allows it, confirm `Yes` delegates to the PDF driver's own dialog/path behavior; use `Экспорт в файлы` for predictable batch PDF output.
10. With sheets from two open documents, select a named print setup and confirm a missing setup in either source produces a source-specific error instead of silently using another setup.
11. Close and reopen the window and confirm the printer is not persisted yet; persistence remains gated by UX/version QA.

Completed first-release tasks:

- ribbon/launcher registration;
- sheet selection from open documents;
- filename template preview and normalization;
- separate and combined PDF export;
- PDF color, raster quality, and raster/vector settings;
- DWG/DXF export setup selection, DWG profile options, and default fallback;
- persisted print settings;
- unit-test coverage for clean logic;
- Revit 2022 and Revit 2025 local deploy/preflight.

Backlog outside the first release:

- source tabs;
- linked models;
- print sets;
- named configurations;
- sheet-parameter filters and grouping;
- advanced CAD parameters beyond saved Revit export setups.

## BIM View Visibility QA

Run this check in both Revit 2022 and Revit 2025 after the matching local deploy or QA preflight script passes.

1. Open a sample project and activate a normal model view, for example a floor plan or 3D view. Do not run this check on a view template.
2. Open the `TrueBIM` ribbon tab.
3. Confirm the `БИМ` panel contains the `Видимость` button with an eye-style icon.
4. Click `Видимость`.
5. Confirm the window title is `Видимость` and the header shows the active view name.
6. Confirm categories are grouped by type, with group headers such as `Модель`, `Аннотации`, and `Аналитическая модель` when those category types are available in the active view.
7. Change `Группа` to `Модель` and confirm only model categories remain visible in the list.
8. Type a common category name fragment such as `Стены`, `Окна`, or `Двери` in `Поиск`.
9. Confirm the list filters by category name and the status text reports filtered and total category counts.
10. Click `Очистить` and confirm the full selected group returns.
11. Toggle 1-2 safe categories off, then click `Применить`.
12. Confirm the selected category elements disappear from the active view and the status reports how many categories were hidden or shown.
13. Reopen `Видимость` on the same active view and confirm the toggled categories still show their current visibility state.
14. Toggle the same categories on and click `Применить`.
15. Confirm the selected category elements return to the active view.
16. Use Revit Undo once after an Apply action and confirm the category visibility changes from that Apply action revert together.
17. Click `Видимость` while a view template is active, if a safe template is available, and confirm the command shows a clear message instead of editing the template.

Expected logs:

- View Visibility requested without an active document, when applicable;
- View Visibility found no controllable categories, when applicable;
- applied category visibility counts for the active view.

Packaging note:

- `Видимость` is a core ribbon command in `TrueBIM.App`, not an installed `module.json` module.
- The icon is generated by `IconFactory`, so no extra image asset, module manifest, deploy script, or installer payload is required for this tool.

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
