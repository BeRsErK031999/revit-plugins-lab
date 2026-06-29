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

## Core workflow status

- Collector: done.
- Rules: done.
- Preview: done.
- Duplicate detection: done.
- Read-only workflow: done.
- Revit write operation: not started.
- UI: not started.
