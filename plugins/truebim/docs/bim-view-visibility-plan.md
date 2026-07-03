# BIM View Visibility Plan

## Scope

Add a BIM-panel visibility tool that lets users turn Revit categories on and off in the active view. Keep the current Print module work isolated: do not edit `plugins/truebim/src/TrueBIM.App/Modules/Print`, `plugins/truebim/modules/print`, or `plugins/truebim/docs/print-module-plan.md` in this task.

## Tasks

1. Add a `Видимость` button to the `БИМ` ribbon panel and implement the first active-view category visibility MVP. Done.
2. Polish category grouping, search, and status text after Revit UI feedback. Done.
3. Add manual QA notes for Revit 2022/2025 and update packaging/deploy scripts only if the module needs extra assets. Done.

## Current Step

Task 3 is implemented. Manual QA now covers the `Видимость` ribbon button and active-view category visibility flow for Revit 2022 and Revit 2025. No packaging or deploy script changes are required because the tool is a core ribbon command and uses an `IconFactory` vector icon.
