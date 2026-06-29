# AI Development Plan

Use this plan when another AI agent implements TrueBIM tasks.
Each task should be delivered as a small commit and reviewed before moving to the next one.

## Review protocol

For each completed AI task, provide:

- Commit hash.
- Changed files summary.
- Screenshots or logs when UI/build behavior changed.
- Exact verification commands and results.
- Known limitations.

The reviewer should check:

- Build passes.
- No unrelated files changed.
- Revit API calls are isolated behind small services where practical.
- Revit model writes happen inside transactions.
- User-facing actions have preview and cancellation paths.
- Installer changes do not install unwanted modules by default.

## Phase 1: Buildable TrueBIM shell

1. Add a solution file for TrueBIM.
2. Make `TrueBIM.App` build cleanly with .NET SDK 8.
3. Add a local deploy script that copies the DLL and `.addin` manifest to the current-user Revit 2025 add-ins folder.
4. Verify Revit 2025 loads the TrueBIM tab.
5. Replace the placeholder task dialog with a minimal module launcher window.

## Phase 2: Sheet numbering core

1. Add sheet collection service using `FilteredElementCollector`.
2. Add sheet model with current number, name, element id, and placeholder status.
3. Add numbering rules:
   - prefix
   - suffix
   - start number
   - increment
   - padding
4. Add preview generation without writing to the model.
5. Add duplicate detection against selected sheets and existing document sheets.
6. Add unit tests for pure numbering logic.

## Phase 3: Sheet numbering UI

1. Create WPF window for sheet numbering.
2. Show sortable sheet list.
3. Add controls for numbering rules.
4. Show old number and preview number.
5. Block Apply when duplicates or invalid values exist.
6. Add cancel path that makes no Revit changes.

## Phase 4: Revit write operation

1. Apply sheet number changes in a single Revit transaction.
2. Report changed, skipped, and failed sheets.
3. Handle read-only/workshared documents gracefully.
4. Handle duplicate sheet number exceptions safely.
5. Verify Revit undo rolls back the full operation.

## Phase 5: Modular installer

1. Build artifacts into `plugins/truebim/artifacts`.
2. Compile the Inno Setup installer.
3. Ensure Core is required and Sheet Numbering is selectable.
4. Install to current user by default.
5. Verify uninstall removes TrueBIM files and add-in manifest.

## Phase 6: Review hardening

1. Add logging.
2. Add settings file for enabled modules.
3. Add module enable/disable UI.
4. Add release notes.
5. Add manual QA checklist for Revit 2025.
