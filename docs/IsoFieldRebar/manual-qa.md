# IsoField Rebar manual QA

Use this checklist for Revit 2022 and Revit 2025 smoke testing after local deploy
or installer validation. The scenarios are intentionally lightweight: they verify
the current MVP behavior without requiring a production recognition worker.

## Preconditions

- Revit is closed before local deploy or installer smoke.
- TrueBIM is installed for the target Revit version.
- Open a small local test model with:
  - one straight wall at least 3000 mm long and 2500 mm high;
  - one rectangular floor/slab at least 3000 x 3000 mm;
  - usable `RebarBarType` values with the diameters required by the selected legend
    (the reference maps may require 10, 12, 14, 16 and 20 mm).
- Keep `%APPDATA%\TrueBIM\Logs\truebim.log` open or easy to inspect.
- Test JSON fixtures are available under `docs/IsoFieldRebar/examples/`.

## Revit 2022 Smoke

1. Start Revit 2022 and open the test model.
2. Open `TrueBIM -> BIM -> Армирование по изополям`.
3. Choose the four reference PK LIRA maps `As1X/As2X/As3Y/As4Y`, assign exactly
   one bottom and one top layer for X and Y, then click `Загрузить зоны`.
4. Verify the WPF preview shows multiple contours, four numerical legends and
   the footer says the images were processed without changing the Revit model.
5. Click `Показать в Revit` on an active 2D view.
6. Verify preview `DetailCurve` lines appear on the active view and can be
   removed with `Очистить`.
7. Select the rectangular slab as host.
8. In `Привязка плиты по трём точкам`, enter two baseline pixel points and a
   third point away from their line. Pick all three matching positions on the
   slab top face.
9. Click `Проверить привязку`. Verify the preview shows the slab boundary, holes,
   numbered control points, clipped filled zones, retained area, scale, rotation,
   and third-point deviation. A valid boundary is green and rule calculation
   becomes available.
10. Keep `Только усиление поверх базовой сетки`, set cover/offset/minimum length,
    and click `Рассчитать раскладку`.
11. Verify every valid zone shows required and accepted `см²/м`, the selected
    `d...s...` combination, X/Y, top/bottom and estimated bar count. Thin lines
    must be visible inside the clipped overlay and no model elements are created.
12. Click `Применить изменения`, verify the confirmation states the mode, total
    number of individual bars and `add/update/delete/unchanged` diff, then confirm.
    Every created bar must remain inside the slab/zone and carry a
    `TrueBIM IsoFieldRebar` comment. One Undo must remove the entire transaction.
13. Recalculate and apply the same layout. Expected: every bar is `без изменений`,
    no transaction starts and no duplicate is created.
14. Repeat the source, preview, host, rule, and creation steps with
    `sample-wall-zones.json` and a straight wall host; slab binding must not be required.
15. Inspect `%APPDATA%\TrueBIM\Logs\truebim.log` for source selection,
    preview, rule preview, and write-flow entries.

## Revit 2025 Smoke

Repeat the Revit 2022 smoke in Revit 2025 with the same fixtures. Confirm:

- the ribbon button opens the same window;
- preview creation and cleanup work on a 2D view;
- wall and slab host selection both update the host status;
- engineering slab creation is guarded by explicit confirmation and reports its bar count;
- logs contain the same workflow milestones.

## Cancel And Guard Flows

- Cancel file selection. Expected: footer says selection was canceled, no model
  change, log records the cancellation.
- Verify `Загрузить зоны` is disabled without a selected source and its tooltip
  explains what is missing.
- Click `Показать в Revit` before loading JSON. Expected: user dialog and log
  warning.
- Click `Рассчитать раскладку` before selecting a host. Expected: read-only
  diagnostics, no model change.
- Cancel host selection with `Esc`. Expected: footer says selection was canceled,
  no model change.
- Click `Применить изменения` and choose `No` in the confirmation dialog. Expected:
  no rebar is created and log records user cancellation.

## Slab Binding Guard Flows

- Select a slab before zones are loaded. Expected: the binding section expands,
  controls remain disabled, and the status asks to load or recognize zones.
- Select a wall. Expected: the slab binding section collapses and rules do not
  require slab control points.
- Pick the first two control points at the same slab location or enter coincident image
  points. Expected: binding is rejected with a precise diagnostic.
- Put point 3 on the baseline of points 1–2. Expected: binding is rejected because
  the third point cannot independently verify the transform.
- Move host point 3 more than 50 mm from its mapped position. Expected: the
  deviation and tolerance are shown, point 3 is red, and rules stay disabled.
- Swap the second host point or toggle `Отразить Y изображения`. Expected: scale,
  rotation, overlay, and validation are recalculated only after
  `Проверить привязку`.
- Let a zone cross the slab edge or an opening. Expected: it is clipped, filled,
  outlined with a yellow dash, retained area is below 100%, and rules remain available.
