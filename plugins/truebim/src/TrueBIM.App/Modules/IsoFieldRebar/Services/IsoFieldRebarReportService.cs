using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarReportService
{
    public const string SchemaVersion = "1.1";
    public const string DefaultFileNamePrefix = "isofield-rebar-report";
    private const double SquareFeetToSquareMeters = 0.09290304;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private readonly IsoFieldRebarReviewService reviewService = new();

    public IsoFieldRebarReport Build(IsoFieldRebarReportRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Preview.EngineeringSettings is null || !request.Preview.IsEngineeringPreview)
        {
            throw new InvalidOperationException(
                "Отчёт доступен только для рассчитанной инженерной раскладки плиты.");
        }

        IsoFieldRebarReportSourceFile[] sourceFiles = request.SourceFiles
            .Select(BuildSourceFile)
            .OrderBy(file => file.LayerRole)
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        IReadOnlyList<IsoFieldRebarReviewRow> reviewRows = reviewService.BuildRows(
            request.Preview,
            request.Recognition,
            request.ChangePlan);
        IsoFieldRebarReportZone[] zones = request.Preview.Items
            .Select(item => BuildZone(item, reviewRows))
            .OrderBy(zone => zone.LayerRole)
            .ThenBy(zone => zone.ZoneId, StringComparer.Ordinal)
            .ToArray();
        IsoFieldRebarReportLayerTotal[] layerTotals = zones
            .Where(zone => zone.LayerRole.HasValue)
            .GroupBy(zone => zone.LayerRole!.Value)
            .OrderBy(group => group.Key)
            .Select(BuildLayerTotal)
            .ToArray();

        List<string> diagnostics = new();
        diagnostics.AddRange(request.Preview.Diagnostics);
        diagnostics.AddRange(request.Recognition.Diagnostics);
        diagnostics.AddRange(request.SlabBinding?.Diagnostics ?? Array.Empty<string>());
        diagnostics.AddRange(request.ChangePlan?.Diagnostics ?? Array.Empty<string>());
        diagnostics.AddRange(request.QualityResult?.Issues.Select(issue => issue.Message)
            ?? Array.Empty<string>());
        diagnostics.AddRange(sourceFiles
            .Where(file => !string.Equals(file.Status, "Готов", StringComparison.Ordinal))
            .Select(file => $"Источник {file.FileName}: {file.Status}."));

        return new IsoFieldRebarReport(
            SchemaVersion,
            request.GeneratedAtUtc ?? DateTimeOffset.UtcNow,
            request.DocumentTitle,
            request.DocumentKey,
            new IsoFieldRebarReportHost(
                request.Host.ElementId,
                request.Host.HostKind,
                request.Host.Name),
            new IsoFieldRebarReportProvenance(
                request.SourceKind,
                request.RecognitionRunner,
                request.RecognitionRunnerVersion,
                request.PluginVersion,
                request.SourceSetManifestPath,
                sourceFiles),
            BuildBinding(request),
            request.Preview.EngineeringSettings,
            BuildRuleProfileSha256(request.Preview),
            zones,
            layerTotals,
            BuildQualityCheck(request),
            BuildChangeSummary(request.ChangePlan),
            diagnostics.Distinct(StringComparer.Ordinal).ToArray());
    }

    public IsoFieldRebarReportSaveResult Save(
        IsoFieldRebarReport report,
        string jsonPath)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            throw new ArgumentException("Report path is required.", nameof(jsonPath));
        }

        string fullJsonPath = Path.ChangeExtension(Path.GetFullPath(jsonPath), ".json");
        string fullCsvPath = Path.ChangeExtension(fullJsonPath, ".csv");
        string directory = Path.GetDirectoryName(fullJsonPath)
            ?? throw new InvalidOperationException("Report directory could not be resolved.");
        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine;
        File.WriteAllText(fullJsonPath, json, Utf8WithoutBom);
        File.WriteAllText(fullCsvPath, FormatCsv(report), Utf8WithBom);
        return new IsoFieldRebarReportSaveResult(fullJsonPath, fullCsvPath);
    }

    public string FormatCsv(IsoFieldRebarReport report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        StringBuilder builder = new();
        AppendCsvRow(builder, ["МЕТАДАННЫЕ"]);
        AppendCsvRow(builder, ["Ключ", "Значение"]);
        AppendCsvRow(builder, ["schemaVersion", report.SchemaVersion]);
        AppendCsvRow(builder, ["generatedAtUtc", report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)]);
        AppendCsvRow(builder, ["documentTitle", report.DocumentTitle]);
        AppendCsvRow(builder, ["documentKey", report.DocumentKey]);
        AppendCsvRow(builder, ["hostElementId", FormatInteger(report.Host.ElementId)]);
        AppendCsvRow(builder, ["hostKind", report.Host.HostKind]);
        AppendCsvRow(builder, ["hostName", report.Host.HostName]);
        AppendCsvRow(builder, ["sourceKind", report.Provenance.SourceKind]);
        AppendCsvRow(builder, ["recognitionRunner", report.Provenance.RecognitionRunner]);
        AppendCsvRow(builder, ["recognitionRunnerVersion", report.Provenance.RecognitionRunnerVersion]);
        AppendCsvRow(builder, ["pluginVersion", report.Provenance.PluginVersion]);
        AppendCsvRow(builder, ["sourceSetManifestPath", report.Provenance.SourceSetManifestPath]);
        AppendCsvRow(builder, ["ruleProfileSha256", report.RuleProfileSha256]);
        AppendCsvRow(builder, ["bindingKind", report.Binding.Kind]);
        AppendCsvRow(builder, ["millimetersPerPixel", FormatDouble(report.Binding.MillimetersPerPixel)]);
        AppendCsvRow(builder, ["rotationDegrees", FormatDouble(report.Binding.RotationDegrees)]);
        AppendCsvRow(builder, ["mirrorImageY", FormatBoolean(report.Binding.MirrorImageY)]);
        AppendCsvRow(builder, ["reinforcementMode", report.EngineeringSettings.Mode.ToString()]);
        AppendCsvRow(builder, ["concreteCoverMillimeters", FormatDouble(report.EngineeringSettings.ConcreteCoverMillimeters)]);
        AppendCsvRow(builder, ["boundaryOffsetMillimeters", FormatDouble(report.EngineeringSettings.BoundaryOffsetMillimeters)]);
        AppendCsvRow(builder, ["minimumBarLengthMillimeters", FormatDouble(report.EngineeringSettings.MinimumBarLengthMillimeters)]);
        AppendCsvRow(builder, ["qualityEvaluated", FormatBoolean(report.QualityCheck.Evaluated)]);
        AppendCsvRow(builder, ["qualityBlockingErrorCount", FormatInteger(report.QualityCheck.BlockingErrorCount)]);
        AppendCsvRow(builder, ["qualityWarningCount", FormatInteger(report.QualityCheck.WarningCount)]);
        AppendCsvRow(builder, ["qualityWarningsAccepted", FormatBoolean(report.QualityCheck.WarningsAccepted)]);
        AppendCsvRow(builder, ["qualityFingerprint", report.QualityCheck.Fingerprint]);
        AppendCsvRow(builder, ["compared", FormatBoolean(report.ChangeSummary.Compared)]);
        AppendCsvRow(builder, Array.Empty<string?>());

        AppendCsvRow(builder, ["ИСТОЧНИКИ"]);
        AppendCsvRow(builder,
        [
            "Файл", "Полный путь", "Слой", "Ширина, px", "Высота, px",
            "Размер, байт", "Изменён UTC", "SHA-256", "Статус"
        ]);
        foreach (IsoFieldRebarReportSourceFile file in report.Provenance.SourceFiles)
        {
            AppendCsvRow(builder,
            [
                file.FileName,
                file.FilePath,
                file.LayerRole?.ToString(),
                FormatNullableInteger(file.PixelWidth),
                FormatNullableInteger(file.PixelHeight),
                FormatNullableInteger(file.SizeBytes),
                file.LastWriteTimeUtc?.ToString("O", CultureInfo.InvariantCulture),
                file.Sha256,
                file.Status
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["ЗОНЫ"]);
        AppendCsvRow(builder,
        [
            "ID зоны", "Имя", "Исходные ID", "Слой", "Грань", "Направление",
            "Режим", "Включена", "Ручное правило", "Объединена", "Армирование",
            "Требуется, см2/м", "Принято, см2/м", "Площадь, м2", "Стержни",
            "Confidence", "Статус", "Добавить", "Обновить", "Удалить",
            "Без изменений", "Компоненты", "Диагностика"
        ]);
        foreach (IsoFieldRebarReportZone zone in report.Zones)
        {
            AppendCsvRow(builder,
            [
                zone.ZoneId,
                zone.ZoneName,
                string.Join(",", zone.SourceZoneIds),
                zone.LayerRole?.ToString(),
                zone.Face?.ToString(),
                zone.Direction,
                zone.ReinforcementMode?.ToString(),
                FormatBoolean(zone.IsIncluded),
                FormatBoolean(zone.IsManuallyOverridden),
                FormatBoolean(zone.IsMerged),
                zone.ReinforcementLabel,
                FormatNullableDouble(zone.RequiredAreaSquareCentimetersPerMeter),
                FormatNullableDouble(zone.ProvidedAreaSquareCentimetersPerMeter),
                FormatDouble(zone.GeometryAreaSquareMeters),
                FormatInteger(zone.EstimatedBarCount),
                FormatNullableDouble(zone.Confidence),
                zone.ReviewStatus.ToString(),
                FormatInteger(zone.AddCount),
                FormatInteger(zone.UpdateCount),
                FormatInteger(zone.DeleteCount),
                FormatInteger(zone.UnchangedCount),
                string.Join(" + ", zone.Components.Select(component =>
                    $"d{FormatDouble(component.DiameterMillimeters)}s{FormatDouble(component.SpacingMillimeters)}")),
                string.Join(" | ", zone.Diagnostics)
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["ИТОГИ ПО СЛОЯМ"]);
        AppendCsvRow(builder,
        [
            "Слой", "Зон", "Включено", "Исключено", "Объединено", "Площадь включённых зон, м2",
            "Стержни", "Мин. требуется, см2/м", "Макс. требуется, см2/м",
            "Мин. принято, см2/м", "Добавить", "Обновить", "Удалить",
            "Без изменений", "Диагностик"
        ]);
        foreach (IsoFieldRebarReportLayerTotal total in report.LayerTotals)
        {
            AppendCsvRow(builder,
            [
                total.LayerRole.ToString(),
                FormatInteger(total.ZoneCount),
                FormatInteger(total.IncludedZoneCount),
                FormatInteger(total.ExcludedZoneCount),
                FormatInteger(total.MergedZoneCount),
                FormatDouble(total.IncludedGeometryAreaSquareMeters),
                FormatInteger(total.EstimatedBarCount),
                FormatNullableDouble(total.MinimumRequiredAreaSquareCentimetersPerMeter),
                FormatNullableDouble(total.MaximumRequiredAreaSquareCentimetersPerMeter),
                FormatNullableDouble(total.MinimumProvidedAreaSquareCentimetersPerMeter),
                FormatInteger(total.AddCount),
                FormatInteger(total.UpdateCount),
                FormatInteger(total.DeleteCount),
                FormatInteger(total.UnchangedCount),
                FormatInteger(total.DiagnosticCount)
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["КОНТРОЛЬ КАЧЕСТВА"]);
        AppendCsvRow(builder,
        [
            "Тип", "Код", "Слой", "Зоны", "Измерено", "Предел", "Сообщение"
        ]);
        foreach (IsoFieldRebarReportQualityIssue issue in report.QualityCheck.Issues)
        {
            AppendCsvRow(builder,
            [
                issue.Severity.ToString(),
                issue.Code.ToString(),
                issue.LayerRole?.ToString(),
                string.Join(",", issue.ZoneIds),
                FormatNullableDouble(issue.MeasuredValue),
                FormatNullableDouble(issue.LimitValue),
                issue.Message
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["ПОКРЫТИЕ СЛОЁВ"]);
        AppendCsvRow(builder,
        [
            "Слой", "Включено зон", "Покрыто, м2", "Площадь host, м2", "Доля покрытия"
        ]);
        foreach (IsoFieldRebarReportQualityCoverage coverage in report.QualityCheck.LayerCoverage)
        {
            AppendCsvRow(builder,
            [
                coverage.LayerRole.ToString(),
                FormatInteger(coverage.IncludedZoneCount),
                FormatDouble(coverage.CoveredAreaSquareMeters),
                FormatDouble(coverage.HostAreaSquareMeters),
                FormatDouble(coverage.CoverageRatio)
            ]);
        }

        if (report.Diagnostics.Count > 0)
        {
            AppendCsvRow(builder, Array.Empty<string?>());
            AppendCsvRow(builder, ["ДИАГНОСТИКА"]);
            AppendCsvRow(builder, ["Сообщение"]);
            foreach (string diagnostic in report.Diagnostics)
            {
                AppendCsvRow(builder, [diagnostic]);
            }
        }

        return builder.ToString();
    }

    private static IsoFieldRebarReportSourceFile BuildSourceFile(
        IsoFieldRebarReportSourceInput input)
    {
        string displayPath = input.FilePath ?? string.Empty;
        string fileName = Path.GetFileName(displayPath);
        try
        {
            string fullPath = Path.GetFullPath(displayPath);
            fileName = Path.GetFileName(fullPath);
            if (!File.Exists(fullPath))
            {
                return new IsoFieldRebarReportSourceFile(
                    fileName,
                    fullPath,
                    input.LayerRole,
                    input.PixelWidth,
                    input.PixelHeight,
                    null,
                    null,
                    null,
                    "Файл отсутствует");
            }

            FileInfo file = new(fullPath);
            return new IsoFieldRebarReportSourceFile(
                file.Name,
                fullPath,
                input.LayerRole,
                input.PixelWidth,
                input.PixelHeight,
                file.Length,
                file.LastWriteTimeUtc,
                CalculateFileSha256(fullPath),
                "Готов");
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return new IsoFieldRebarReportSourceFile(
                fileName,
                displayPath,
                input.LayerRole,
                input.PixelWidth,
                input.PixelHeight,
                null,
                null,
                null,
                $"Ошибка чтения: {exception.Message}");
        }
    }

    private static IsoFieldRebarReportQualityCheck BuildQualityCheck(
        IsoFieldRebarReportRequest request)
    {
        if (request.QualityResult is null)
        {
            return new IsoFieldRebarReportQualityCheck(
                false,
                0,
                0,
                false,
                null,
                Array.Empty<IsoFieldRebarReportQualityCoverage>(),
                Array.Empty<IsoFieldRebarReportQualityIssue>());
        }

        return new IsoFieldRebarReportQualityCheck(
            true,
            request.QualityResult.BlockingIssues.Count,
            request.QualityResult.Warnings.Count,
            request.QualityWarningsAccepted,
            request.QualityResult.Fingerprint,
            request.QualityResult.LayerCoverage
                .OrderBy(coverage => coverage.LayerRole)
                .Select(coverage => new IsoFieldRebarReportQualityCoverage(
                    coverage.LayerRole,
                    coverage.IncludedZoneCount,
                    coverage.CoveredAreaSquareMeters,
                    coverage.HostAreaSquareMeters,
                    coverage.CoverageRatio))
                .ToArray(),
            request.QualityResult.Issues
                .Select(issue => new IsoFieldRebarReportQualityIssue(
                    issue.Code,
                    issue.Severity,
                    issue.Message,
                    issue.LayerRole,
                    issue.EffectiveZoneIds,
                    issue.MeasuredValue,
                    issue.LimitValue))
                .ToArray());
    }

    private static IsoFieldRebarReportBinding BuildBinding(
        IsoFieldRebarReportRequest request)
    {
        if (request.SlabBinding is null)
        {
            return new IsoFieldRebarReportBinding(
                "LegacyCalibration",
                request.Calibration.ImageAnchor.X,
                request.Calibration.ImageAnchor.Y,
                request.Calibration.RevitAnchorXFeet,
                request.Calibration.RevitAnchorYFeet,
                request.Calibration.MillimetersPerPixel,
                0,
                request.Calibration.InvertImageY,
                null,
                null,
                null,
                request.BindingProfile?.SavedAtUtc);
        }

        IsoFieldPlanarTransform transform = request.SlabBinding.Transform;
        return new IsoFieldRebarReportBinding(
            "SlabThreePoint",
            transform.ImageAnchor.X,
            transform.ImageAnchor.Y,
            transform.HostAnchorFeet.X,
            transform.HostAnchorFeet.Y,
            transform.MillimetersPerPixel,
            transform.RotationDegrees,
            transform.MirrorImageY,
            request.SlabBinding.RetainedAreaRatio,
            request.SlabBinding.ThirdPointDeviationMillimeters,
            request.SlabBinding.ThirdPointToleranceMillimeters,
            request.BindingProfile?.SavedAtUtc);
    }

    private static IsoFieldRebarReportZone BuildZone(
        RebarRulePreviewItem item,
        IReadOnlyList<IsoFieldRebarReviewRow> reviewRows)
    {
        IsoFieldRebarReviewRow? review = reviewRows.FirstOrDefault(row =>
            string.Equals(row.ZoneId, item.ZoneId, StringComparison.Ordinal));
        return new IsoFieldRebarReportZone(
            item.ZoneId,
            item.ZoneName,
            item.EffectiveSourceZoneIds,
            item.Rule.LayerRole,
            item.Rule.Face,
            item.Rule.PlacementDirection,
            item.Rule.ReinforcementMode,
            item.IsIncluded,
            item.IsManuallyOverridden,
            item.IsMerged,
            item.Rule.ReinforcementLabel ?? item.Rule.BarTypeName,
            item.Rule.RequiredAreaSquareCentimetersPerMeter,
            item.Rule.ProvidedAreaSquareCentimetersPerMeter,
            item.EffectiveRegions.Sum(region => region.AreaSquareFeet) * SquareFeetToSquareMeters,
            item.EstimatedBarCount,
            review?.Confidence,
            review?.Status ?? IsoFieldRebarReviewStatus.NotCompared,
            review?.AddCount ?? 0,
            review?.UpdateCount ?? 0,
            review?.DeleteCount ?? 0,
            review?.UnchangedCount ?? 0,
            item.Rule.EffectiveComponents.Select(component =>
                new IsoFieldRebarReportComponent(
                    component.DiameterMillimeters,
                    component.SpacingMillimeters,
                    component.CombinationIndex,
                    component.CombinationCount,
                    component.AreaSquareCentimetersPerMeter)).ToArray(),
            item.Diagnostics);
    }

    private static IsoFieldRebarReportLayerTotal BuildLayerTotal(
        IGrouping<IsoFieldLayerRole, IsoFieldRebarReportZone> group)
    {
        IsoFieldRebarReportZone[] zones = group.ToArray();
        IsoFieldRebarReportZone[] included = zones.Where(zone => zone.IsIncluded).ToArray();
        return new IsoFieldRebarReportLayerTotal(
            group.Key,
            zones.Length,
            included.Length,
            zones.Length - included.Length,
            zones.Count(zone => zone.IsMerged),
            included.Sum(zone => zone.GeometryAreaSquareMeters),
            included.Sum(zone => zone.EstimatedBarCount),
            Minimum(included.Select(zone => zone.RequiredAreaSquareCentimetersPerMeter)),
            Maximum(included.Select(zone => zone.RequiredAreaSquareCentimetersPerMeter)),
            Minimum(included.Select(zone => zone.ProvidedAreaSquareCentimetersPerMeter)),
            zones.Sum(zone => zone.AddCount),
            zones.Sum(zone => zone.UpdateCount),
            zones.Sum(zone => zone.DeleteCount),
            zones.Sum(zone => zone.UnchangedCount),
            zones.Sum(zone => zone.Diagnostics.Count));
    }

    private static IsoFieldRebarReportChangeSummary BuildChangeSummary(
        IsoFieldRebarChangePlan? changePlan)
    {
        return changePlan is null
            ? new IsoFieldRebarReportChangeSummary(false, false, 0, 0, 0, 0)
            : new IsoFieldRebarReportChangeSummary(
                true,
                changePlan.CanApply,
                changePlan.AddCount,
                changePlan.UpdateCount,
                changePlan.DeleteCount,
                changePlan.UnchangedCount);
    }

    private static string BuildRuleProfileSha256(RebarRulePreviewResult preview)
    {
        IsoFieldEngineeringSettings settings = preview.EngineeringSettings
            ?? throw new InvalidOperationException("Engineering settings are missing.");
        StringBuilder canonical = new();
        canonical.Append(settings.Mode).Append('|')
            .Append(FormatDouble(settings.ConcreteCoverMillimeters)).Append('|')
            .Append(FormatDouble(settings.BoundaryOffsetMillimeters)).Append('|')
            .Append(FormatDouble(settings.MinimumBarLengthMillimeters)).Append('|')
            .Append(settings.MaximumBarCount.ToString(CultureInfo.InvariantCulture));
        foreach (RebarRulePreviewItem item in preview.Items.OrderBy(item => item.ZoneId, StringComparer.Ordinal))
        {
            canonical.AppendLine();
            canonical.Append(item.ZoneId).Append('|')
                .Append(item.IsIncluded).Append('|')
                .Append(item.IsManuallyOverridden).Append('|')
                .Append(string.Join(",", item.EffectiveSourceZoneIds.OrderBy(id => id, StringComparer.Ordinal))).Append('|')
                .Append(item.Rule.LayerRole).Append('|')
                .Append(item.Rule.Face).Append('|')
                .Append(item.Rule.PlacementDirection).Append('|')
                .Append(item.Rule.ReinforcementMode).Append('|')
                .Append(FormatNullableDouble(item.Rule.RequiredAreaSquareCentimetersPerMeter)).Append('|')
                .Append(FormatNullableDouble(item.Rule.ProvidedAreaSquareCentimetersPerMeter));
            foreach (IsoFieldRebarComponent component in item.Rule.EffectiveComponents)
            {
                canonical.Append('|')
                    .Append(FormatDouble(component.DiameterMillimeters)).Append('@')
                    .Append(FormatDouble(component.SpacingMillimeters)).Append('@')
                    .Append(component.CombinationIndex.ToString(CultureInfo.InvariantCulture)).Append('@')
                    .Append(component.CombinationCount.ToString(CultureInfo.InvariantCulture));
            }
        }

        return CalculateSha256(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private static string CalculateFileSha256(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using SHA256 sha256 = SHA256.Create();
        return FormatSha256(sha256.ComputeHash(stream));
    }

    private static string CalculateSha256(byte[] value)
    {
        using SHA256 sha256 = SHA256.Create();
        return FormatSha256(sha256.ComputeHash(value));
    }

    private static string FormatSha256(IEnumerable<byte> hash)
    {
        return string.Concat(hash.Select(value =>
            value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static double? Minimum(IEnumerable<double?> values)
    {
        double[] actual = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return actual.Length == 0 ? null : actual.Min();
    }

    private static double? Maximum(IEnumerable<double?> values)
    {
        double[] actual = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return actual.Length == 0 ? null : actual.Max();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void AppendCsvRow(StringBuilder builder, IReadOnlyList<string?> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(';');
            }

            builder.Append(EscapeCsv(values[index] ?? string.Empty));
        }

        builder.AppendLine();
    }

    private static string EscapeCsv(string value)
    {
        return value.IndexOfAny([';', '"', '\r', '\n']) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatBoolean(bool value) => value ? "Да" : "Нет";

    private static string FormatDouble(double value) =>
        value.ToString("0.########", CultureInfo.InvariantCulture);

    private static string? FormatNullableDouble(double? value) =>
        value.HasValue ? FormatDouble(value.Value) : null;

    private static string FormatInteger(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string? FormatNullableInteger(long? value) =>
        value.HasValue ? FormatInteger(value.Value) : null;
}
