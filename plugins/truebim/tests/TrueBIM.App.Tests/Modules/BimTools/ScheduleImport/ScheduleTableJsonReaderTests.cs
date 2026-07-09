using System.Text;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleTableJsonReaderTests
{
    [Fact]
    public void ReadTables_LoadsRowsAndBuildsCells()
    {
        string path = Path.Combine(Path.GetTempPath(), $"schedule-import-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              "tables": [
                {
                  "sourceFilePath": "pipes.pdf",
                  "pageNumber": 2,
                  "columns": ["Поз.", "Диаметр", "Длина, м"],
                  "rows": [
                    ["1", "DN25", "12.5"],
                    ["2", "DN20", "7.2"]
                  ],
                  "confidence": 0.82,
                  "warnings": ["Проверьте единицы"]
                }
              ]
            }
            """,
            Encoding.UTF8);

        try
        {
            var tables = new ScheduleTableJsonReader().ReadTables(path);

            var table = Assert.Single(tables);
            Assert.Equal("pipes.pdf", table.SourceFilePath);
            Assert.Equal(2, table.PageNumber);
            Assert.Equal(2, table.RowCount);
            Assert.Equal(3, table.ColumnCount);
            Assert.Equal(6, table.Cells.Count);
            Assert.Contains(table.Warnings, warning => warning.Contains("единицы", StringComparison.CurrentCultureIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadTables_SupportsSingleTableContract()
    {
        string path = Path.Combine(Path.GetTempPath(), $"schedule-import-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            path,
            """
            {
              "columns": ["A", "B"],
              "cells": [
                { "rowIndex": 0, "columnIndex": 0, "text": "Header", "isHeader": true },
                { "rowIndex": 1, "columnIndex": 1, "text": "Value" }
              ]
            }
            """,
            Encoding.UTF8);

        try
        {
            var table = Assert.Single(new ScheduleTableJsonReader().ReadTables(path));

            Assert.Equal(2, table.RowCount);
            Assert.Equal(2, table.ColumnCount);
            Assert.Equal("Value", table.Rows[1].Values[1]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
