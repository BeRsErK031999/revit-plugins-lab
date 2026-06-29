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
- Placeholder sheets are visible but excluded from preview/apply unless `Include placeholders` is enabled.
- Keep the operation reversible through one Revit transaction.
- Apply is all-or-nothing; failures roll back the whole transaction.

## Current workflow

1. Open TrueBIM from the Revit ribbon.
2. Open Sheet Numbering.
3. Select target sheets.
4. Optionally enable `Include placeholders`.
5. Choose preview order.
6. Configure numbering rules.
7. Click `Preview`.
8. Review `Preview Number` and `Status / Issue`.
9. Optionally click `Export Preview` to save a CSV for review.
10. Click `Apply` only after confirming the changes.

`Apply` is enabled only when:

- at least one eligible sheet is selected;
- preview is current;
- there are no duplicate conflicts;
- at least one preview row changes.

Before writing, the module shows a confirmation dialog with the changed count and the first five `OldNumber -> NewNumber` examples. The write is one Revit transaction, so Revit Undo should revert the operation in one step.

## Exports and logs

Preview export CSV files are written to:

```text
%APPDATA%\TrueBIM\Exports\SheetNumbering\
```

Logs are written to:

```text
%APPDATA%\TrueBIM\Logs\truebim.log
```

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
- Apply confirmation: done.
- Preview export: done.
- Placeholder include/exclude control: done.
- Undo behavior: one Revit transaction should roll back Apply in one Undo step.
- Full read-only UI workflow before Apply: done.
- Installer: not finalized.
