using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Сохраняет выпуск в отдельных параметрах настоящих помещений, сохраняя выбор строк Revit.</summary>
internal sealed class FinishRoomVersionService
{
    public static string? Validate(ViewSchedule schedule, bool textSnapshot)
    {
        string? issue = FinishScheduleSnapshotService.Validate(schedule);
        if (issue is not null) return issue;
        try
        {
            using VersionData data = Capture(schedule, textSnapshot);
            foreach (ElementId id in data.Values.Keys)
                if (RevitFinishParameterAccess.IsOwnedByOtherUser(schedule.Document, id))
                    return $"Помещение {RevitElementIds.GetValue(id)} прежней ведомости «{schedule.Name}» занято другим пользователем.";
            return null;
        }
        catch (Exception exception)
        {
            return $"Не удалось сохранить связь ведомости «{schedule.Name}» с помещениями: {exception.Message} "
                + "Исходная ведомость не изменена.";
        }
    }

    // The caller owns the transaction and the encompassing atomic write group.
    public void Preserve(ViewSchedule schedule, bool textSnapshot, IReadOnlyList<long>? roomIds = null)
    {
        using VersionData data = Capture(schedule, textSnapshot);
        Document document = schedule.Document;
        if (roomIds is not null)
        {
            HashSet<long> allowed = new(roomIds);
            foreach (ElementId id in data.Values.Keys.Where(id => !allowed.Contains(RevitElementIds.GetValue(id))).ToArray())
                data.Values.Remove(id);
            if (!allowed.SetEquals(data.Values.Keys.Select(RevitElementIds.GetValue)))
                throw new InvalidOperationException("Состав помещений выпуска не совпадает с областью расчёта.");
        }
        foreach (ElementId id in data.Values.Keys)
            if (RevitFinishParameterAccess.IsOwnedByOtherUser(document, id))
                throw new InvalidOperationException($"Помещение {RevitElementIds.GetValue(id)} занято другим пользователем.");

        using FinishScheduleSnapshotService.TableSnapshot header =
            FinishScheduleSnapshotService.CaptureHeader(schedule, data.HeaderRows);
        Guid[] parameters = CreateParameters(schedule, data.Columns);
        document.Regenerate();
        foreach (KeyValuePair<ElementId, string[]> pair in data.Values)
        {
            Element room = document.GetElement(pair.Key);
            for (int column = 0; column < parameters.Length; column++)
            {
                Parameter parameter = room.get_Parameter(parameters[column])
                    ?? throw new InvalidOperationException("Служебный параметр выпуска не привязался к помещению.");
                string value = column < pair.Value.Length ? pair.Value[column] : "1";
                if (parameter.IsReadOnly || !parameter.Set(value))
                    throw new InvalidOperationException("Revit не сохранил значение выпуска в помещении.");
            }
        }

        ScheduleDefinition definition = schedule.Definition;
        if (roomIds is not null)
        {
            ScheduleField sourceMembership = definition.AddField(ScheduleFieldType.Instance,
                SharedParameterElement.Lookup(document, parameters[parameters.Length - 1]).Id);
            sourceMembership.IsHidden = true;
            definition.AddFilter(new ScheduleFilter(sourceMembership.FieldId, ScheduleFilterType.Equal, "1"));
            document.Regenerate();
            schedule.RefreshData();
            data.Rows = ReadRows(schedule, SectionType.Body, data.ShowHeaders ? 1 : 0);
        }
        definition.ClearFilters();
        definition.ClearSortGroupFields();
        definition.ClearFields();
        for (int index = 0; index < data.Columns.Length; index++)
        {
            Column source = data.Columns[index];
            SharedParameterElement parameter = SharedParameterElement.Lookup(document, parameters[index]);
            ScheduleField field = definition.AddField(ScheduleFieldType.Instance, parameter.Id);
            field.ColumnHeading = source.Heading;
            field.SheetColumnWidth = source.Width;
            field.HorizontalAlignment = source.Alignment;
            field.SetStyle(source.Style);
        }
        ScheduleField membership = definition.AddField(ScheduleFieldType.Instance,
            SharedParameterElement.Lookup(document, parameters[parameters.Length - 1]).Id);
        membership.IsHidden = true;
        definition.AddFilter(new ScheduleFilter(membership.FieldId, ScheduleFilterType.Equal, "1"));
        definition.IsItemized = data.IsItemized;
        foreach (SortColumn sort in data.SortColumns)
        {
            using ScheduleSortGroupField field = new(definition.GetFieldId(sort.Index), sort.Order)
            {
                ShowBlankLine = sort.BlankLine, ShowHeader = sort.Header, ShowFooter = sort.Footer
            };
            if (sort.Footer)
            {
                field.ShowFooterCount = sort.FooterCount;
                field.ShowFooterTitle = sort.FooterTitle;
            }
            definition.AddSortGroupField(field);
        }
        definition.ShowTitle = data.HeaderRows > 0;
        definition.ShowHeaders = data.ShowHeaders;
        document.Regenerate();
        schedule.RefreshData();
        if (data.HeaderRows > 0) FinishScheduleSnapshotService.WriteSnapshot(schedule, header);
        document.Regenerate();
        schedule.RefreshData();

        HashSet<ElementId> members = new(new FilteredElementCollector(document, schedule.Id)
            .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElementIds());
        if (!members.SetEquals(data.Values.Keys))
            throw new InvalidOperationException("Состав помещений ведомости изменился при сохранении выпуска.");
        string[] actual = ReadRows(schedule, SectionType.Body, data.ShowHeaders ? 1 : 0)
            .Select(RowKey).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        string[] expected = data.Rows.Select(RowKey).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Значения строк изменились при восстановлении связи с помещениями.");
    }