- Place a zone fully outside the slab or fully inside an opening. Expected: its
  original outline is red and dashed; `Рассчитать раскладку` plus
  `Применить изменения` stay disabled.
- Apply manual zone correction after a valid binding. Expected: binding is
  rechecked and the clipped overlay updates instead of falling back to raw pixel preview.
- Save a valid profile, reset and reselect the same slab on the same view. Expected:
  `Загрузить профиль` becomes available and loading it reruns validation. On another
  view or slab the button stays disabled.

## P4.1 Engineering Rules And Clipped Layout

- Use a zone with range `7.85–9.58 см²/м`. Expected: the upper value `9.58`
  is required and `d10s200+d12s200` is accepted because its calculated area is
  not smaller than the requirement.
- Temporarily replace the upper legend label in a test fixture with a smaller
  combination. Expected: the item says `принятая площадь меньше требуемой` and
  creation stays disabled.
- In `Только усиление поверх базовой сетки`, expected: the first component of
  each label is treated as existing base reinforcement and is not created. The
  confirmation repeats this assumption.
- In `Полное сочетание внутри зон`, expected: all components are included and
  parallel components use a phase offset instead of coincident bars. No base
  mesh is created outside recognized zones.
- Change cover, boundary offset or minimum length after preview. Expected: the
  preview is reset immediately and must be recalculated.
- Use a zone crossing an opening. Expected: each scan line splits at the opening;
  no bar crosses the void. Short residual pieces are removed by minimum length.
- Verify X and Y are on separate depth planes: X is closer to the respective
  face, Y is deeper with 5 mm clear spacing. Top and bottom layers must not clash.
- Remove one required diameter from the Revit model. Expected: the whole
  transaction rolls back with a missing-diameter diagnostic; the first available
  bar type must not be substituted.
- Set parameters so the estimate exceeds 5000 bars. Expected: preview is blocked
  before a Revit transaction starts.

## P5.1 Idempotent Apply And Ownership Diff

- Apply a valid engineering layout for the first time. Expected: the confirmation
  shows every planned bar in `Добавить`; comments contain `id=`, `sig=` and the
  selected host id.
- Apply the same layout again. Expected: all bars are `Без изменений`, the model
  is not modified and no additional Undo entry appears.
- Change cover, offset or minimum length and recalculate. Expected: bars with the
  same stable id and changed geometry are `Обновить`; no longer planned ids are
  `Удалить`. Confirming applies the whole diff in one Undo transaction.
- Move one owned bar manually without changing its comment. Expected: actual
  centerline comparison detects it as `Обновить` and restores the planned line.
- Copy an owned bar together with its TrueBIM comment. Expected: the duplicate
  stable id is healed as one `Обновить`, leaving exactly one planned element.
- Delete one owned bar manually. Expected: only that stable id becomes `Добавить`.
- Add manual Rebar on the same slab without the exact
  `TrueBIM IsoFieldRebar; id=...` marker. Expected: it is absent from the diff and
  remains untouched after apply.
- Test an element created by P4.1 before `sig=` was introduced. Expected: it is
  updated once to the new ownership metadata, then becomes `Без изменений`.
- Cancel the diff confirmation. Expected: no elements are added, changed or deleted.

## P6.3 Completion Summary And Artifacts

- Export the report before apply, then apply a valid diff. Expected: the green
  `Последнее применение` block shows added/updated/deleted/unchanged counts,
  host and local completion time; the earlier report is explicitly marked stale.
- Click `Обновить итоговый отчёт`. Expected: the same JSON/CSV pair is updated
  only after the click, schema is `1.2`, `applicationSummary.applied` is `true`,
  counts match the UI and created/deleted element ids are present.
- Verify the update action disappears for the current report and `Открыть отчёт`
  opens the JSON. Delete or move the JSON and verify the open action becomes
  disabled after refresh instead of failing silently.
- Click `Открыть лог`. Expected: `%APPDATA%\TrueBIM\Logs\truebim.log` exists and
  opens in the associated application.
- Change the source, binding or rule preview. Expected: the previous completion
  summary is hidden and cannot be mistaken for the new workflow state.

## Built-in PNG Recognition Smoke

1. Clear `TRUEBIM_ISOFIELD_WORKER` before starting Revit.
2. Choose the four reference PK LIRA images together.
3. Verify the status names the image processor `Встроенный` and recognition is
   available without additional setup.
4. Click `Распознать 4 изображения`.
5. Verify the summary reports four legends and a non-zero contour count.
6. Verify four compact legend cards are visible for `As1X`, `As2X`, `As3Y`,
   and `As4Y`; every card shows its full numerical range in `см²/м` and ordered
   color swatches. The card also shows the first and last `d...s...` label;
   every swatch tooltip contains the numerical range, both boundary labels,
   and HEX color.
