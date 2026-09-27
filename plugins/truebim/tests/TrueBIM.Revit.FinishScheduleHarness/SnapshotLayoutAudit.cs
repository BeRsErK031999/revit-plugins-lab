using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Revit;

namespace TrueBIM.Revit.FinishScheduleHarness;

/// <summary>Сравнивает высоту статических ячеек с текстом Revit на временных листах.</summary>
public static class SnapshotLayoutAudit
{
    public static string Run(Document document, string reportPath)
    {
        ViewSchedule source = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>().Single(view => view.Name == "Помещения • Ведомость отделки помещений 12357756");
        List<object> measurements = [];
        using TransactionGroup rollback = new(document, "TrueBIM: проверка высоты текста с откатом");
        rollback.Start();
        try
        {
            foreach (double factor in new[] { 0.0, 1.5, 2.0 })
            {
                ViewSheet sheet;
                using (Transaction transaction = new(document, "Временная копия ведомости"))
                {
                    transaction.Start();
                    sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
                    sheet.Name = factor == 0 ? "Высота по метрикам шрифта" : "Высота строк x" + factor.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    ViewSchedule copy = (ViewSchedule)document.GetElement(source.Duplicate(ViewDuplicateOption.Duplicate));
                    using TableData table = copy.GetTableData();
                    using TableSectionData header = table.GetSectionData(SectionType.Header);
                    for (int row = 3; row <= header.LastRowNumber; row++)
                    {
                        double height = 0;
                        for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
                        {
                            using TableCellStyle style = header.GetTableCellStyle(row, column);
                            height = Math.Max(height, FinishSnapshotTextMeasurement.RequiredHeight(
                                source.GetCellText(SectionType.Header, row, column), style, header.GetColumnWidth(column)));
                        }
                        header.SetRowHeight(row, factor == 0
                            ? Math.Max(header.GetRowHeight(row), height)
                            : header.GetRowHeight(row) * factor);
                    }
                    ScheduleSheetInstance.Create(document, sheet.Id, copy.Id, XYZ.Zero);
                    transaction.Commit();
                }
                Export(document, sheet, reportPath);
            }

            using (Transaction transaction = new(document, "Измерение нативного текста"))
            {
                transaction.Start();
                ViewSheet sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
                using TableData table = source.GetTableData();
                using TableSectionData header = table.GetSectionData(SectionType.Header);
                TextNoteType textType = (TextNoteType)((TextNoteType)document.GetElement(source.BodyTextTypeId)).Duplicate("TrueBIM temporary text measurement");
                foreach (int row in new[] { 3, 5, 29 })
                {
                    foreach (int column in new[] { 1, 3, 4, 5, 6 })
                    {
                        string text = source.GetCellText(SectionType.Header, row, column);
                        using TableCellStyle style = header.GetTableCellStyle(row, column);
                        textType.get_Parameter(BuiltInParameter.TEXT_FONT).Set(style.FontName);
                        textType.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(style.TextSize / (12 * 96));
                        textType.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD).Set(style.IsFontBold ? 1 : 0);
                        textType.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC).Set(style.IsFontItalic ? 1 : 0);
                        textType.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE).Set(1.0);
                        TextNote note = TextNote.Create(document, sheet.Id, XYZ.Zero,
                            header.GetColumnWidth(column) - 2.0 / 304.8, text.Replace("\r\n", "\n"), textType.Id);
                        document.Regenerate();
                        measurements.Add(new { Row = row, Column = column, Text = text,
                            CellHeight = header.GetRowHeight(row) * 304.8,
                            TextHeight = note.Height * 304.8, TextWidth = note.Width * 304.8,
                            Font = style.FontName, Size = style.TextSize });
                        document.Delete(note.Id);
                    }
                }
                transaction.RollBack();
            }
        }
        finally { rollback.RollBack(); }
        string result = new JavaScriptSerializer().Serialize(new { TemporaryChangesRolledBack = true, Measurements = measurements });
        File.WriteAllText(reportPath, result);
        return result;
    }

    private static void Export(Document document, ViewSheet sheet, string reportPath)
    {
        using ImageExportOptions options = new()
        {
            FilePath = reportPath, ExportRange = ExportRange.SetOfViews,
            HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_150, ZoomType = ZoomFitType.FitToPage, PixelSize = 9000
        };
        options.SetViewsAndSheets(new List<ElementId> { sheet.Id });
        document.ExportImage(options);
    }
}