    private static VersionData Capture(ViewSchedule schedule, bool textSnapshot)
    {
        ScheduleDefinition definition = schedule.Definition;
        ScheduleField[] fields = definition.GetFieldOrder().Select(definition.GetField)
            .Where(field => !field.IsHidden).ToArray();
        if (fields.Length == 0 || fields.Any(field => !field.HasSchedulableField))
            throw new InvalidOperationException("В ведомости есть неподдерживаемые вычисляемые поля или нет столбцов.");
        VersionData result = new();
        try
        {
            result.Columns = fields.Select(field => new Column(field.ColumnHeading, field.SheetColumnWidth,
                field.HorizontalAlignment, field.GetStyle())).ToArray();
            using TableData table = schedule.GetTableData();
            using TableSectionData header = table.GetSectionData(SectionType.Header);
            result.HeaderRows = definition.ShowTitle ? header.NumberOfRows : 0;
            result.ShowHeaders = !textSnapshot && definition.ShowHeaders;
            result.IsItemized = !textSnapshot && definition.IsItemized;
            if (textSnapshot)
            {
                Room[] rooms = new FilteredElementCollector(schedule.Document).OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().Cast<Room>().ToArray();
                Dictionary<long, string> numbers = rooms.ToDictionary(room => RevitElementIds.GetValue(room.Id), room => room.Number);
                List<string[]> rows = [];
                bool started = false;
                for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
                {
                    string[] values = Enumerable.Range(header.FirstColumnNumber, fields.Length)
                        .Select(column => schedule.GetCellText(SectionType.Header, row, column)).ToArray();
                    if (values.All(string.IsNullOrWhiteSpace)) continue;
                    bool hasMergedCells = Enumerable.Range(header.FirstColumnNumber, fields.Length).Any(column =>
                    {
                        TableMergedCell merged = header.GetMergedCell(row, column);
                        return merged.Top != merged.Bottom || merged.Left != merged.Right;
                    });
                    if (!started && (hasMergedCells || values.SequenceEqual(fields.Select(field => field.ColumnHeading)))) continue;
                    IReadOnlyList<long> ids = FinishLegacyRoomRowResolver.Resolve(values[0], numbers);
                    if (!started) result.HeaderRows = row - header.FirstRowNumber;
                    started = true;
                    foreach (long id in ids)
                    {
                        ElementId elementId = RevitElementIds.Create(id);
                        if (result.Values.ContainsKey(elementId))
                            throw new InvalidOperationException("Помещение встречается в нескольких архивных строках.");
                        result.Values.Add(elementId, values);
                    }
                    rows.Add(values);
                }
                if (!started)
                    throw new InvalidOperationException("Текстовый архив не содержит однозначных номеров помещений. "
                        + "Для восстановления архива с именами, изменёнными или повторяющимися номерами нужна исходная модель.");
                result.Rows = rows;
                result.SortColumns = [new SortColumn(0, ScheduleSortOrder.Ascending, false, false, false, false, false)];
            }
            else
            {
                foreach (Element room in new FilteredElementCollector(schedule.Document, schedule.Id)
                    .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType())
                {
                    string[] values = fields.Select(field => ReadValue(room, field.ParameterId)).ToArray();
                    if (!values.All(string.IsNullOrWhiteSpace)) result.Values.Add(room.Id, values);
                }
                result.Rows = ReadRows(schedule, SectionType.Body, definition.ShowHeaders ? 1 : 0);
                List<SortColumn> sorting = [];
                foreach (ScheduleSortGroupField sort in definition.GetSortGroupFields())
                {
                    int index = Array.FindIndex(fields, field => field.FieldId.Equals(sort.FieldId));
                    if (index < 0) throw new InvalidOperationException("Архивация сортировки по скрытому полю не поддерживается.");
                    sorting.Add(new SortColumn(index, sort.SortOrder, sort.ShowBlankLine, sort.ShowHeader,
                        sort.ShowFooter, sort.ShowFooterCount, sort.ShowFooterTitle));
                }
                result.SortColumns = sorting;
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static string ReadValue(Element room, ElementId parameterId)
    {
        Parameter? parameter = room.Parameters.Cast<Parameter>().FirstOrDefault(item => item.Id == parameterId);
        if (parameter is null || parameter.StorageType != StorageType.String)
            throw new InvalidOperationException("Все отображаемые поля выпуска должны быть текстовыми параметрами помещений.");
        return parameter.AsString() ?? string.Empty;
    }

    private static List<string[]> ReadRows(ViewSchedule schedule, SectionType type, int skip)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData section = table.GetSectionData(type);
        List<string[]> rows = [];
        for (int row = section.FirstRowNumber + skip; row <= section.LastRowNumber; row++)
        {
            string[] values = Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns)
                .Select(column => schedule.GetCellText(type, row, column)).ToArray();
            if (values.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(values);
        }
        return rows;
    }

    private static string RowKey(IEnumerable<string> cells) => string.Join("\u001f", cells
        .Select(value => value.Replace("\r\n", "\n").Replace('\r', '\n')));

    private static Guid[] CreateParameters(ViewSchedule schedule, Column[] columns)
    {
        Document document = schedule.Document;
        Autodesk.Revit.ApplicationServices.Application application = document.Application;
        string originalFile = application.SharedParametersFilename;
        string temporaryFile = Path.Combine(Path.GetTempPath(), "TrueBIM-finish-version-" + Guid.NewGuid().ToString("N") + ".txt");
        Guid[] guids = Enumerable.Range(0, columns.Length + 1).Select(_ => Guid.NewGuid()).ToArray();
        try
        {
            File.WriteAllText(temporaryFile, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n", Encoding.UTF8);
            application.SharedParametersFilename = temporaryFile;
            DefinitionGroup group = application.OpenSharedParameterFile().Groups.Create("TrueBIM — Выпуски отделки");
            CategorySet categories = application.Create.NewCategorySet();
            categories.Insert(Category.GetCategory(document, BuiltInCategory.OST_Rooms));
            for (int index = 0; index < guids.Length; index++)
            {
                string label = index < columns.Length ? columns[index].Heading.Replace('\n', ' ').Replace('\r', ' ') : "Состав выпуска";
                string name = $"TrueBIM • Отделка {RevitElementIds.GetValue(schedule.Id)} • {index + 1} {label}";
#if REVIT2022_OR_GREATER
                using ExternalDefinitionCreationOptions options = new(name, SpecTypeId.String.Text);
#else
                using ExternalDefinitionCreationOptions options = new(name, ParameterType.Text);
#endif
                options.GUID = guids[index];
                options.Visible = true;
                options.UserModifiable = false;
                options.Description = $"Значение выпуска «{schedule.Name}». Связь с реальным помещением сохраняется.";
                Definition parameter = group.Definitions.Create(options);
                InstanceBinding binding = application.Create.NewInstanceBinding(categories);
#if REVIT2022_OR_GREATER
                bool inserted = document.ParameterBindings.Insert(parameter, binding, GroupTypeId.Data);
#else
                bool inserted = document.ParameterBindings.Insert(parameter, binding, BuiltInParameterGroup.PG_DATA);
#endif
                if (!inserted) throw new InvalidOperationException("Revit не создал параметр отдельного выпуска отделки.");
            }
            return guids;
        }
        finally
        {
            application.SharedParametersFilename = originalFile;
            if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
        }
    }

    private sealed record Column(string Heading, double Width, ScheduleHorizontalAlignment Alignment, TableCellStyle Style);
    private sealed record SortColumn(int Index, ScheduleSortOrder Order, bool BlankLine, bool Header, bool Footer, bool FooterCount, bool FooterTitle);
    private sealed class VersionData : IDisposable
    {
        public Column[] Columns { get; set; } = [];
        public Dictionary<ElementId, string[]> Values { get; } = [];
        public IReadOnlyList<string[]> Rows { get; set; } = [];
        public IReadOnlyList<SortColumn> SortColumns { get; set; } = [];
        public int HeaderRows { get; set; }
        public bool ShowHeaders { get; set; }
        public bool IsItemized { get; set; }
        public void Dispose() { foreach (Column column in Columns) column.Style.Dispose(); }
    }
}
