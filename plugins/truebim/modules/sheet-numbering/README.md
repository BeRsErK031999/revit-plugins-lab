# Sheet Numbering

First TrueBIM module.

## Purpose

Renumber Revit sheets with preview, duplicate protection, and configurable numbering rules.

## Planned first workflow

1. Open TrueBIM from the Revit ribbon.
2. Choose Sheet Numbering.
3. Select target sheets.
4. Configure numbering:
   - Prefix
   - Start number
   - Padding
   - Suffix
5. Preview old and new sheet numbers.
6. Apply changes.

## Safety rules

- Never write without preview.
- Detect duplicates before transaction commit.
- Skip placeholder or filtered sheets only when the user chooses that behavior.
- Keep the operation reversible through one Revit transaction.
- Apply is all-or-nothing; failures roll back the whole transaction.

## Core workflow status

- Collector: done.
- Rules: done.
- Preview: done.
- Duplicate detection: done.
- Read-only workflow: done.
- Revit write operation: done for sheet number Apply.
- UI skeleton: done.
- Real document read-only preview: done.
- Selection UI: done.
- Preview sorting/order controls: done for original order, current number, and sheet name.
- Duplicate issues shown in UI: done.
- Apply button: enabled after a changed duplicate-free preview.
- Apply transaction: done with all-or-nothing rollback on failure.
- Undo behavior: one Revit transaction should roll back Apply in one Undo step.
- Full read-only UI workflow before Apply: done.
- Installer: not finalized.
