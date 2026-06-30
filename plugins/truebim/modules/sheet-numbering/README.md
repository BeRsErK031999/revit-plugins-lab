# Нумерация листов

First TrueBIM module.

## Purpose

Renumber Revit sheets with preview, duplicate protection, and configurable numbering rules.
The UI is localized for Russian-speaking users as `Нумерация листов`.

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
2. Open `Нумерация листов`.
3. Select target sheets.
4. Optionally enable `Включать листы-заглушки`.
5. Choose preview order: `Исходный порядок`, `Текущий номер`, `Название`, or `Ручной порядок`.
6. Configure numbering rules.
7. For manual order, select a row and use `Вверх`, `Вниз`, or `Позиция` + `Переместить`.
8. Click `Предпросмотр`.
9. Review `Предпросмотр` and `Статус / проблема`.
10. Optionally click `Экспорт` to save a CSV for review.
11. Click `Применить` only after confirming the changes.

## Manual order model

The table has a `Позиция` column showing the active preview order. Moving a row up, down, or to a numeric position switches the order mode to `Ручной порядок` and invalidates the current preview, so the user must click `Предпросмотр` again before applying. Checkbox selection is stored on each row object and is preserved while rows are reordered.

Changing the order combo to `Исходный порядок`, `Текущий номер`, or `Название` explicitly replaces the manual order with that sorted order and also invalidates the preview.

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
- Manual preview ordering: done for move up, move down, and move to position.
- Russian UI localization, action icons, and tooltips: done.
- Duplicate issues shown in UI: done.
- Apply button: enabled after a changed duplicate-free preview.
- Apply transaction: done with all-or-nothing rollback on failure.
- Apply confirmation: done.
- Preview export: done.
- Placeholder include/exclude control: done.
- Undo behavior: one Revit transaction should roll back Apply in one Undo step.
- Full read-only UI workflow before Apply: done.
- Installer: not finalized.
