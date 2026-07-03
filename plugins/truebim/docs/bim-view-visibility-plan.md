# BIM View Visibility Plan

## Scope

Add a BIM-panel visibility tool that lets users turn Revit categories on and off in the active view. Keep the current Print module work isolated: do not edit `plugins/truebim/src/TrueBIM.App/Modules/Print`, `plugins/truebim/modules/print`, or `plugins/truebim/docs/print-module-plan.md` in this task.

## Tasks

1. Add a `Видимость` button to the `БИМ` ribbon panel and implement the first active-view category visibility MVP. Done.
2. Polish category grouping, search, and status text after Revit UI feedback. Done.
3. Add manual QA notes for Revit 2022/2025 and update packaging/deploy scripts only if the module needs extra assets.

## Current Step

Task 2 is implemented. The visibility window now groups categories by type, supports group filtering, searches by category name or group, and reports filtered/total visibility and change counts.
