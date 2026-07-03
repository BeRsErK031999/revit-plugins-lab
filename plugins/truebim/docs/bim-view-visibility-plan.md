# BIM View Visibility Plan

Status updated: 2026-07-03.

## Scope

Add a BIM-panel visibility tool that lets users turn Revit categories on and off in the active view. Keep the current Print module work isolated: do not edit `plugins/truebim/src/TrueBIM.App/Modules/Print`, `plugins/truebim/modules/print`, or `plugins/truebim/docs/print-module-plan.md` in this task.

`Видимость` is a core ribbon command in `TrueBIM.App`, not a separately installed `module.json` module. It belongs to the `TrueBIM` ribbon tab, `БИМ` panel.

## Completed Work

1. Added the `Видимость` button to the `БИМ` ribbon panel.
2. Implemented the first active-view category visibility workflow:
   - reads controllable categories from the active view;
   - shows each category with its current visibility state;
   - lets the user toggle categories on and off;
   - applies changes inside one Revit transaction;
   - reports clear status text after loading, filtering, and applying changes.
3. Added category grouping and filtering:
   - group filter for model, annotation, analytical, internal, and other category types;
   - search by category name;
   - filtered/total category counts in the status area.
4. Added a generated eye-style icon through `IconFactory`; no extra bitmap asset is required.
5. Added logging for command startup, empty category cases, and applied visibility counts.
6. Added manual QA coverage for Revit 2022 and Revit 2025 in `manual-qa.md`.
7. Fixed installer/preflight packaging so the visibility build can reach local Revit QA.
8. Added automated smoke tests for ribbon metadata so the `Видимость` button stays on `БИМ` with the expected command and icon.

## Completed Tasks

All planned tasks for the BIM View Visibility feature are complete.

1. Add a `Видимость` button to the `БИМ` ribbon panel and implement the active-view category visibility MVP. Completed in `75f3c44`.
2. Polish category grouping, search, and status text after Revit UI feedback. Completed in `85309a9`.
3. Add manual QA notes for Revit 2022/2025 and keep packaging isolated from Print module work. Completed in `5859481`.
4. Run local QA preflight and fix packaging/deploy blockers. Completed in `12b87f5` and installer follow-up commits.
5. Add automated smoke coverage for the `Видимость` ribbon button metadata. Completed in `00657f2`.

## Verification Status

Last verified locally on 2026-07-03.

- `C:\Program Files\dotnet\dotnet.exe build TrueBIM.sln --configuration Release` passed.
- `C:\Program Files\dotnet\dotnet.exe test TrueBIM.sln --configuration Release` passed: 110 tests.
- `C:\Program Files\dotnet\dotnet.exe format TrueBIM.sln --verify-no-changes` passed.
- `plugins\truebim\scripts\qa-preflight-2025.ps1` passed.
- `plugins\truebim\scripts\qa-preflight-2022.ps1` passed.
- `origin/main` contains the completed implementation through `00657f2`.

Local deploy paths verified by preflight:

- `%APPDATA%\Autodesk\Revit\Addins\2025\TrueBIM.addin`
- `%APPDATA%\TrueBIM\2025\Core\TrueBIM.App.dll`
- `%APPDATA%\Autodesk\Revit\Addins\2022\TrueBIM.addin`
- `%APPDATA%\TrueBIM\2022\Core\TrueBIM.App.dll`

## Remaining Follow-Up

No implementation tasks remain for this feature. The only follow-up is manual visual QA inside Revit after launching or restarting Revit:

1. Confirm `TrueBIM > БИМ > Видимость` is visible.
2. Open a normal model view and click `Видимость`.
3. Toggle one or two safe categories, apply, undo, and restore the categories.
4. Confirm the active-view category state matches the checklist in `manual-qa.md`.
