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
8. Click `Рассчитать правила`.
9. Verify rule preview lists valid slab rules and no model elements are created.
10. Click `Создать пробное армирование`, confirm the dialog, and verify test rebar is
    created. Undo must remove it.
11. Repeat steps 3-10 with `sample-wall-zones.json` and a straight wall host.
12. Inspect `%APPDATA%\TrueBIM\Logs\truebim.log` for source selection,
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

## CLI Worker Smoke

Only run this section when a real or fake worker is available.

1. Set `TRUEBIM_ISOFIELD_WORKER`, optional `TRUEBIM_ISOFIELD_WORKER_ARGS`, and
   optional `TRUEBIM_ISOFIELD_WORKER_TIMEOUT_SECONDS` before starting Revit.
2. Choose an image file rather than JSON.
3. Click `Распознать изображение`.
4. Verify the runner logs `Runner=CLI`, prepares temp request/output files, and
   validates output JSON.
5. Verify timeout or non-zero exit failures show a user-friendly dialog and a
   detailed log entry.

## Known Limitations

- Real contour recognition is still external to this module.
- Current write-flow creates test rebar only; it is not a production reinforcement
  layout engine.
- Wall placement supports simple straight walls, not curved or stacked walls.
- Slab placement uses bounding boxes and does not account for holes, sloped slabs,
  or compound structure.
- Manual QA still needs real Revit for visual preview and rebar creation checks.
