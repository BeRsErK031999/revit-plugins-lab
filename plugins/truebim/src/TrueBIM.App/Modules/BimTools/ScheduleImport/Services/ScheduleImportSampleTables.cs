using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportSampleTables
{
    public static ParsedTable CreatePipeSchedule(string sourceFilePath)
    {
        string[] columns =
        [
            "Поз.",
            "Наименование",
            "Диаметр",
            "Длина, м",
            "Количество"
        ];
        string[][] rows =
        [
            columns,
            ["1", "Труба стальная водогазопроводная", "DN25", "12.5", "4"],
            ["2", "Отвод 90 градусов", "DN25", "", "8"],
            ["3", "Кран шаровой", "DN20", "", "2"],
            ["4", "Труба стальная водогазопроводная", "DN20", "7.2", "2"]
        ];

        List<ParsedRow> parsedRows = [];
        List<ParsedCell> cells = [];
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            parsedRows.Add(new ParsedRow(rowIndex, rows[rowIndex]));
            for (int columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                cells.Add(new ParsedCell(
                    rowIndex,
                    columnIndex,
                    1,
                    1,
                    rows[rowIndex][columnIndex],
                    null,
                    rowIndex == 0 ? 0.98 : 0.9,
                    rowIndex == 0));
            }
        }

        return new ParsedTable(
            sourceFilePath,
            1,
            parsedRows,
            columns,
            cells,
            0.86,
            [
                "Используется тестовая JSON-таблица. Реальный PDF parser будет подключен следующим этапом.",
                "PDF используется для выбора структуры полей; строки новой ViewSchedule формируются реальными элементами Revit после сопоставления и dry-run."
            ]);
    }
}
