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
  - at least one usable `RebarBarType`, preferably names containing `10` and `12`.
- Keep `%APPDATA%\TrueBIM\Logs\truebim.log` open or easy to inspect.
- Test JSON fixtures are available under `docs/IsoFieldRebar/examples/`.

## Revit 2022 Smoke

1. Start Revit 2022 and open the test model.
2. Open `TrueBIM -> BIM -> Армирование по изополям`.
3. Choose `docs/IsoFieldRebar/examples/sample-slab-zones.json`.
4. Verify the WPF preview shows multiple contours and the footer says the JSON
   contract was read without changing the Revit model.
5. Click `Показать в Revit` on an active 2D view.
6. Verify preview `DetailCurve` lines appear on the active view and can be
   removed with `Очистить`.
7. Select the rectangular slab as host.
8. In `Привязка плиты по двум точкам`, enter two recognizable pixel points,
   click `Точка 1 на плите` and `Точка 2 на плите`, then select their matching
   positions on the slab top face.
9. Click `Проверить привязку`. Verify the preview shows the slab boundary, holes,
   numbered control points, mapped zones, scale, and rotation. A valid boundary
   is green and rule calculation becomes available.
10. Click `Рассчитать правила`.
11. Verify rule preview lists valid slab rules and no model elements are created.
12. Click `Создать пробное армирование`, confirm the dialog, and verify test rebar is
    created. Undo must remove it.
13. Repeat the source, preview, host, rule, and creation steps with
    `sample-wall-zones.json` and a straight wall host; slab binding must not be required.
14. Inspect `%APPDATA%\TrueBIM\Logs\truebim.log` for source selection,
    preview, rule preview, and write-flow entries.

## Revit 2025 Smoke

Repeat the Revit 2022 smoke in Revit 2025 with the same fixtures. Confirm:

- the ribbon button opens the same window;
- preview creation and cleanup work on a 2D view;
- wall and slab host selection both update the host status;
- test rebar creation is guarded by explicit confirmation;
- logs contain the same workflow milestones.

## Cancel And Guard Flows

- Cancel file selection. Expected: footer says selection was canceled, no model
  change, log records the cancellation.
- Verify `Загрузить зоны` is disabled without a selected source and its tooltip
  explains what is missing.
- Click `Показать в Revit` before loading JSON. Expected: user dialog and log
  warning.
- Click `Рассчитать правила` before selecting a host. Expected: read-only
  diagnostics, no model change.
- Cancel host selection with `Esc`. Expected: footer says selection was canceled,
  no model change.
- Click `Создать пробное армирование` and choose `No` in the confirmation dialog. Expected:
  no rebar is created and log records user cancellation.

## Slab Binding Guard Flows

- Select a slab before zones are loaded. Expected: the binding section expands,
  controls remain disabled, and the status asks to load or recognize zones.
- Select a wall. Expected: the slab binding section collapses and rules do not
  require slab control points.
- Pick both control points at the same slab location or enter coincident image
  points. Expected: binding is rejected with a precise diagnostic.
- Swap the second host point or toggle `Отразить Y изображения`. Expected: scale,
  rotation, overlay, and validation are recalculated only after
  `Проверить привязку`.
- Place a mapped zone outside the slab or inside an opening. Expected: slab
  boundary and offending zone are red, the zone remains visible, and `Рассчитать правила`
  plus `Создать пробное армирование` stay disabled.
- Apply manual zone correction after a valid binding. Expected: binding is
  rechecked; if a corrected zone exits the slab, the invalid red overlay remains
  visible instead of falling back to raw pixel preview.

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
   faces or `Не задано` keep `Создать пробное армирование` disabled.
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
- Current write-flow creates test rebar only; it is not a production reinforcement
  layout engine.
- Wall placement supports simple straight walls, not curved or stacked walls.
- Slab overlay reads the top face and blocks zones outside boundaries or inside
  holes, but does not clip zone geometry. Test rebar placement still uses bounding
  boxes and does not account for holes, sloped slabs, or compound structure.
- Manual QA still needs real Revit for visual preview and rebar creation checks.