7. Verify the preview contains closed hotspot envelopes, zone names contain a
   numerical range in `см²/м`, and diagnostics confirm recognition of numerical
   boundaries plus the maximum-level rule inside each contour.
8. Click `Исправить зоны`. Verify the table shows layer, zone id, confidence,
   include checkbox, legend class and current action for every contour.
9. Exclude one zone and change the class of another. Select two included zones
   of the same layer/class with Ctrl and click `Объединить выбранные`; verify the
   action column and footer counters update. Cross-layer or mixed-class merging
   must show an inline warning and remain unapplied.
10. Change the class or inclusion of one grouped row. Verify the complete merge
    group is removed automatically. Use `Сбросить правки` and confirm all rows
    return to their initial state.
11. Apply the corrections. Verify the window preview and contour count update,
    previously calculated rules are cleared, and diagnostics contain a manual
    correction summary. If Revit preview lines existed, repeat `Показать в Revit`
    and verify they are replaced rather than duplicated.
12. Confirm recognition and zone correction do not create or modify Revit
    elements.

## Optional CLI Worker Smoke

Only run this section when a real or fake worker is available. CLI configuration
must override the built-in runner.

1. Set `TRUEBIM_ISOFIELD_WORKER`, optional `TRUEBIM_ISOFIELD_WORKER_ARGS`, and
   optional `TRUEBIM_ISOFIELD_WORKER_TIMEOUT_SECONDS` before starting Revit.
2. In one file dialog choose four images whose names contain `As1X`, `As2X`,
   `As3Y`, and `As4Y` and whose pixel dimensions match.
3. Verify four thumbnails, detected roles, and the message that the set is ready.
4. For the four reference PK LIRA images, verify every row says
   `Роль: имя + заголовок` and its tooltip shows the detected role and confidence.
5. Assign one `Низ` and one `Верх` layer for each X/Y direction. Verify duplicate
   faces or `Не задано` keep `Применить изменения` disabled.
6. Change one role to create a duplicate. Verify `Распознать 4 изображения` is
   disabled and the exact missing/duplicate role is shown; restore the role.
7. Save `*.isofield-set.json`, close the window, reopen it, and select the manifest.
   Verify files, roles, dimensions, and face assignments are restored.
8. Click `Распознать 4 изображения`.
9. Verify the runner logs `Runner=CLI`, invokes four source files in role order,
   prepares temp request/output files, and validates every output JSON.
10. Verify merged contour ids and diagnostics keep their `As*` role prefixes.
11. Verify timeout or non-zero exit failures show a user-friendly dialog and a
   detailed log entry.

## Source Set Guard Flows

- Choose fewer or more than four images. Expected: exact selected count is shown
  and recognition remains disabled.
- Rename a reference image so its file name has no `As*` marker. Expected: the
  role is still read from the raster header and the row says `Роль: по заголовку`.
- Rename the `As2X` reference image so its name contains `As1X`. Expected: the
  row is highlighted, role remains empty, and the exact name/header conflict is
  shown until the user chooses a role manually.
- Choose an image whose name and header contain no supported marker. Expected:
  its role selector stays empty until the user assigns a role manually.
- Choose duplicate layer names. Expected: both the duplicate and missing roles
  are named in the source-set state.
- Choose images with different pixel dimensions. Expected: the set is blocked
  with a same-scale/export warning.
- Mix JSON and images in one selection. Expected: the selection is rejected and
  no model change occurs.
- After saving a manifest, modify one source image and reopen the manifest.
  Expected: the affected row reports SHA-256 mismatch and processing is blocked.
- Move the manifest together with all four images to another directory. Expected:
  relative paths still resolve and the set remains valid.

## Known Limitations

- External CLI remains an optional replacement for teams with a custom OCR/CV pipeline.
- Built-in contours are conservative convex envelopes of dense color regions,
  not exact finite-element boundaries.
- Numerical boundaries and `d10s200+...` labels are read only for the current
  PK LIRA raster template and known ordered label catalog. A changed font,
  grammar, or skipped catalog level falls back without accepting partial labels.
- Header detection is intentionally limited to the current PK LIRA marker style.
  A nonstandard font or scaled header falls back to file-name/manual assignment.
- P4.1 creates individual engineering bars, not grouped `Rebar Set` or
  `Area Reinforcement` elements. Neighboring zones are not merged.
- Wall placement supports simple straight walls, not curved or stacked walls.
- Slab placement consumes clipped top-face regions and holes. Full-combination
  mode does not create a background base mesh outside recognized zones; laps,
  anchorage, sloped slabs and compound structures remain unsupported.
- P5.1 diff is summarized before confirmation; there is no separate filterable
  per-stable-id diff table yet. The legacy straight-wall probe is not idempotent.
- Manual QA still needs real Revit for visual preview and rebar creation checks.
