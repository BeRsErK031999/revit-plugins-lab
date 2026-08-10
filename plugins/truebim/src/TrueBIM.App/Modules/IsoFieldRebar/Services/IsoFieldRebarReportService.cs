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
    public const string SchemaVersion = "1.2";
    public const string DefaultFileNamePrefix = "isofield-rebar-report";
    private const double SquareFeetToSquareMeters = 0.09290304;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
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
                "Отчёт доступен после расчёта раскладки для выбранной стены или плиты.");
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
            BuildApplicationSummary(request),
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
            throw new ArgumentException("Не указан путь для сохранения отчёта.", nameof(jsonPath));
        }

        string fullJsonPath = Path.ChangeExtension(Path.GetFullPath(jsonPath), ".json");
        string fullCsvPath = Path.ChangeExtension(fullJsonPath, ".csv");
        string directory = Path.GetDirectoryName(fullJsonPath)
            ?? throw new InvalidOperationException("Не удалось определить папку для сохранения отчёта.");
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
        AppendCsvRow(builder, ["ОБЩИЕ СВЕДЕНИЯ"]);
        AppendCsvRow(builder, ["Показатель", "Значение"]);
        AppendCsvRow(builder, ["Версия отчёта", report.SchemaVersion]);
        AppendCsvRow(builder, ["Создан", FormatDateTime(report.GeneratedAtUtc)]);
        AppendCsvRow(builder, ["Документ", report.DocumentTitle]);
        AppendCsvRow(builder, ["Файл проекта", report.DocumentKey]);
        AppendCsvRow(builder, ["Номер конструкции", FormatInteger(report.Host.ElementId)]);
        AppendCsvRow(builder, ["Тип конструкции", FormatHostKind(report.Host.HostKind)]);
        AppendCsvRow(builder, ["Название конструкции", report.Host.HostName]);
        AppendCsvRow(builder, ["Источник зон", FormatSourceKind(report.Provenance.SourceKind)]);
        AppendCsvRow(builder, ["Способ поиска зон", report.Provenance.RecognitionRunner]);
        AppendCsvRow(builder, ["Версия средства поиска", FormatUnknown(report.Provenance.RecognitionRunnerVersion)]);
        AppendCsvRow(builder, ["Версия TrueBIM", FormatUnknown(report.Provenance.PluginVersion)]);
        AppendCsvRow(builder, ["Сохранённый комплект карт", report.Provenance.SourceSetManifestPath]);
        AppendCsvRow(builder, ["Контрольные данные правил", report.RuleProfileSha256]);
        AppendCsvRow(builder, ["Способ привязки", FormatBindingKind(report.Binding.Kind)]);
        AppendCsvRow(builder, ["Масштаб, мм на точку изображения", FormatDouble(report.Binding.MillimetersPerPixel)]);
        AppendCsvRow(builder, ["Поворот, градусы", FormatDouble(report.Binding.RotationDegrees)]);
        AppendCsvRow(builder, ["Вертикаль карты перевёрнута", FormatBoolean(report.Binding.MirrorImageY)]);
        AppendCsvRow(builder, ["Режим армирования", FormatReinforcementMode(report.EngineeringSettings.Mode)]);
        AppendCsvRow(builder, ["Отступ арматуры от поверхности, мм", FormatDouble(report.EngineeringSettings.ConcreteCoverMillimeters)]);
        AppendCsvRow(builder, ["Отступ от границ и отверстий, мм", FormatDouble(report.EngineeringSettings.BoundaryOffsetMillimeters)]);
        AppendCsvRow(builder, ["Минимальная длина стержня, мм", FormatDouble(report.EngineeringSettings.MinimumBarLengthMillimeters)]);
        AppendCsvRow(builder, ["Проверка выполнена", FormatBoolean(report.QualityCheck.Evaluated)]);
        AppendCsvRow(builder, ["Ошибок", FormatInteger(report.QualityCheck.BlockingErrorCount)]);
        AppendCsvRow(builder, ["Предупреждений", FormatInteger(report.QualityCheck.WarningCount)]);
        AppendCsvRow(builder, ["Предупреждения приняты", FormatBoolean(report.QualityCheck.WarningsAccepted)]);
        AppendCsvRow(builder, ["Контрольные данные проверки", report.QualityCheck.Fingerprint]);
        AppendCsvRow(builder, ["Сравнение с моделью выполнено", FormatBoolean(report.ChangeSummary.Compared)]);
        AppendCsvRow(builder, ["Изменения применены", FormatBoolean(report.ApplicationSummary.Applied)]);
        AppendCsvRow(builder, ["Изменения завершены", FormatNullableDateTime(report.ApplicationSummary.CompletedAtUtc)]);
        AppendCsvRow(builder, ["Добавлено", FormatInteger(report.ApplicationSummary.AddedCount)]);
        AppendCsvRow(builder, ["Изменено", FormatInteger(report.ApplicationSummary.UpdatedCount)]);
        AppendCsvRow(builder, ["Удалено", FormatInteger(report.ApplicationSummary.DeletedCount)]);
        AppendCsvRow(builder, ["Без изменений", FormatInteger(report.ApplicationSummary.UnchangedCount)]);
        AppendCsvRow(builder, ["Номера созданных элементов", string.Join(",", report.ApplicationSummary.CreatedElementIds)]);
        AppendCsvRow(builder, ["Номера удалённых элементов", string.Join(",", report.ApplicationSummary.DeletedElementIds)]);
        AppendCsvRow(builder, Array.Empty<string?>());

        AppendCsvRow(builder, ["ИСТОЧНИКИ"]);
        AppendCsvRow(builder,
        [
            "Файл", "Полный путь", "Карта", "Ширина изображения", "Высота изображения",
            "Размер файла, байт", "Время изменения", "Контрольная сумма", "Состояние"
        ]);
        foreach (IsoFieldRebarReportSourceFile file in report.Provenance.SourceFiles)
        {
            AppendCsvRow(builder,
            [
                file.FileName,
                file.FilePath,
                file.LayerRole.HasValue ? FormatLayer(file.LayerRole.Value) : null,
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
            "Номер зоны", "Название", "Исходные номера", "Карта", "Сторона", "Направление",
            "Режим", "Учитывается", "Настроена вручную", "Объединена", "Армирование",
            "Требуется, см2/м", "Принято, см2/м", "Площадь, м2", "Стержни",
            "Распознано", "Состояние", "Добавить", "Изменить", "Удалить",
            "Без изменений", "Наборы стержней", "Замечания"
        ]);
        foreach (IsoFieldRebarReportZone zone in report.Zones)
        {
            AppendCsvRow(builder,
            [
                zone.ZoneId,
                zone.ZoneName,
                string.Join(",", zone.SourceZoneIds),
                zone.LayerRole.HasValue ? FormatLayer(zone.LayerRole.Value) : null,
                FormatFace(report.Host.HostKind, zone.Face),
                FormatDirection(zone.Direction),
                FormatNullableReinforcementMode(zone.ReinforcementMode),
                FormatBoolean(zone.IsIncluded),
                FormatBoolean(zone.IsManuallyOverridden),
                FormatBoolean(zone.IsMerged),
                FormatReinforcementLabel(zone.ReinforcementLabel),
                FormatNullableDouble(zone.RequiredAreaSquareCentimetersPerMeter),
                FormatNullableDouble(zone.ProvidedAreaSquareCentimetersPerMeter),
                FormatDouble(zone.GeometryAreaSquareMeters),
                FormatInteger(zone.EstimatedBarCount),
                FormatPercentage(zone.Confidence),
                FormatReviewStatus(zone.ReviewStatus),
                FormatInteger(zone.AddCount),
                FormatInteger(zone.UpdateCount),
                FormatInteger(zone.DeleteCount),
                FormatInteger(zone.UnchangedCount),
                string.Join(" + ", zone.Components.Select(component =>
                    $"Ø{FormatDouble(component.DiameterMillimeters)}, шаг {FormatDouble(component.SpacingMillimeters)} мм")),
                string.Join(" | ", zone.Diagnostics)
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["ИТОГИ ПО КАРТАМ"]);
        AppendCsvRow(builder,
        [
            "Карта", "Зон", "Включено", "Исключено", "Объединено", "Площадь включённых зон, м2",
            "Стержни", "Мин. требуется, см2/м", "Макс. требуется, см2/м",
            "Мин. принято, см2/м", "Добавить", "Обновить", "Удалить",
            "Без изменений", "Замечаний"
        ]);
        foreach (IsoFieldRebarReportLayerTotal total in report.LayerTotals)
        {
            AppendCsvRow(builder,
            [
                FormatLayer(total.LayerRole),
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
        AppendCsvRow(builder, ["ПРОВЕРКА ЗОН И АРМАТУРЫ"]);
        AppendCsvRow(builder,
        [
            "Важность", "Проверка", "Карта", "Зоны", "Измерено", "Предел", "Сообщение"
        ]);
        foreach (IsoFieldRebarReportQualityIssue issue in report.QualityCheck.Issues)
        {
            AppendCsvRow(builder,
            [
                FormatQualitySeverity(issue.Severity),
                FormatQualityCode(issue.Code),
                issue.LayerRole.HasValue ? FormatLayer(issue.LayerRole.Value) : null,
                string.Join(",", issue.ZoneIds),
                FormatNullableDouble(issue.MeasuredValue),
                FormatNullableDouble(issue.LimitValue),
                issue.Message
            ]);
        }

        AppendCsvRow(builder, Array.Empty<string?>());
        AppendCsvRow(builder, ["ПОКРЫТИЕ ПО КАРТАМ"]);
        AppendCsvRow(builder,
        [
            "Карта", "Учитывается зон", "Покрыто, м²", "Площадь конструкции, м²", "Доля покрытия"
        ]);
        foreach (IsoFieldRebarReportQualityCoverage coverage in report.QualityCheck.LayerCoverage)
        {
            AppendCsvRow(builder,
            [
                FormatLayer(coverage.LayerRole),
                FormatInteger(coverage.IncludedZoneCount),
                FormatDouble(coverage.CoveredAreaSquareMeters),
                FormatDouble(coverage.HostAreaSquareMeters),
                FormatPercentage(coverage.CoverageRatio)
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

    private static IsoFieldRebarReportApplicationSummary BuildApplicationSummary(
        IsoFieldRebarReportRequest request)
    {
        if (request.ApplicationResult is null)
        {
            return new IsoFieldRebarReportApplicationSummary(
                false,
                null,
                0,
                0,
                0,
                0,
                Array.Empty<long>(),
                Array.Empty<long>());
        }

        return new IsoFieldRebarReportApplicationSummary(
            true,
            request.ApplicationCompletedAtUtc,
            request.ApplicationResult.AddedCount,
            request.ApplicationResult.UpdatedCount,
            request.ApplicationResult.DeletedCount,
            request.ApplicationResult.UnchangedCount,
            request.ApplicationResult.CreatedElementIds.OrderBy(id => id).ToArray(),
            request.ApplicationResult.DeletedElementIds.OrderBy(id => id).ToArray());
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
            request.Host.IsWall ? "WallThreePoint" : "SlabThreePoint",
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
            ?? throw new InvalidOperationException("Не найдены настройки рассчитанной раскладки. Повторите расчёт.");
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

    private static string FormatHostKind(string hostKind)
    {
        return hostKind switch
        {
            "Wall" => "Стена",
            "Slab" => "Плита",
            _ => "Неизвестная конструкция"
        };
    }

    private static string FormatSourceKind(string sourceKind)
    {
        return sourceKind switch
        {
            "RecognitionJson" => "Готовые зоны",
            "ImageSourceSet" => "Четыре карты изополей",
            _ => "Неизвестный источник"
        };
    }

    private static string FormatBindingKind(string bindingKind)
    {
        return bindingKind switch
        {
            "WallThreePoint" => "Стена, по трём точкам",
            "SlabThreePoint" => "Плита, по трём точкам",
            "LegacyCalibration" => "Начало и масштаб изображения",
            _ => "Неизвестный способ"
        };
    }

    private static string FormatReinforcementMode(IsoFieldReinforcementMode mode)
    {
        return mode switch
        {
            IsoFieldReinforcementMode.AdditionalOverBase => "Только усиление поверх базовой сетки",
            IsoFieldReinforcementMode.FullCombination => "Полное сочетание внутри зон",
            _ => "Неизвестный режим"
        };
    }

    private static string? FormatNullableReinforcementMode(IsoFieldReinforcementMode? mode) =>
        mode.HasValue ? FormatReinforcementMode(mode.Value) : null;

    private static string? FormatFace(string hostKind, IsoFieldRebarFace? face)
    {
        if (!face.HasValue || face == IsoFieldRebarFace.Unconfirmed)
        {
            return null;
        }

        if (string.Equals(hostKind, "Wall", StringComparison.Ordinal))
        {
            return face == IsoFieldRebarFace.Bottom ? "Внутренняя" : "Наружная";
        }

        return face == IsoFieldRebarFace.Bottom ? "Низ" : "Верх";
    }

    private static string FormatDirection(string direction)
    {
        return direction switch
        {
            "X" => "X",
            "Y" => "Y",
            "AlongHost" => "Вдоль конструкции",
            _ => "Определено автоматически"
        };
    }

    private static string FormatReviewStatus(IsoFieldRebarReviewStatus status)
    {
        return status switch
        {
            IsoFieldRebarReviewStatus.NotCompared => "Не сравнено",
            IsoFieldRebarReviewStatus.Add => "Добавить",
            IsoFieldRebarReviewStatus.Update => "Изменить",
            IsoFieldRebarReviewStatus.Delete => "Удалить",
            IsoFieldRebarReviewStatus.Unchanged => "Без изменений",
            IsoFieldRebarReviewStatus.Mixed => "Несколько видов изменений",
            IsoFieldRebarReviewStatus.Invalid => "Ошибка",
            IsoFieldRebarReviewStatus.Excluded => "Исключена",
            _ => "Неизвестно"
        };
    }

    private static string FormatQualitySeverity(IsoFieldRebarQualitySeverity severity) =>
        severity == IsoFieldRebarQualitySeverity.Blocking ? "Ошибка" : "Предупреждение";

    private static string FormatQualityCode(IsoFieldRebarQualityCode code)
    {
        return code switch
        {
            IsoFieldRebarQualityCode.GeometryAnalysisFailed => "Не удалось проверить раскладку",
            IsoFieldRebarQualityCode.RequiredAreaDeficit => "Недостаточно арматуры",
            IsoFieldRebarQualityCode.SameLayerOverlap => "Пересечение зон одной карты",
            IsoFieldRebarQualityCode.FinalGeometryOutsideHost => "Зона выходит за границу конструкции",
            IsoFieldRebarQualityCode.MissingLayerCoverage => "Карта не покрывает конструкцию",
            IsoFieldRebarQualityCode.PartialLayerCoverage => "Карта покрывает конструкцию не полностью",
            IsoFieldRebarQualityCode.ZoneClippedByHost => "Зона обрезана по границе конструкции",
            IsoFieldRebarQualityCode.SourceZoneOutsideHost => "Зона за границами конструкции",
            _ => "Неизвестная проверка"
        };
    }

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", RussianCulture);

    private static string? FormatNullableDateTime(DateTimeOffset? value) =>
        value.HasValue ? FormatDateTime(value.Value) : null;

    private static string? FormatPercentage(double? value) =>
        value.HasValue
            ? (value.Value * 100).ToString("0.#", RussianCulture) + "%"
            : null;

    private static string FormatPercentage(double value) =>
        (value * 100).ToString("0.#", RussianCulture) + "%";

    private static string FormatReinforcementLabel(string? label) =>
        new IsoFieldReinforcementCombinationService().FormatForDisplay(label);

    private static string FormatUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase)
            ? "Неизвестно"
            : value;

    private static string FormatLayer(IsoFieldLayerRole role)
    {
        return role switch
        {
            IsoFieldLayerRole.As1X => "X, карта 1",
            IsoFieldLayerRole.As2X => "X, карта 2",
            IsoFieldLayerRole.As3Y => "Y, карта 1",
            IsoFieldLayerRole.As4Y => "Y, карта 2",
            _ => "неизвестная карта"
        };
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
