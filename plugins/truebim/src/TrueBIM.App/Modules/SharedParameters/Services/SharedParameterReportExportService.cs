using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class SharedParameterReportExportService
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public SharedParameterReportPackage Save(
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionResult? deletion,
        string targetPath,
        IReadOnlyList<FamilyParameterUsageReport>? familyReports = null)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            throw new ArgumentException("Report path is required.", nameof(targetPath));
        }

        string jsonPath = Path.ChangeExtension(Path.GetFullPath(targetPath), ".json");
        string directory = Path.GetDirectoryName(jsonPath)
            ?? throw new InvalidOperationException("Report directory could not be resolved.");
        Directory.CreateDirectory(directory);

        string csvPath = Path.ChangeExtension(jsonPath, ".csv");
        string htmlPath = Path.ChangeExtension(jsonPath, ".html");
        string textPath = Path.ChangeExtension(jsonPath, ".txt");

        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(
                new SharedParameterReportDocument(analysis, familyReports ?? [], deletion),
                JsonOptions) + Environment.NewLine,
            Utf8WithoutBom);
        File.WriteAllText(csvPath, BuildCsv(analysis, deletion, familyReports), Utf8WithBom);
        File.WriteAllText(htmlPath, BuildHtml(analysis, deletion, familyReports), Utf8WithoutBom);
        File.WriteAllText(textPath, BuildText(analysis, deletion, familyReports), Utf8WithBom);
        return new SharedParameterReportPackage(jsonPath, csvPath, htmlPath, textPath);
    }

    public string BuildCsv(
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionResult? deletion = null,
        IReadOnlyList<FamilyParameterUsageReport>? familyReports = null)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        StringBuilder builder = new();
        AppendCsvRow(builder, ["Раздел", "Объект", "ElementId", "Статус", "Детали"]);
        AppendCsvRow(builder, [
            "Параметр",
            analysis.Parameter.Name,
            FormatInteger(analysis.Parameter.ParameterElementId),
            analysis.Parameter.BindingDisplay,
            $"{analysis.Parameter.Guid:D}; {analysis.Parameter.DataTypeName}; {analysis.Parameter.ParameterGroupName}"
        ]);

        foreach (ElementUsageAggregate aggregate in analysis.ElementAggregates)
        {
            AppendCsvRow(builder, [
                "Элементы",
                aggregate.CategoryName,
                string.Empty,
                $"Найдено {aggregate.ElementCount}",
                $"Параметр {aggregate.HasParameterCount}; заполнено {aggregate.FilledCount}; пусто {aggregate.EmptyCount}; read-only {aggregate.ReadOnlyCount}"
            ]);
        }

        foreach (ElementParameterUsage element in analysis.Elements)
        {
            AppendCsvRow(builder, [
                "Элемент",
                element.Name,
                FormatInteger(element.ElementId),
                element.HasValue ? "Заполнен" : "Пуст",
                $"{element.CategoryName}; family={element.FamilyName}; type={element.TypeName}; "
                + $"elementType={element.IsElementType}; readOnly={element.IsReadOnly}; value={element.DisplayValue}"
            ]);
        }

        foreach (ScheduleFieldUsage schedule in analysis.ScheduleFields)
        {
            AppendCsvRow(builder, [
                "Спецификации",
                schedule.ScheduleName,
                FormatInteger(schedule.ScheduleId),
                schedule.Confidence.ToString(),
                $"Поле {schedule.FieldName}; FieldId {schedule.FieldId}; hidden={schedule.IsHidden}; filter={schedule.UsedInFilter}; sort={schedule.UsedInSortOrGroup}"
            ]);
        }

        foreach (ViewFilterUsage filter in analysis.ViewFilters)
        {
            AppendCsvRow(builder, [
                "Фильтры видов",
                filter.FilterName,
                FormatInteger(filter.FilterId),
                filter.Confidence.ToString(),
                $"Целевых правил {filter.TargetRules.Count}; других правил {filter.OtherRules.Count}; видов {filter.AppliedViews.Count}; rebuild={filter.CanRebuildWithoutTarget}"
            ]);
        }

        foreach (GlobalParameterAssociationUsage association in analysis.GlobalParameterAssociations)
        {
            AppendCsvRow(builder, [
                "Глобальные параметры",
                association.GlobalParameterName,
                FormatInteger(association.ElementId),
                "Ассоциация",
                $"GlobalParameterId {association.GlobalParameterId}; element={association.ElementName}; "
                + $"category={association.ElementCategory}; formula={association.Formula}"
            ]);
        }

        foreach (ProjectFamilyPresence family in analysis.Families)
        {
            AppendCsvRow(builder, [
                "Семейства",
                family.FamilyName,
                FormatInteger(family.FamilyId),
                family.Status.ToString(),
                family.ErrorMessage ?? (family.ContainsParameter ? "Параметр найден" : "Параметр отсутствует")
            ]);
        }

        foreach (FamilyParameterUsageReport report in familyReports ?? [])
        {
            AppendFamilyCsvRows(builder, report);
        }

        foreach (DeletionBlocker blocker in analysis.Blockers)
        {
            AppendCsvRow(builder, [
                "Blocker",
                blocker.ObjectKind,
                blocker.ElementId.HasValue ? FormatInteger(blocker.ElementId.Value) : string.Empty,
                blocker.Confidence.ToString(),
                $"{blocker.Code}: {blocker.Message}"
            ]);
        }

        if (deletion is not null)
        {
            AppendCsvRow(builder, [
                "Удаление",
                deletion.Parameter.Name,
                FormatInteger(deletion.Parameter.ParameterElementId),
                deletion.Status.ToString(),
                deletion.Summary
            ]);
        }

        return builder.ToString();
    }

    public string BuildHtml(
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionResult? deletion = null,
        IReadOnlyList<FamilyParameterUsageReport>? familyReports = null)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        string Encode(string value) => WebUtility.HtmlEncode(value);
        StringBuilder builder = new();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>TrueBIM — Общие параметры</title>");
        builder.AppendLine("<style>body{font:14px Segoe UI,Arial,sans-serif;color:#1f2937;margin:32px;max-width:1200px}h1,h2{color:#111827}.meta{display:grid;grid-template-columns:220px 1fr;gap:6px 16px}.card{border:1px solid #d1d5db;border-radius:8px;padding:16px;margin:16px 0}.danger{border-color:#dc2626;background:#fef2f2}.warning{border-color:#d97706;background:#fffbeb}table{border-collapse:collapse;width:100%;margin-top:8px}th,td{border:1px solid #d1d5db;padding:7px;text-align:left;vertical-align:top}th{background:#f3f4f6}</style>");
        builder.AppendLine("</head><body>");
        builder.AppendLine("<h1>Общие параметры</h1>");
        builder.AppendLine("<div class=\"card meta\">");
        AppendHtmlMeta(builder, "Проект", analysis.Document.Title, Encode);
        AppendHtmlMeta(builder, "Revit", analysis.Document.RevitVersion, Encode);
        AppendHtmlMeta(builder, "Параметр", analysis.Parameter.Name, Encode);
        AppendHtmlMeta(builder, "GUID", analysis.Parameter.Guid.ToString("D"), Encode);
        AppendHtmlMeta(builder, "ElementId", FormatInteger(analysis.Parameter.ParameterElementId), Encode);
        AppendHtmlMeta(builder, "Привязка", analysis.Parameter.BindingDisplay, Encode);
        AppendHtmlMeta(builder, "Проанализировано", analysis.AnalyzedAt.ToLocalTime().ToString("G", CultureInfo.CurrentCulture), Encode);
        builder.AppendLine("</div>");

        AppendHtmlSummary(builder, analysis);
        AppendHtmlSchedules(builder, analysis, Encode);
        AppendHtmlViewFilters(builder, analysis, Encode);
        AppendHtmlFamilies(builder, analysis, Encode);
        AppendHtmlDeepFamilies(builder, familyReports ?? [], Encode);
        AppendHtmlIssues(builder, analysis, Encode);

        if (deletion is not null)
        {
            string cssClass = deletion.Status == DeletionStatus.Success ? "card" : "card warning";
            builder.AppendLine($"<section class=\"{cssClass}\"><h2>Удаление</h2>");
            builder.AppendLine($"<p><strong>Статус:</strong> {Encode(deletion.Status.ToString())}</p>");
            builder.AppendLine($"<p>{Encode(deletion.Summary)}</p>");
            builder.AppendLine($"<p>Удалено объектов: {deletion.DeletedElementIds.Count}; изменено: {deletion.ChangedElementIds.Count}; обработано семейств: {deletion.ProcessedFamilies.Count}.</p>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    public string BuildText(
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionResult? deletion = null,
        IReadOnlyList<FamilyParameterUsageReport>? familyReports = null)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        StringBuilder builder = new();
        builder.AppendLine("TRUEBIM SHARED PARAMETER INSPECTOR");
        builder.AppendLine($"Проект: {analysis.Document.Title}");
        builder.AppendLine($"Revit: {analysis.Document.RevitVersion}");
        builder.AppendLine($"Параметр: {analysis.Parameter.Name}");
        builder.AppendLine($"GUID: {analysis.Parameter.Guid:D}");
        builder.AppendLine($"ElementId: {analysis.Parameter.ParameterElementId}");
        builder.AppendLine($"Привязка: {analysis.Parameter.BindingDisplay}");
        builder.AppendLine($"Проанализировано: {analysis.AnalyzedAt:O}");
        builder.AppendLine($"Категорий: {analysis.Parameter.Categories.Count}");
        builder.AppendLine($"Элементов: {analysis.Elements.Count}; заполнено: {analysis.FilledValueCount}; пусто: {analysis.EmptyValueCount}");
        builder.AppendLine($"Спецификаций: {analysis.ScheduleFields.Select(field => field.ScheduleId).Distinct().Count()}");
        builder.AppendLine($"Фильтров видов: {analysis.ViewFilters.Count}");
        builder.AppendLine($"Семейств с параметром: {analysis.FamilyCountWithParameter}");
        builder.AppendLine($"Blockers: {analysis.Blockers.Count}; предупреждений: {analysis.Warnings.Count}; ошибок: {analysis.Errors.Count}");

        IReadOnlyList<FamilyParameterUsageReport> deepReports = familyReports ?? [];
        if (deepReports.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("ГЛУБОКИЙ АНАЛИЗ СЕМЕЙСТВ");
            foreach (FamilyParameterUsageReport report in deepReports)
            {
                builder.AppendLine(
                    $"- {report.Family.Name}: найден={report.ParameterFound}; типы={report.TypeValues.Count}; "
                    + $"формулы={report.Formulas.Count}; размеры={report.Dimensions.Count}; "
                    + $"ассоциации={report.Associations.Count}; вложенные={report.NestedFamilies.Count}; "
                    + $"annotation={report.Annotations.Count}; blockers={report.DeletionBlockers.Count}; "
                    + $"ошибки={report.Errors.Count}");
                foreach (DeletionBlocker blocker in report.DeletionBlockers)
                {
                    builder.AppendLine($"  BLOCKER [{blocker.Code}] {blocker.Message}");
                }
            }
        }

        if (analysis.Blockers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("BLOCKERS");
            foreach (DeletionBlocker blocker in analysis.Blockers)
            {
                builder.AppendLine($"- [{blocker.Code}] {blocker.Message}");
            }
        }

        if (deletion is not null)
        {
            builder.AppendLine();
            builder.AppendLine("УДАЛЕНИЕ");
            builder.AppendLine($"Статус: {deletion.Status}");
            builder.AppendLine($"Режим: {deletion.Mode}");
            builder.AppendLine(deletion.Summary);
        }

        return builder.ToString();
    }

    private static void AppendHtmlSummary(StringBuilder builder, SharedParameterProjectAnalysis analysis)
    {
        builder.AppendLine("<section class=\"card\"><h2>Сводка</h2><div class=\"meta\">");
        builder.AppendLine($"<span>Элементы</span><strong>{analysis.Elements.Count}</strong>");
        builder.AppendLine($"<span>Заполнено / пусто</span><strong>{analysis.FilledValueCount} / {analysis.EmptyValueCount}</strong>");
        builder.AppendLine($"<span>Поля спецификаций</span><strong>{analysis.ScheduleFields.Count}</strong>");
        builder.AppendLine($"<span>Фильтры видов</span><strong>{analysis.ViewFilters.Count}</strong>");
        builder.AppendLine($"<span>Семейства с параметром</span><strong>{analysis.FamilyCountWithParameter}</strong>");
        builder.AppendLine("</div></section>");
    }

    private static void AppendHtmlSchedules(
        StringBuilder builder,
        SharedParameterProjectAnalysis analysis,
        Func<string, string> encode)
    {
        builder.AppendLine("<section class=\"card\"><h2>Спецификации</h2><table><thead><tr><th>Спецификация</th><th>Поле</th><th>Hidden</th><th>Filter</th><th>Sort/group</th><th>Confidence</th></tr></thead><tbody>");
        foreach (ScheduleFieldUsage field in analysis.ScheduleFields)
        {
            builder.AppendLine($"<tr><td>{encode(field.ScheduleName)}</td><td>{encode(field.FieldName)}</td><td>{field.IsHidden}</td><td>{field.UsedInFilter}</td><td>{field.UsedInSortOrGroup}</td><td>{field.Confidence}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");
    }

    private static void AppendHtmlViewFilters(
        StringBuilder builder,
        SharedParameterProjectAnalysis analysis,
        Func<string, string> encode)
    {
        builder.AppendLine("<section class=\"card\"><h2>Фильтры видов</h2><table><thead><tr><th>Фильтр</th><th>Target rules</th><th>Other rules</th><th>Виды/шаблоны</th><th>Перестроение</th></tr></thead><tbody>");
        foreach (ViewFilterUsage filter in analysis.ViewFilters)
        {
            builder.AppendLine($"<tr><td>{encode(filter.FilterName)}</td><td>{filter.TargetRules.Count}</td><td>{filter.OtherRules.Count}</td><td>{filter.AppliedViews.Count}</td><td>{filter.CanRebuildWithoutTarget}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");
    }

    private static void AppendHtmlFamilies(
        StringBuilder builder,
        SharedParameterProjectAnalysis analysis,
        Func<string, string> encode)
    {
        builder.AppendLine("<section class=\"card\"><h2>Семейства</h2><table><thead><tr><th>Семейство</th><th>Категория</th><th>Статус</th><th>Результат</th></tr></thead><tbody>");
        foreach (ProjectFamilyPresence family in analysis.Families)
        {
            builder.AppendLine($"<tr><td>{encode(family.FamilyName)}</td><td>{encode(family.CategoryName)}</td><td>{family.Status}</td><td>{(family.ContainsParameter ? "Найден" : "Не найден")}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");
    }

    private static void AppendHtmlDeepFamilies(
        StringBuilder builder,
        IReadOnlyList<FamilyParameterUsageReport> reports,
        Func<string, string> encode)
    {
        if (reports.Count == 0)
        {
            return;
        }

        builder.AppendLine("<section class=\"card\"><h2>Глубокий анализ семейств</h2><table><thead><tr><th>Семейство</th><th>Параметр</th><th>Типы</th><th>Формулы</th><th>Размеры</th><th>Ассоциации</th><th>Вложенные</th><th>Blockers / errors</th></tr></thead><tbody>");
        foreach (FamilyParameterUsageReport report in reports)
        {
            string parameterKind = report.Parameter is null
                ? "Не найден"
                : report.Parameter.IsInstance ? "Экземпляр" : "Тип";
            builder.AppendLine(
                $"<tr><td>{encode(report.Family.Name)}<br><small>{encode(report.Family.Path)}</small></td>"
                + $"<td>{encode(parameterKind)}</td><td>{report.TypeValues.Count}</td>"
                + $"<td>{report.Formulas.Count}</td><td>{report.Dimensions.Count}</td>"
                + $"<td>{report.Associations.Count}</td><td>{report.NestedFamilies.Count}</td>"
                + $"<td>{report.DeletionBlockers.Count} / {report.Errors.Count}</td></tr>");
        }

        builder.AppendLine("</tbody></table></section>");
    }

    private static void AppendHtmlIssues(
        StringBuilder builder,
        SharedParameterProjectAnalysis analysis,
        Func<string, string> encode)
    {
        if (analysis.Blockers.Count == 0 && analysis.Warnings.Count == 0 && analysis.Errors.Count == 0)
        {
            return;
        }

        builder.AppendLine("<section class=\"card danger\"><h2>Ограничения и ошибки</h2><ul>");
        foreach (DeletionBlocker blocker in analysis.Blockers)
        {
            builder.AppendLine($"<li><strong>{encode(blocker.Code)}</strong>: {encode(blocker.Message)}</li>");
        }

        foreach (DeletionWarning warning in analysis.Warnings)
        {
            builder.AppendLine($"<li><strong>{encode(warning.Code)}</strong>: {encode(warning.Message)}</li>");
        }

        foreach (AnalysisError error in analysis.Errors)
        {
            builder.AppendLine($"<li><strong>{encode(error.Phase)}</strong>: {encode(error.Message)}</li>");
        }

        builder.AppendLine("</ul></section>");
    }

    private static void AppendHtmlMeta(
        StringBuilder builder,
        string label,
        string value,
        Func<string, string> encode)
    {
        builder.AppendLine($"<span>{encode(label)}</span><strong>{encode(value)}</strong>");
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string?> values)
    {
        builder.AppendLine(string.Join(";", values.Select(EscapeCsv)));
    }

    private static void AppendFamilyCsvRows(
        StringBuilder builder,
        FamilyParameterUsageReport report)
    {
        AppendCsvRow(builder, [
            "Семейство: сводка",
            report.Family.Name,
            report.Family.ProjectFamilyId.HasValue
                ? FormatInteger(report.Family.ProjectFamilyId.Value)
                : string.Empty,
            report.ParameterFound ? "Параметр найден" : "Параметр не найден",
            $"source={report.Family.SourceKind}; path={report.Family.Path}; "
            + $"instance={report.Parameter?.IsInstance}; dataType={report.Parameter?.DataTypeName}; "
            + $"group={report.Parameter?.ParameterGroupName}"
        ]);
        foreach (FamilyTypeValueUsage value in report.TypeValues)
        {
            AppendCsvRow(builder, [
                "Семейство: значение типа",
                $"{report.Family.Name} / {value.TypeName}",
                string.Empty,
                value.HasValue ? "Заполнено" : "Пусто",
                $"formula={value.IsFormulaDriven}; display={value.DisplayValue}; internal={value.InternalValue}"
            ]);
        }

        foreach (FormulaUsage formula in report.Formulas)
        {
            AppendCsvRow(builder, [
                "Семейство: формула",
                $"{report.Family.Name} / {formula.ParameterName}",
                string.Empty,
                formula.Confidence.ToString(),
                $"targetFormula={formula.IsTargetFormula}; {formula.Formula}"
            ]);
        }

        foreach (DimensionUsage dimension in report.Dimensions)
        {
            AppendCsvRow(builder, [
                "Семейство: размер",
                $"{report.Family.Name} / {dimension.ViewName}",
                FormatInteger(dimension.DimensionId),
                dimension.Confidence.ToString(),
                $"segments={dimension.SegmentCount}; reporting={dimension.IsReporting}; value={dimension.Value}"
            ]);
        }

        foreach (AssociatedParameterUsage association in report.Associations)
        {
            AppendCsvRow(builder, [
                "Семейство: ассоциация",
                $"{report.Family.Name} / {association.ParameterName}",
                FormatInteger(association.ElementId),
                association.Confidence.ToString(),
                $"{association.Direction}; element={association.ElementName}; "
                + $"category={association.CategoryName}; type={association.ElementTypeName}"
            ]);
        }

        foreach (NestedFamilyUsage nested in report.NestedFamilies)
        {
            AppendCsvRow(builder, [
                "Семейство: вложенное",
                $"{report.Family.Name} / {nested.FamilyName}",
                FormatInteger(nested.ElementId),
                nested.Confidence.ToString(),
                $"type={nested.TypeName}; parameter={nested.ParameterName}; "
                + $"association={nested.AssociationKind}; depth={nested.Depth}"
            ]);
        }

        foreach (AnnotationUsage annotation in report.Annotations)
        {
            AppendCsvRow(builder, [
                "Семейство: аннотация",
                report.Family.Name,
                string.Empty,
                annotation.Confidence.ToString(),
                annotation.Message
            ]);
        }

        foreach (DeletionBlocker blocker in report.DeletionBlockers)
        {
            AppendCsvRow(builder, [
                "Семейство: blocker",
                report.Family.Name,
                blocker.ElementId.HasValue ? FormatInteger(blocker.ElementId.Value) : string.Empty,
                blocker.Confidence.ToString(),
                $"{blocker.Code}: {blocker.Message}"
            ]);
        }

        foreach (AnalysisError error in report.Errors)
        {
            AppendCsvRow(builder, [
                "Семейство: ошибка",
                report.Family.Name,
                string.Empty,
                error.Phase,
                $"{error.Message}; source={error.Source}"
            ]);
        }
    }

    private static string EscapeCsv(string? value)
    {
        string safe = value ?? string.Empty;
        if (safe.IndexOfAny([';', '"', '\r', '\n']) < 0)
        {
            return safe;
        }

        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static string FormatInteger(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record SharedParameterReportDocument(
        SharedParameterProjectAnalysis Analysis,
        IReadOnlyList<FamilyParameterUsageReport> FamilyReports,
        SharedParameterDeletionResult? Deletion);
}
