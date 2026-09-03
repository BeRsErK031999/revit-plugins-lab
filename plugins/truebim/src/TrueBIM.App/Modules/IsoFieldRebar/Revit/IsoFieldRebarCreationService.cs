using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldRebarCreationService
{
    private const string StraightArrayFamilyCode = "000";
    private const string StraightArrayLengthParameter = "A";
    private const string ArrayWidthParameter = "Зона • Ширина";
    private const string DiameterParameter = "• Деталь • Арматура. Диаметр";
    private const string SpacingParameter = "• Деталь • Шаг элементов";
    private const string ConcreteClassParameter = "Деталь • Арматура. Класс бетона";
    private const string WallHostKind = "Wall";
    private const string SlabHostKind = "Slab";
    private const double MillimetersPerFoot = 304.8;
    private const double MinimumTestLengthFeet = 0.5;
    private const double MinimumDirectionLengthFeet = 1e-9;
    private const double GeometryComparisonToleranceFeet = 1e-5;
    private readonly SlabRebarPlacementService slabPlacementService = new();
    private readonly WallRebarPlacementService wallPlacementService = new();
    private readonly IsoFieldRebarChangePlanService changePlanService = new();
    private readonly IsoFieldArrayRebarGroupingService arrayGroupingService = new();
    private readonly IsoFieldHostSupportService hostSupportService = new();
    private readonly ITrueBimLogger logger;

    public IsoFieldRebarCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRebarCreationResult CreateTestRebar(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview,
        IsoFieldSlabBindingAnalysis? slabBinding = null)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (hostElement is null)
        {
            throw new ArgumentNullException(nameof(hostElement));
        }

        if (rulePreview is null)
        {
            throw new ArgumentNullException(nameof(rulePreview));
        }

        EnsureSupportedHost(hostElement);

        IReadOnlyList<RebarRulePreviewItem> previewItems = ResolvePreviewItems(rulePreview, hostElement.HostKind);
        Document document = uiDocument.Document;
        Element host = document.GetElement(RevitElementIds.Create(hostElement.ElementId))
            ?? throw new InvalidOperationException("Выбранная конструкция не найдена в текущем документе Revit. Выберите её заново.");
        EnsureHostMatchesSelection(host, hostElement);

        ArrayRebarCreationRequest[] requests = BuildArrayCreationRequests(
                document,
                host,
                hostElement,
                rulePreview,
                previewItems,
                slabBinding)
            .ToArray();
        if (rulePreview.IsEngineeringPreview)
        {
            IsoFieldRebarChangePlan changePlan = BuildEngineeringChangePlan(document, host, requests);
            return ApplyEngineeringChangePlan(document, host, hostElement, requests, changePlan);
        }

        List<long> createdIds = new();
        logger.Info($"IsoField test array-family transaction starting. HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}; ValidRules={previewItems.Count}.");

        string transactionName = rulePreview.IsEngineeringPreview
            ? "TrueBIM: армирование по изополям"
            : "TrueBIM: пробное армирование по изополям";
        using Transaction transaction = new(document, transactionName);
        transaction.Start();
        IsoFieldRebarFailuresPreprocessor failuresPreprocessor = ConfigureFailureHandling(transaction);

        try
        {
            foreach (ArrayRebarCreationRequest request in requests)
            {
                FamilyInstance instance = CreateArrayFamily(document, host, request);
                MarkCreatedArrayFamily(
                    instance,
                    request.PreviewItem,
                    request.Placement,
                    request.Signature,
                    hostElement.ElementId);
                createdIds.Add(RevitElementIds.GetValue(instance.Id));
            }

            TransactionStatus commitStatus = transaction.Commit();
            EnsureTransactionCommitted(commitStatus, failuresPreprocessor);
        }
        catch (Exception exception)
        {
            RollBackIfStarted(transaction);
            logger.Error($"IsoField test array-family transaction rolled back. HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}.", exception);
            throw;
        }

        logger.Info($"IsoField test array families created. Count={createdIds.Count}; HostId={hostElement.ElementId}; HostKind={hostElement.HostKind}.");
        string resultKind = rulePreview.IsEngineeringPreview
            ? "семейства дополнительного армирования по отсечённым зонам"
            : "пробные семейства дополнительного армирования";
        return new IsoFieldRebarCreationResult(
            createdIds.Count,
            0,
            0,
            0,
            createdIds,
            Array.Empty<long>(),
            $"Создано {resultKind}: {createdIds.Count}. Конструкция: {hostElement.DisplayName}.");
    }

    public IsoFieldRebarChangePlan PreviewEngineeringChanges(
        UIDocument uiDocument,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview,
        IsoFieldSlabBindingAnalysis? slabBinding = null)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (hostElement is null)
        {
            throw new ArgumentNullException(nameof(hostElement));
        }

        if (rulePreview is null)
        {
            throw new ArgumentNullException(nameof(rulePreview));
        }

        EnsureSupportedHost(hostElement);

        if (!rulePreview.IsEngineeringPreview)
        {
            throw new InvalidOperationException("Повторное сравнение доступно только для рассчитанной раскладки арматуры.");
        }

        IReadOnlyList<RebarRulePreviewItem> previewItems = ResolvePreviewItems(rulePreview, hostElement.HostKind);
        Document document = uiDocument.Document;
        Element host = document.GetElement(RevitElementIds.Create(hostElement.ElementId))
            ?? throw new InvalidOperationException("Выбранная конструкция не найдена в текущем документе Revit. Выберите её заново.");
        EnsureHostMatchesSelection(host, hostElement);
        ArrayRebarCreationRequest[] requests = BuildArrayCreationRequests(
                document,
                host,
                hostElement,
                rulePreview,
                previewItems,
                slabBinding)
            .ToArray();
        return BuildEngineeringChangePlan(document, host, requests);
    }

    private static IReadOnlyList<RebarRulePreviewItem> ResolvePreviewItems(
        RebarRulePreviewResult rulePreview,
        string hostKind)
    {
        if (!rulePreview.CanCreateRebar)
        {
            throw new InvalidOperationException("Перед созданием пробного армирования рассчитайте раскладку без ошибок.");
        }

        RebarRulePreviewItem[] validItems = rulePreview.Items
            .Where(item => item.IsIncluded
                && item.HasValidRule
                && string.Equals(item.Rule.HostKind, hostKind, StringComparison.Ordinal))
            .ToArray();
        if (validItems.Length == 0)
        {
            throw new InvalidOperationException("Нет подходящей зоны для создания пробного армирования.");
        }

        return validItems;
    }

    private static void EnsureHostMatchesSelection(Element host, IsoFieldHostElement selectedHost)
    {
        Category category = host.Category
            ?? throw new InvalidOperationException("Не удалось определить категорию выбранной конструкции. Выберите другую стену или плиту.");

        long categoryId = RevitElementIds.GetValue(category.Id);
        string actualHostKind = categoryId switch
        {
            (long)BuiltInCategory.OST_Walls => WallHostKind,
            (long)BuiltInCategory.OST_Floors => SlabHostKind,
            _ => string.Empty
        };

        if (!string.Equals(actualHostKind, selectedHost.HostKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("После выбора тип конструкции изменился. Выберите стену или плиту заново.");
        }

        IsoFieldHostElement actualHost = IsoFieldHostSelectionService.CreateHostElement(host);
        if (selectedHost.GeometryProfile != IsoFieldHostGeometryProfile.Unknown
            && actualHost.GeometryProfile != selectedHost.GeometryProfile)
        {
            throw new InvalidOperationException(
                "Форма конструкции изменилась после выбора. Выберите конструкцию заново и повторите расчёт.");
        }

        if (selectedHost.Geometry is not null
            && (actualHost.Geometry is null
                || !HostGeometryMatches(selectedHost.Geometry, actualHost.Geometry)))
        {
            throw new InvalidOperationException(
                "Форма конструкции или отверстия изменились после привязки. Выберите конструкцию заново и повторите расчёт.");
        }
    }

    private static bool HostGeometryMatches(
        IsoFieldHostGeometry selected,
        IsoFieldHostGeometry actual)
    {
        if (!PointsMatch(selected.OriginFeet, actual.OriginFeet)
            || !PointsMatch(selected.AxisX, actual.AxisX)
            || !PointsMatch(selected.AxisY, actual.AxisY)
            || !PointsMatch(selected.Normal, actual.Normal)
            || selected.BoundaryLoopsFeet.Count != actual.BoundaryLoopsFeet.Count)
        {
            return false;
        }

        for (int loopIndex = 0; loopIndex < selected.BoundaryLoopsFeet.Count; loopIndex++)
        {
            IReadOnlyList<IsoFieldPoint> selectedLoop = selected.BoundaryLoopsFeet[loopIndex];
            IReadOnlyList<IsoFieldPoint> actualLoop = actual.BoundaryLoopsFeet[loopIndex];
            if (selectedLoop.Count != actualLoop.Count)
            {
                return false;
            }

            for (int pointIndex = 0; pointIndex < selectedLoop.Count; pointIndex++)
            {
                if (Math.Abs(selectedLoop[pointIndex].X - actualLoop[pointIndex].X) > GeometryComparisonToleranceFeet
                    || Math.Abs(selectedLoop[pointIndex].Y - actualLoop[pointIndex].Y) > GeometryComparisonToleranceFeet)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool PointsMatch(
        IsoFieldRebarPoint3D first,
        IsoFieldRebarPoint3D second)
    {
        return Math.Abs(first.XFeet - second.XFeet) <= GeometryComparisonToleranceFeet
            && Math.Abs(first.YFeet - second.YFeet) <= GeometryComparisonToleranceFeet
            && Math.Abs(first.ZFeet - second.ZFeet) <= GeometryComparisonToleranceFeet;
    }

    private void EnsureSupportedHost(IsoFieldHostElement hostElement)
    {
        IsoFieldHostSupportResult support = hostSupportService.Analyze(hostElement);
        if (!support.CanApplyRebar)
        {
            throw new InvalidOperationException(support.Message);
        }
    }

    private static RebarBarType ResolveBarType(
        Document document,
        string preferredName,
        IsoFieldRebarComponent? component = null)
    {
        List<RebarBarType> barTypes = new FilteredElementCollector(document)
            .OfClass(typeof(RebarBarType))
            .Cast<RebarBarType>()
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (barTypes.Count == 0)
        {
            throw new InvalidOperationException("В документе Revit нет ни одного типа арматурного стержня. Добавьте подходящий тип и повторите расчёт.");
        }

        if (component is not null)
        {
            RebarBarType? diameterMatch = barTypes.FirstOrDefault(type =>
            {
                Parameter? diameterParameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                return diameterParameter?.StorageType == StorageType.Double
                    && Math.Abs(
                        (diameterParameter.AsDouble() * MillimetersPerFoot)
                        - component.DiameterMillimeters) <= 0.2;
            });
            if (diameterMatch is null)
            {
                throw new InvalidOperationException(
                    $"В документе Revit не найден тип арматуры диаметром {component.DiameterMillimeters:0.###} мм для {component.DisplayName}.");
            }

            return diameterMatch;
        }

        string normalizedPreferredName = NormalizeBarTypeName(preferredName);
        RebarBarType? exactMatch = barTypes.FirstOrDefault(type =>
            string.Equals(NormalizeBarTypeName(type.Name), normalizedPreferredName, StringComparison.Ordinal)
            || string.Equals(NormalizeBarTypeName(type.FamilyName), normalizedPreferredName, StringComparison.Ordinal));
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        string diameterToken = new(normalizedPreferredName.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(diameterToken))
        {
            RebarBarType? diameterMatch = barTypes.FirstOrDefault(type =>
                ContainsOrdinal(NormalizeBarTypeName(type.Name), diameterToken)
                || ContainsOrdinal(NormalizeBarTypeName(type.FamilyName), diameterToken));
            if (diameterMatch is not null)
            {
                return diameterMatch;
            }
        }

        return barTypes[0];
    }

    private IReadOnlyList<ArrayRebarCreationRequest> BuildArrayCreationRequests(
        Document document,
        Element host,
        IsoFieldHostElement hostElement,
        RebarRulePreviewResult rulePreview,
        IReadOnlyList<RebarRulePreviewItem> previewItems,
        IsoFieldSlabBindingAnalysis? slabBinding)
    {
        if (!rulePreview.IsEngineeringPreview)
        {
            throw new InvalidOperationException(
                "Создание семейств дополнительного армирования доступно после инженерного расчёта раскладки.");
        }

        if (string.Equals(hostElement.HostKind, WallHostKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Для стен семейства дополнительного армирования пока не настроены. Выберите плиту.");
        }

        if (string.Equals(hostElement.HostKind, SlabHostKind, StringComparison.Ordinal))
        {
            IsoFieldRebarPlacementBounds bounds = BuildPlacementBounds(host);
            if (slabBinding?.CanProceed != true || hostElement.Geometry is null)
            {
                throw new InvalidOperationException(
                    "Привязка плиты устарела или не прошла проверку. Проверьте совмещение по трём точкам заново.");
            }

            IReadOnlyList<IsoFieldRebarPlacement> placements = slabPlacementService.BuildEngineeringPlacements(
                hostElement.Geometry,
                bounds.WidthZFeet,
                rulePreview);
            foreach (IsoFieldRebarPlacement placement in placements)
            {
                logger.Info($"IsoField slab rebar placement prepared. ZoneId={placement.ZoneId}; Direction={placement.Rule.PlacementDirection}; LengthFeet={placement.LengthFeet:0.###}.");
            }

            IReadOnlyList<IsoFieldArrayRebarPlacement> arrayPlacements = arrayGroupingService.BuildArrays(placements);
            logger.Info(
                $"IsoField slab array families prepared. Bars={placements.Count}; Families={arrayPlacements.Count}; "
                + $"SingleBarFamilies={arrayPlacements.Count(item => item.BarCount <= 1)}; HostId={hostElement.ElementId}.");
            return arrayPlacements
                .Select(placement => CreateArrayRequest(
                    document,
                    host,
                    previewItems.First(item => string.Equals(item.ZoneId, placement.ZoneId, StringComparison.Ordinal)),
                    placement))
                .ToArray();
        }

        throw new InvalidOperationException(
            "Семейства дополнительного армирования доступны только для горизонтальных плит.");
    }

    private static IsoFieldWallPlacementFrame BuildWallPlacementFrame(Wall wall)
    {
        if (wall.Location is not LocationCurve locationCurve)
        {
            throw new InvalidOperationException("Не удалось определить осевую линию стены. Выберите другую стену.");
        }

        if (locationCurve.Curve is not Line location)
        {
            throw new InvalidOperationException("Пробное армирование доступно только для прямых стен.");
        }

        XYZ start = location.GetEndPoint(0);
        XYZ end = location.GetEndPoint(1);
        XYZ direction = end - start;
        double lengthFeet = new XYZ(direction.X, direction.Y, 0).GetLength();
        if (lengthFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("Стена слишком короткая для пробного армирования.");
        }

        XYZ axis = NormalizeHorizontalDirection(direction);
        BoundingBoxXYZ boundingBox = wall.get_BoundingBox(null)
            ?? throw new InvalidOperationException("Не удалось определить границы выбранной стены.");
        double heightFeet = boundingBox.Max.Z - boundingBox.Min.Z;
        if (heightFeet < MinimumTestLengthFeet)
        {
            throw new InvalidOperationException("Размер выбранной стены слишком мал для пробного армирования.");
        }

        XYZ centerOnCurve = location.Evaluate(0.5, true);
        XYZ center = new(centerOnCurve.X, centerOnCurve.Y, (boundingBox.Min.Z + boundingBox.Max.Z) / 2);
        XYZ normal = ResolveWallNormal(wall, axis);

        return new IsoFieldWallPlacementFrame(
            ToPoint3D(center),
            ToPoint3D(axis),
            ToPoint3D(normal),
            lengthFeet,
            heightFeet);
    }

    private static IsoFieldRebarPlacementBounds BuildPlacementBounds(Element slab)
    {
        BoundingBoxXYZ boundingBox = slab.get_BoundingBox(null)
            ?? throw new InvalidOperationException("Не удалось определить границы выбранной плиты.");

        return new IsoFieldRebarPlacementBounds(
            boundingBox.Min.X,
            boundingBox.Min.Y,
            boundingBox.Min.Z,
            boundingBox.Max.X,
            boundingBox.Max.Y,
            boundingBox.Max.Z);
    }

    private static XYZ NormalizeHorizontalDirection(XYZ direction)
    {
        XYZ horizontal = new(direction.X, direction.Y, 0);
        if (horizontal.GetLength() < MinimumDirectionLengthFeet)
        {
            throw new InvalidOperationException("Не удалось определить горизонтальное направление стены.");
        }

        return horizontal.Normalize();
    }

    private static XYZ ResolveWallNormal(Wall wall, XYZ direction)
    {
        XYZ normal = wall.Orientation;
        if (normal.GetLength() < 1e-9)
        {
            normal = direction.CrossProduct(XYZ.BasisZ);
        }

        return normal.Normalize();
    }

    private static void MarkCreatedArrayFamily(
        FamilyInstance instance,
        RebarRulePreviewItem previewItem,
        IsoFieldArrayRebarPlacement placement,
        string? signature,
        long hostElementId)
    {
        Parameter? parameter = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (parameter is null
            || parameter.IsReadOnly
            || string.IsNullOrWhiteSpace(placement.StableId)
            || string.IsNullOrWhiteSpace(signature))
        {
            throw new InvalidOperationException(
                "Не удалось сохранить служебные данные семейства дополнительного армирования. Все изменения отменены.");
        }

        string sourceMetadata = previewItem.IsMerged
            ? $"sources={string.Join(",", previewItem.EffectiveSourceZoneIds)}; "
            : string.Empty;
        string comment =
            $"{IsoFieldRebarChangePlanService.OwnedCommentPrefix}; id={placement.StableId}; sig={signature}; host={hostElementId}; zone={previewItem.ZoneId}; "
            + sourceMetadata
            + $"layer={previewItem.Rule.LayerRole}; face={previewItem.Rule.Face}; shape={StraightArrayFamilyCode}; "
            + $"bars={placement.BarCount}; {placement.Component.DisplayName}";
        bool marked = parameter.Set(comment);
        if (!marked)
        {
            throw new InvalidOperationException(
                "Семейство создано, но Revit не позволил сохранить его служебные данные. Все изменения отменены.");
        }
    }

    private ArrayRebarCreationRequest CreateArrayRequest(
        Document document,
        Element host,
        RebarRulePreviewItem previewItem,
        IsoFieldArrayRebarPlacement placement)
    {
        ArrayFamilySymbolResolution symbolResolution = ResolveArrayFamilySymbol(
            document,
            host,
            placement);
        string signature = changePlanService.BuildSignature(placement);
        return new ArrayRebarCreationRequest(
            previewItem,
            symbolResolution,
            placement,
            signature);
    }

    private static ArrayFamilySymbolResolution ResolveArrayFamilySymbol(
        Document document,
        Element host,
        IsoFieldArrayRebarPlacement placement)
    {
        FamilySymbol[] familySymbols = new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(symbol => IsArrayFamilyForShape(symbol, StraightArrayFamilyCode))
            .OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (familySymbols.Length == 0)
        {
            throw new InvalidOperationException(
                $"В проекте не загружено семейство \"(Массив • У) Арматура • {StraightArrayFamilyCode}\" для прямых зон дополнительного армирования.");
        }

        string faceToken = placement.Rule.Face == IsoFieldRebarFace.Top
            ? "ВЕРХ"
            : "НИЗ";
        string axisToken = string.Equals(
            placement.Rule.PlacementDirection,
            "X",
            StringComparison.OrdinalIgnoreCase)
            ? "Б/О"
            : "Ц/О";
        int? concreteClass = ResolveConcreteClass(document, host);
        FamilySymbol[] parameterMatches = familySymbols
            .Where(symbol => TypeHasToken(symbol.Name, faceToken)
                && TypeHasToken(symbol.Name, axisToken)
                && SizeMatches(
                    symbol,
                    placement.Component.DiameterMillimeters,
                    placement.Component.SpacingMillimeters))
            .ToArray();
        FamilySymbol? exactMatch = parameterMatches.FirstOrDefault(symbol =>
                concreteClass is null || ParameterMatchesInteger(
                    symbol.LookupParameter(ConcreteClassParameter),
                    concreteClass.Value))
            ?? parameterMatches.FirstOrDefault();
        if (exactMatch is not null)
        {
            return new ArrayFamilySymbolResolution(
                exactMatch,
                false,
                exactMatch.Name,
                concreteClass);
        }

        FamilySymbol[] compatibleTemplates = familySymbols
            .Where(symbol => TypeHasToken(symbol.Name, faceToken)
                && TypeHasToken(symbol.Name, axisToken))
            .OrderBy(symbol => GetTemplateDistance(
                symbol,
                placement.Component.DiameterMillimeters,
                placement.Component.SpacingMillimeters,
                concreteClass))
            .ToArray();
        FamilySymbol? template = compatibleTemplates.FirstOrDefault();
        if (template is null)
        {
            throw new InvalidOperationException(
                $"В семействе {StraightArrayFamilyCode} нет основы для типа: "
                + $"{faceToken.ToLowerInvariant()}, {axisToken.ToLowerInvariant()}. "
                + "Загрузите хотя бы один тип для этой стороны и направления.");
        }

        string requiredTypeName = BuildArrayFamilyTypeName(
            template.Name,
            placement,
            faceToken,
            axisToken,
            concreteClass);
        return new ArrayFamilySymbolResolution(
            template,
            true,
            requiredTypeName,
            concreteClass);
    }

    private static double GetTemplateDistance(
        FamilySymbol symbol,
        double diameterMillimeters,
        double spacingMillimeters,
        int? concreteClass)
    {
        double diameter = GetParameterMillimeters(symbol.LookupParameter(DiameterParameter));
        double spacing = GetParameterMillimeters(symbol.LookupParameter(SpacingParameter));
        double concretePenalty = concreteClass is not null
            && !ParameterMatchesInteger(
                symbol.LookupParameter(ConcreteClassParameter),
                concreteClass.Value)
            ? 100000
            : 0;
        return concretePenalty
            + (Math.Abs(diameter - diameterMillimeters) * 1000)
            + Math.Abs(spacing - spacingMillimeters);
    }

    private static double GetParameterMillimeters(Parameter? parameter)
    {
        return parameter?.StorageType == StorageType.Double
            ? parameter.AsDouble() * MillimetersPerFoot
            : double.MaxValue / 10000;
    }

    private static string BuildArrayFamilyTypeName(
        string templateName,
        IsoFieldArrayRebarPlacement placement,
        string faceToken,
        string axisToken,
        int? concreteClass)
    {
        string concrete = concreteClass is null
            ? ResolveConcreteNamePrefix(templateName)
            : $"B{concreteClass.Value}";
        string diameter = placement.Component.DiameterMillimeters.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
        string spacing = placement.Component.SpacingMillimeters.ToString(
            "0.###",
            CultureInfo.InvariantCulture);
        return $"{concrete} • ({ToTitleCase(faceToken)} - {axisToken.ToLowerInvariant()}) "
            + $"⌀{diameter} A500С ш.{spacing} д/с";
    }

    private static string ResolveConcreteNamePrefix(string templateName)
    {
        string value = (templateName ?? string.Empty).Trim();
        int separatorIndex = value.IndexOf('•');
        return separatorIndex > 0
            ? value.Substring(0, separatorIndex).Trim()
            : "B25";
    }

    private static string ToTitleCase(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }

    private static bool SizeMatches(
        FamilySymbol symbol,
        double expectedDiameterMillimeters,
        double expectedSpacingMillimeters)
    {
        bool parametersMatch = ParameterMatchesMillimeters(
                symbol.LookupParameter(DiameterParameter),
                expectedDiameterMillimeters)
            && ParameterMatchesMillimeters(
                symbol.LookupParameter(SpacingParameter),
                expectedSpacingMillimeters);
        return parametersMatch
            || TypeNameMatchesSize(
                symbol.Name,
                expectedDiameterMillimeters,
                expectedSpacingMillimeters);
    }

    private static bool TypeNameMatchesSize(
        string value,
        double expectedDiameterMillimeters,
        double expectedSpacingMillimeters)
    {
        string normalized = (value ?? string.Empty)
            .ToUpperInvariant()
            .Replace(" ", string.Empty)
            .Replace(',', '.');
        string diameter = expectedDiameterMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
        string spacing = expectedSpacingMillimeters.ToString("0.###", CultureInfo.InvariantCulture);
        bool diameterMatches = normalized.IndexOf($"⌀{diameter}", StringComparison.Ordinal) >= 0
            || normalized.IndexOf($"Ø{diameter}", StringComparison.Ordinal) >= 0
            || normalized.IndexOf($"∅{diameter}", StringComparison.Ordinal) >= 0;
        bool spacingMatches = normalized.IndexOf($"Ш.{spacing}", StringComparison.Ordinal) >= 0
            || normalized.IndexOf($"Ш{spacing}", StringComparison.Ordinal) >= 0
            || normalized.IndexOf($"ШАГ{spacing}", StringComparison.Ordinal) >= 0;
        return diameterMatches && spacingMatches;
    }

    private static bool IsArrayFamilyForShape(FamilySymbol symbol, string shapeCode)
    {
        string normalized = NormalizeFamilyName(symbol.FamilyName);
        return normalized.IndexOf("МАССИВ", StringComparison.Ordinal) >= 0
            && normalized.IndexOf("АРМАТУРА", StringComparison.Ordinal) >= 0
            && normalized.IndexOf("НАБОР", StringComparison.Ordinal) < 0
            && normalized.EndsWith(shapeCode, StringComparison.Ordinal);
    }

    private static string NormalizeFamilyName(string value)
    {
        return new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool TypeHasToken(string value, string token)
    {
        return (value ?? string.Empty).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ParameterMatchesMillimeters(Parameter? parameter, double expectedMillimeters)
    {
        return parameter?.StorageType == StorageType.Double
            && Math.Abs((parameter.AsDouble() * MillimetersPerFoot) - expectedMillimeters) <= 0.2;
    }

    private static bool ParameterMatchesInteger(Parameter? parameter, int expected)
    {
        return parameter?.StorageType == StorageType.Integer
            && parameter.AsInteger() == expected;
    }

    private static int? ResolveConcreteClass(Document document, Element host)
    {
        Element? type = document.GetElement(host.GetTypeId());
        string value = $"{host.Name} {type?.Name}".ToUpperInvariant();
        for (int index = 0; index < value.Length - 1; index++)
        {
            if (value[index] is not ('B' or 'В') || !char.IsDigit(value[index + 1]))
            {
                continue;
            }

            int end = index + 1;
            while (end < value.Length && char.IsDigit(value[end]))
            {
                end++;
            }

            if (int.TryParse(
                value.Substring(index + 1, end - index - 1),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
            {
                return result;
            }
        }

        return null;
    }

    private IsoFieldRebarChangePlan BuildEngineeringChangePlan(
        Document document,
        Element host,
        IReadOnlyList<ArrayRebarCreationRequest> requests)
    {
        IsoFieldRebarPlanItem[] plannedItems = requests
            .Select(request => new IsoFieldRebarPlanItem(
                request.Placement.StableId
                    ?? throw new InvalidOperationException("У расчётного стержня нет служебного номера."),
                request.Signature
                    ?? throw new InvalidOperationException("У расчётного стержня нет контрольных данных.")))
            .ToArray();
        Dictionary<string, ArrayRebarCreationRequest> requestsByStableId = requests
            .Where(request => !string.IsNullOrWhiteSpace(request.Placement.StableId))
            .GroupBy(request => request.Placement.StableId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);
        long hostId = RevitElementIds.GetValue(host.Id);
        List<IsoFieldOwnedRebarSnapshot> existingElements = new();
        Dictionary<string, int> mismatchCounts = new(StringComparer.Ordinal);
        int loggedMismatchCount = 0;
        foreach (FamilyInstance instance in new FilteredElementCollector(document)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>())
        {
            Parameter? comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string? comment = comments?.AsString();
            if (comment is null
                || comment.IndexOf($"host={hostId};", StringComparison.Ordinal) < 0)
            {
                continue;
            }

            if (changePlanService.TryParseOwnedComment(
                RevitElementIds.GetValue(instance.Id),
                comment,
                out IsoFieldOwnedRebarSnapshot? snapshot))
            {
                IsoFieldOwnedRebarSnapshot activeSnapshot = snapshot!;
                activeSnapshot = activeSnapshot with
                {
                    StateSignature = BuildOwnedArrayFamilyStateSignature(instance)
                };
                if (requestsByStableId.TryGetValue(activeSnapshot.StableId, out ArrayRebarCreationRequest? request)
                    && !ArrayFamilyMatchesRequest(instance, request, out string mismatchReason))
                {
                    activeSnapshot = activeSnapshot with { Signature = null };
                    mismatchCounts[mismatchReason] = mismatchCounts.TryGetValue(mismatchReason, out int count)
                        ? count + 1
                        : 1;
                    if (loggedMismatchCount < 12)
                    {
                        logger.Warning(
                            $"IsoField owned array family differs from request. StableId={activeSnapshot.StableId}; "
                            + $"ElementId={RevitElementIds.GetValue(instance.Id)}; Reason={mismatchReason}; "
                            + $"Actual={DescribeArrayFamilyState(instance)}; Planned={DescribeArrayFamilyRequest(request)}.");
                        loggedMismatchCount++;
                    }
                }

                existingElements.Add(activeSnapshot);
            }
        }

        if (mismatchCounts.Count > 0)
        {
            logger.Warning(
                "IsoField owned array-family mismatch summary. "
                + string.Join(
                    "; ",
                    mismatchCounts
                        .OrderByDescending(item => item.Value)
                        .ThenBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => $"{item.Key}={item.Value}")));
        }

        return changePlanService.Build(plannedItems, existingElements);
    }

    private static string BuildOwnedArrayFamilyStateSignature(FamilyInstance instance)
    {
        LocationPoint? location = instance.Location as LocationPoint;
        return string.Join(
            "|",
            RevitElementIds.GetValue(instance.GetTypeId()),
            location is null ? "<none>" : FormatStatePoint(location.Point),
            location?.Rotation.ToString("0.#########", CultureInfo.InvariantCulture) ?? "<none>",
            FormatParameterState(instance.LookupParameter(StraightArrayLengthParameter)),
            FormatParameterState(instance.LookupParameter(ArrayWidthParameter)),
            FormatParameterState(instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM)));
    }

    private static string FormatParameterState(Parameter? parameter)
    {
        return parameter?.StorageType == StorageType.Double
            ? parameter.AsDouble().ToString("0.#########", CultureInfo.InvariantCulture)
            : "<none>";
    }

    private static string FormatStatePoint(XYZ point)
    {
        return string.Join(
            ":",
            point.X.ToString("0.#########", CultureInfo.InvariantCulture),
            point.Y.ToString("0.#########", CultureInfo.InvariantCulture),
            point.Z.ToString("0.#########", CultureInfo.InvariantCulture));
    }

    private static bool ArrayFamilyMatchesRequest(
        FamilyInstance instance,
        ArrayRebarCreationRequest request,
        out string mismatchReason)
    {
        bool symbolMatches = request.SymbolResolution.RequiresDuplication
            ? SymbolMatchesPlacement(
                instance.Symbol,
                request.Placement,
                request.SymbolResolution.ConcreteClass)
            : RevitElementIds.GetValue(instance.GetTypeId())
                == RevitElementIds.GetValue(request.SymbolResolution.TemplateSymbol.Id);
        if (!symbolMatches
            || instance.Location is not LocationPoint location)
        {
            mismatchReason = symbolMatches ? "Location" : "Symbol";
            return false;
        }

        XYZ plannedOrigin = ToXyz(request.Placement.FirstBarStart);
        if (!PointsMatch(location.Point, plannedOrigin))
        {
            mismatchReason = "Origin";
            return false;
        }

        if (!ParameterMatchesFeet(
                instance.LookupParameter(StraightArrayLengthParameter),
                request.Placement.BarLengthFeet))
        {
            mismatchReason = "Length";
            return false;
        }

        if (!ParameterMatchesFeet(
                instance.LookupParameter(ArrayWidthParameter),
                request.Placement.ArrayWidthFeet))
        {
            mismatchReason = "Width";
            return false;
        }

        double expectedRotation = ResolveArrayRotation(request.Placement);
        if (!AngleMatchesModuloPi(location.Rotation, expectedRotation))
        {
            mismatchReason = "Rotation";
            return false;
        }

        mismatchReason = string.Empty;
        return true;
    }

    private static string DescribeArrayFamilyState(FamilyInstance instance)
    {
        LocationPoint? location = instance.Location as LocationPoint;
        return $"Symbol={RevitElementIds.GetValue(instance.GetTypeId())}:{instance.Symbol.Name}; "
            + $"Origin={(location is null ? "<none>" : FormatStatePoint(location.Point))}; "
            + $"Rotation={(location is null ? "<none>" : location.Rotation.ToString("0.#########", CultureInfo.InvariantCulture))}; "
            + $"Length={FormatParameterState(instance.LookupParameter(StraightArrayLengthParameter))}; "
            + $"Width={FormatParameterState(instance.LookupParameter(ArrayWidthParameter))}";
    }

    private static string DescribeArrayFamilyRequest(ArrayRebarCreationRequest request)
    {
        return $"Symbol={RevitElementIds.GetValue(request.SymbolResolution.TemplateSymbol.Id)}:{request.SymbolResolution.TemplateSymbol.Name}; "
            + $"RequiresDuplication={request.SymbolResolution.RequiresDuplication}; "
            + $"Origin={FormatStatePoint(ToXyz(request.Placement.FirstBarStart))}; "
            + $"Rotation={ResolveArrayRotation(request.Placement).ToString("0.#########", CultureInfo.InvariantCulture)}; "
            + $"Length={request.Placement.BarLengthFeet.ToString("0.#########", CultureInfo.InvariantCulture)}; "
            + $"Width={request.Placement.ArrayWidthFeet.ToString("0.#########", CultureInfo.InvariantCulture)}";
    }

    private static bool ParameterMatchesFeet(Parameter? parameter, double expected)
    {
        return parameter?.StorageType == StorageType.Double
            && Math.Abs(parameter.AsDouble() - expected) <= GeometryComparisonToleranceFeet;
    }

    private static bool AngleMatchesModuloPi(double actual, double expected)
    {
        // Revit normalizes a negative rotation to the equivalent positive angle
        // (for example, -PI/2 becomes 3*PI/2). IEEERemainder avoids the 2*PI
        // boundary error caused by a tiny negative floating-point remainder.
        double difference = Math.Abs(Math.IEEERemainder(actual - expected, Math.PI));
        return difference <= 1e-6;
    }

    private static bool PointsMatch(XYZ first, XYZ second)
    {
        return first.DistanceTo(second) <= GeometryComparisonToleranceFeet;
    }

    private IsoFieldRebarCreationResult ApplyEngineeringChangePlan(
        Document document,
        Element host,
        IsoFieldHostElement hostElement,
        IReadOnlyList<ArrayRebarCreationRequest> requests,
        IsoFieldRebarChangePlan changePlan)
    {
        if (!changePlan.CanApply)
        {
            throw new InvalidOperationException(string.Join(" ", changePlan.Diagnostics));
        }

        if (!changePlan.HasChanges)
        {
            string unchangedMessage =
                $"Семейства дополнительного армирования уже соответствуют расчётной раскладке. Без изменений: {changePlan.UnchangedCount}. Конструкция: {hostElement.DisplayName}.";
            logger.Info($"IsoField engineering array families are current. HostId={hostElement.ElementId}; Unchanged={changePlan.UnchangedCount}.");
            return new IsoFieldRebarCreationResult(
                0,
                0,
                0,
                changePlan.UnchangedCount,
                Array.Empty<long>(),
                Array.Empty<long>(),
                unchangedMessage);
        }

        Dictionary<string, ArrayRebarCreationRequest> requestsByStableId = requests.ToDictionary(
            request => request.Placement.StableId!,
            StringComparer.Ordinal);
        List<long> createdIds = new();
        List<long> deletedIds = new();
        logger.Info(
            $"IsoField engineering array-family transaction starting. HostId={hostElement.ElementId}; {changePlan.Summary}");
        using Transaction transaction = new(document, "TrueBIM: семейства допармирования по изополям");
        transaction.Start();
        IsoFieldRebarFailuresPreprocessor failuresPreprocessor = ConfigureFailureHandling(transaction);
        try
        {
            foreach (IsoFieldRebarChange change in changePlan.Changes.Where(change =>
                change.Kind is IsoFieldRebarChangeKind.Update or IsoFieldRebarChangeKind.Delete))
            {
                foreach (long elementId in change.ExistingElementIds)
                {
                    document.Delete(RevitElementIds.Create(elementId));
                    deletedIds.Add(elementId);
                }
            }

            foreach (IsoFieldRebarChange change in changePlan.Changes.Where(change =>
                change.Kind is IsoFieldRebarChangeKind.Add or IsoFieldRebarChangeKind.Update))
            {
                ArrayRebarCreationRequest request = requestsByStableId[change.StableId];
                FamilyInstance instance = CreateArrayFamily(document, host, request);
                MarkCreatedArrayFamily(
                    instance,
                    request.PreviewItem,
                    request.Placement,
                    request.Signature,
                    hostElement.ElementId);
                createdIds.Add(RevitElementIds.GetValue(instance.Id));
            }

            TransactionStatus commitStatus = transaction.Commit();
            EnsureTransactionCommitted(commitStatus, failuresPreprocessor);
        }
        catch (Exception exception)
        {
            RollBackIfStarted(transaction);
            logger.Error(
                $"IsoField engineering array-family transaction rolled back. HostId={hostElement.ElementId}.",
                exception);
            throw;
        }

        string message = $"Семейства дополнительного армирования обновлены. {changePlan.Summary} Конструкция: {hostElement.DisplayName}.";
        logger.Info(
            $"IsoField engineering array-family changes applied. HostId={hostElement.ElementId}; {changePlan.Summary}");
        return new IsoFieldRebarCreationResult(
            changePlan.AddCount,
            changePlan.UpdateCount,
            changePlan.DeleteCount,
            changePlan.UnchangedCount,
            createdIds,
            deletedIds,
            message);
    }

    private static IsoFieldRebarFailuresPreprocessor ConfigureFailureHandling(
        Transaction transaction)
    {
        IsoFieldRebarFailuresPreprocessor preprocessor = new();
        FailureHandlingOptions options = transaction
            .GetFailureHandlingOptions()
            .SetFailuresPreprocessor(preprocessor)
            .SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
        return preprocessor;
    }

    private static void EnsureTransactionCommitted(
        TransactionStatus status,
        IsoFieldRebarFailuresPreprocessor failuresPreprocessor)
    {
        if (status == TransactionStatus.Committed)
        {
            return;
        }

        string details = failuresPreprocessor.BuildUserMessage();
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(details)
                ? "Revit отменил создание семейств дополнительного армирования. Модель не изменена; подробности записаны в журнал работы."
                : "Revit отменил создание семейств дополнительного армирования. Модель не изменена. " + details);
    }

    private static void RollBackIfStarted(Transaction transaction)
    {
        if (transaction.GetStatus() == TransactionStatus.Started)
        {
            transaction.RollBack();
        }
    }

    private static string NormalizeBarTypeName(string value)
    {
        return new string((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool ContainsOrdinal(string value, string search)
    {
        return value.IndexOf(search, StringComparison.Ordinal) >= 0;
    }

    private static FamilyInstance CreateArrayFamily(
        Document document,
        Element host,
        ArrayRebarCreationRequest request)
    {
        if (host is not Floor)
        {
            throw new InvalidOperationException(
                "Семейства дополнительного армирования этой версии предназначены только для плит.");
        }

        FamilySymbol symbol = ResolveOrCreateArrayFamilySymbol(document, request);
        if (!symbol.IsActive)
        {
            symbol.Activate();
            document.Regenerate();
        }

        Level level = document.GetElement(host.LevelId) as Level
            ?? ResolveNearestLevel(document, request.Placement.FirstBarStart.ZFeet);
        XYZ origin = ToXyz(request.Placement.FirstBarStart);
        FamilyInstance instance = document.Create.NewFamilyInstance(
            origin,
            symbol,
            level,
            StructuralType.NonStructural);
        Parameter? elevation = instance.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
        if (elevation is not null && !elevation.IsReadOnly)
        {
            elevation.Set(origin.Z - level.Elevation);
        }

        double rotation = ResolveArrayRotation(request.Placement);
        if (Math.Abs(rotation) > 1e-9)
        {
            Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(document, instance.Id, axis, rotation);
        }

        SetRequiredLengthParameter(
            instance,
            StraightArrayLengthParameter,
            request.Placement.BarLengthFeet);
        SetRequiredLengthParameter(
            instance,
            ArrayWidthParameter,
            request.Placement.ArrayWidthFeet);
        return instance;
    }

    private static FamilySymbol ResolveOrCreateArrayFamilySymbol(
        Document document,
        ArrayRebarCreationRequest request)
    {
        if (!request.SymbolResolution.RequiresDuplication)
        {
            return request.SymbolResolution.TemplateSymbol;
        }

        FamilySymbol? existing = new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(symbol => IsArrayFamilyForShape(symbol, StraightArrayFamilyCode)
                && SymbolMatchesPlacement(
                    symbol,
                    request.Placement,
                    request.SymbolResolution.ConcreteClass));
        if (existing is not null)
        {
            return existing;
        }

        FamilySymbol symbol = request.SymbolResolution.TemplateSymbol
            .Duplicate(request.SymbolResolution.RequiredTypeName) as FamilySymbol
            ?? throw new InvalidOperationException(
                "Revit не смог создать недостающий тип семейства дополнительного армирования. Все изменения отменены.");
        SetRequiredTypeLengthParameter(
            symbol,
            DiameterParameter,
            request.Placement.Component.DiameterMillimeters / MillimetersPerFoot);
        SetRequiredTypeLengthParameter(
            symbol,
            SpacingParameter,
            request.Placement.Component.SpacingMillimeters / MillimetersPerFoot);
        if (request.SymbolResolution.ConcreteClass is not null)
        {
            SetRequiredTypeIntegerParameter(
                symbol,
                ConcreteClassParameter,
                request.SymbolResolution.ConcreteClass.Value);
        }

        return symbol;
    }

    private static bool SymbolMatchesPlacement(
        FamilySymbol symbol,
        IsoFieldArrayRebarPlacement placement,
        int? concreteClass)
    {
        string faceToken = placement.Rule.Face == IsoFieldRebarFace.Top
            ? "ВЕРХ"
            : "НИЗ";
        string axisToken = string.Equals(
            placement.Rule.PlacementDirection,
            "X",
            StringComparison.OrdinalIgnoreCase)
            ? "Б/О"
            : "Ц/О";
        return IsArrayFamilyForShape(symbol, StraightArrayFamilyCode)
            && TypeHasToken(symbol.Name, faceToken)
            && TypeHasToken(symbol.Name, axisToken)
            && SizeMatches(
                symbol,
                placement.Component.DiameterMillimeters,
                placement.Component.SpacingMillimeters)
            && (concreteClass is null
                || ParameterMatchesInteger(
                    symbol.LookupParameter(ConcreteClassParameter),
                    concreteClass.Value));
    }

    private static void SetRequiredTypeLengthParameter(
        FamilySymbol symbol,
        string parameterName,
        double valueFeet)
    {
        Parameter? parameter = symbol.LookupParameter(parameterName);
        if (parameter is null
            || parameter.IsReadOnly
            || parameter.StorageType != StorageType.Double
            || !parameter.Set(valueFeet))
        {
            throw new InvalidOperationException(
                $"Не удалось заполнить параметр типа \"{parameterName}\" у семейства дополнительного армирования. Все изменения отменены.");
        }
    }

    private static void SetRequiredTypeIntegerParameter(
        FamilySymbol symbol,
        string parameterName,
        int value)
    {
        Parameter? parameter = symbol.LookupParameter(parameterName);
        if (parameter is null
            || parameter.IsReadOnly
            || parameter.StorageType != StorageType.Integer
            || !parameter.Set(value))
        {
            throw new InvalidOperationException(
                $"Не удалось заполнить параметр типа \"{parameterName}\" у семейства дополнительного армирования. Все изменения отменены.");
        }
    }

    private static Level ResolveNearestLevel(Document document, double elevationFeet)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => Math.Abs(level.Elevation - elevationFeet))
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "В проекте нет уровня для размещения семейства дополнительного армирования.");
    }

    private static void SetRequiredLengthParameter(
        FamilyInstance instance,
        string parameterName,
        double valueFeet)
    {
        Parameter? parameter = instance.LookupParameter(parameterName);
        if (parameter?.StorageType != StorageType.Double || parameter.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"В семействе {instance.Symbol.FamilyName} недоступен параметр \"{parameterName}\". Все изменения отменены.");
        }

        if (!parameter.Set(Math.Max(0, valueFeet)))
        {
            throw new InvalidOperationException(
                $"Revit не принял значение параметра \"{parameterName}\" семейства дополнительного армирования. Все изменения отменены.");
        }
    }

    private static double ResolveArrayRotation(IsoFieldArrayRebarPlacement placement)
    {
        double dx = placement.FirstBarEnd.XFeet - placement.FirstBarStart.XFeet;
        double dy = placement.FirstBarEnd.YFeet - placement.FirstBarStart.YFeet;
        double horizontalLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (horizontalLength <= MinimumDirectionLengthFeet)
        {
            throw new InvalidOperationException(
                $"Не удалось определить направление семейства для зоны {placement.ZoneName}.");
        }

        return Math.Atan2(dy, dx) - (Math.PI / 2);
    }

    private static XYZ ToXyz(IsoFieldRebarPoint3D point)
    {
        return new XYZ(point.XFeet, point.YFeet, point.ZFeet);
    }

    private static IsoFieldRebarPoint3D ToPoint3D(XYZ point)
    {
        return new IsoFieldRebarPoint3D(point.X, point.Y, point.Z);
    }

    private sealed record ArrayRebarCreationRequest(
        RebarRulePreviewItem PreviewItem,
        ArrayFamilySymbolResolution SymbolResolution,
        IsoFieldArrayRebarPlacement Placement,
        string Signature);

    private sealed record ArrayFamilySymbolResolution(
        FamilySymbol TemplateSymbol,
        bool RequiresDuplication,
        string RequiredTypeName,
        int? ConcreteClass);

    private sealed class IsoFieldRebarFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly List<string> messages = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                string description = failure.GetDescriptionText();
                if (!string.IsNullOrWhiteSpace(description))
                {
                    messages.Add(description.Trim());
                }
            }

            return messages.Count == 0
                ? FailureProcessingResult.Continue
                : FailureProcessingResult.ProceedWithRollBack;
        }

        public string BuildUserMessage()
        {
            string[] uniqueMessages = messages
                .Distinct(StringComparer.Ordinal)
                .Take(3)
                .ToArray();
            return uniqueMessages.Length == 0
                ? string.Empty
                : "Причина Revit: " + string.Join(" ", uniqueMessages);
        }
    }
}
