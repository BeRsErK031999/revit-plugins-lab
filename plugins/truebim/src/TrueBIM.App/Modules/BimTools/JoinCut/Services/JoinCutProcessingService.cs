using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Globalization;
using TrueBIM.App.Modules.BimTools.JoinCut.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.JoinCut.Services;

public sealed class JoinCutProcessingService
{
    private const int MaxPairsPerRun = 2000;

    public JoinCutProcessingResult PreviewJoin(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        JoinAction action)
    {
        return ProcessJoin(uiDocument, configuration, scope, action, execute: false);
    }

    public JoinCutProcessingResult ExecuteJoin(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        JoinAction action)
    {
        return ProcessJoin(uiDocument, configuration, scope, action, execute: true);
    }

    public JoinCutProcessingResult PreviewCut(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        CutAction action)
    {
        return ProcessCut(uiDocument, configuration, scope, action, execute: false);
    }

    public JoinCutProcessingResult ExecuteCut(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        CutAction action)
    {
        return ProcessCut(uiDocument, configuration, scope, action, execute: true);
    }

    private static JoinCutProcessingResult ProcessJoin(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        JoinAction action,
        bool execute)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(configuration, nameof(configuration));

        Document document = uiDocument.Document;
        List<string> messages = [];
        List<JoinCutOperationRow> rows = [];
        bool changedModel = false;
        bool truncated = false;

        IReadOnlyList<JoinRule> rules = configuration.JoinRules
            .Where(rule => rule.Enabled)
            .ToList();
        if (rules.Count == 0)
        {
            messages.Add("В выбранной конфигурации нет включенных правил соединения.");
            return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
        }

        IReadOnlyList<Element> scopeElements = CollectScopeElements(document, uiDocument, scope, messages);
        if (scopeElements.Count == 0)
        {
            return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
        }

        Transaction? transaction = null;
        try
        {
            if (execute)
            {
                transaction = new Transaction(document, "TrueBIM: соединение элементов");
                if (transaction.Start() != TransactionStatus.Started)
                {
                    messages.Add("Не удалось открыть транзакцию Revit для соединения элементов.");
                    return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
                }
            }

            foreach (JoinRule rule in rules)
            {
                IReadOnlyList<ElementCandidate> leftCandidates = CreateCandidates(scopeElements, rule.LeftFilter);
                IReadOnlyList<ElementCandidate> rightCandidates = CreateCandidates(scopeElements, rule.RightFilter);
                messages.Add($"{rule.Name}: найдено слева {leftCandidates.Count}, справа {rightCandidates.Count}.");

                HashSet<string> processedPairs = new(StringComparer.Ordinal);
                foreach ((ElementCandidate left, ElementCandidate right) in CreateIntersectingPairs(leftCandidates, rightCandidates, rule, processedPairs))
                {
                    if (rows.Count >= MaxPairsPerRun)
                    {
                        truncated = true;
                        messages.Add($"Обработка остановлена после {MaxPairsPerRun} пар. Сузьте категории или область обработки.");
                        break;
                    }

                    JoinCutOperationRow row = execute
                        ? ApplyJoinAction(document, rule.Name, left, right, action)
                        : PreviewJoinAction(document, rule.Name, left, right, action);
                    if (row.Status == JoinCutOperationStatuses.Done)
                    {
                        changedModel = true;
                    }

                    rows.Add(row);
                }

                if (truncated)
                {
                    break;
                }
            }

            if (execute && transaction is not null)
            {
                transaction.Commit();
            }
        }
        catch
        {
            if (transaction is not null && transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
        finally
        {
            transaction?.Dispose();
        }

        if (rows.Count == 0)
        {
            messages.Add("Подходящие пары элементов не найдены. Проверьте область обработки и выбранные категории.");
        }

        return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
    }

    private static JoinCutProcessingResult ProcessCut(
        UIDocument uiDocument,
        JoinCutConfiguration configuration,
        ProcessingScope scope,
        CutAction action,
        bool execute)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(configuration, nameof(configuration));

        Document document = uiDocument.Document;
        List<string> messages = [];
        List<JoinCutOperationRow> rows = [];
        bool changedModel = false;
        bool truncated = false;

        IReadOnlyList<CutRule> rules = configuration.CutRules
            .Where(rule => rule.Enabled)
            .ToList();
        if (rules.Count == 0)
        {
            messages.Add("В выбранной конфигурации нет включенных правил вырезания.");
            return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
        }

        IReadOnlyList<Element> scopeElements = CollectScopeElements(document, uiDocument, scope, messages);
        if (scopeElements.Count == 0)
        {
            return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
        }

        Transaction? transaction = null;
        try
        {
            if (execute)
            {
                transaction = new Transaction(document, "TrueBIM: вырезание элементов");
                if (transaction.Start() != TransactionStatus.Started)
                {
                    messages.Add("Не удалось открыть транзакцию Revit для вырезания элементов.");
                    return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
                }
            }

            foreach (CutRule rule in rules)
            {
                IReadOnlyList<ElementCandidate> cuttingCandidates = CreateCandidates(scopeElements, rule.CuttingElementsFilter);
                IReadOnlyList<ElementCandidate> cutCandidates = CreateCandidates(scopeElements, rule.CutElementsFilter);
                messages.Add($"{rule.Name}: найдено режущих {cuttingCandidates.Count}, вырезаемых {cutCandidates.Count}.");

                HashSet<string> processedPairs = new(StringComparer.Ordinal);
                foreach ((ElementCandidate cuttingElement, ElementCandidate cutElement) in CreateIntersectingPairs(cuttingCandidates, cutCandidates, processedPairs))
                {
                    if (rows.Count >= MaxPairsPerRun)
                    {
                        truncated = true;
                        messages.Add($"Обработка остановлена после {MaxPairsPerRun} пар. Сузьте категории или область обработки.");
                        break;
                    }

                    JoinCutOperationRow row = execute
                        ? ApplyCutAction(document, rule.Name, cuttingElement, cutElement, action, rule.SplitFacesOfCuttingSolid)
                        : PreviewCutAction(rule.Name, cuttingElement, cutElement, action);
                    if (row.Status == JoinCutOperationStatuses.Done)
                    {
                        changedModel = true;
                    }

                    rows.Add(row);
                }

                if (truncated)
                {
                    break;
                }
            }

            if (execute && transaction is not null)
            {
                transaction.Commit();
            }
        }
        catch
        {
            if (transaction is not null && transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
        finally
        {
            transaction?.Dispose();
        }

        if (rows.Count == 0)
        {
            messages.Add("Подходящие пары элементов для вырезания не найдены. Проверьте область обработки и выбранные категории.");
        }

        return new JoinCutProcessingResult(rows, messages, changedModel, truncated);
    }

    private static IReadOnlyList<Element> CollectScopeElements(
        Document document,
        UIDocument uiDocument,
        ProcessingScope scope,
        ICollection<string> messages)
    {
        List<Element> elements = [];
        switch (scope)
        {
            case ProcessingScope.SelectedElements:
                ICollection<ElementId> selectedIds = uiDocument.Selection.GetElementIds();
                if (selectedIds.Count == 0)
                {
                    messages.Add("Для области 'Выбранные элементы' сначала выберите элементы в Revit.");
                    return elements;
                }

                elements = selectedIds
                    .Select(document.GetElement)
                    .Where(element => element is not null)
                    .Select(element => element!)
                    .Where(CanUseElement)
                    .ToList();
                messages.Add($"Область обработки: выбранные элементы ({elements.Count}).");
                return elements;

            case ProcessingScope.ActiveView:
                View activeView = document.ActiveView;
                elements = new FilteredElementCollector(document, activeView.Id)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(CanUseElement)
                    .ToList();
                messages.Add($"Область обработки: активный вид '{activeView.Name}' ({elements.Count}).");
                return elements;

            case ProcessingScope.EntireProject:
                elements = new FilteredElementCollector(document)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Where(CanUseElement)
                    .ToList();
                messages.Add($"Область обработки: весь проект ({elements.Count}).");
                return elements;

            default:
                messages.Add("Неизвестная область обработки.");
                return elements;
        }
    }

    private static IReadOnlyList<ElementCandidate> CreateCandidates(
        IReadOnlyList<Element> elements,
        ElementFilterDefinition filter)
    {
        List<ElementCandidate> candidates = [];
        foreach (Element element in elements)
        {
            if (!MatchesFilter(element, filter))
            {
                continue;
            }

            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            if (boundingBox is null)
            {
                continue;
            }

            candidates.Add(ElementCandidate.Create(element, boundingBox));
        }

        return candidates;
    }

    private static IEnumerable<(ElementCandidate Left, ElementCandidate Right)> CreateIntersectingPairs(
        IReadOnlyList<ElementCandidate> leftCandidates,
        IReadOnlyList<ElementCandidate> rightCandidates,
        JoinRule rule,
        ISet<string> processedPairs)
    {
        foreach (ElementCandidate left in leftCandidates)
        {
            foreach (ElementCandidate right in rightCandidates)
            {
                if (left.ElementId == right.ElementId || !BoxesIntersect(left, right))
                {
                    continue;
                }

                long firstId = Math.Min(left.ElementId, right.ElementId);
                long secondId = Math.Max(left.ElementId, right.ElementId);
                string pairKey = $"{firstId}:{secondId}";
                if (!processedPairs.Add(pairKey))
                {
                    continue;
                }

                if (rule.OnlyParallelWalls && !AreParallelWalls(left.Element, right.Element))
                {
                    continue;
                }

                yield return (left, right);
            }
        }
    }

    private static IEnumerable<(ElementCandidate Left, ElementCandidate Right)> CreateIntersectingPairs(
        IReadOnlyList<ElementCandidate> leftCandidates,
        IReadOnlyList<ElementCandidate> rightCandidates,
        ISet<string> processedPairs)
    {
        foreach (ElementCandidate left in leftCandidates)
        {
            foreach (ElementCandidate right in rightCandidates)
            {
                if (left.ElementId == right.ElementId || !BoxesIntersect(left, right))
                {
                    continue;
                }

                long firstId = Math.Min(left.ElementId, right.ElementId);
                long secondId = Math.Max(left.ElementId, right.ElementId);
                string pairKey = $"{firstId}:{secondId}";
                if (!processedPairs.Add(pairKey))
                {
                    continue;
                }

                yield return (left, right);
            }
        }
    }

    private static JoinCutOperationRow PreviewJoinAction(
        Document document,
        string ruleName,
        ElementCandidate left,
        ElementCandidate right,
        JoinAction action)
    {
        try
        {
            bool joined = JoinGeometryUtils.AreElementsJoined(document, left.Element, right.Element);
            return action switch
            {
                JoinAction.Join when joined => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Элементы уже соединены."),
                JoinAction.Join => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Ready, "Можно соединить."),
                JoinAction.Unjoin when !joined => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Элементы не соединены."),
                JoinAction.Unjoin => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Ready, "Можно отсоединить."),
                JoinAction.SwitchJoinOrder when !joined => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Порядок можно инвертировать только у соединенных элементов."),
                JoinAction.SwitchJoinOrder => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Ready, "Можно инвертировать порядок соединения."),
                _ => CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Неизвестное действие.")
            };
        }
        catch (Exception exception)
        {
            return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, $"Revit не разрешает проверить пару: {exception.Message}");
        }
    }

    private static JoinCutOperationRow ApplyJoinAction(
        Document document,
        string ruleName,
        ElementCandidate left,
        ElementCandidate right,
        JoinAction action)
    {
        try
        {
            bool joined = JoinGeometryUtils.AreElementsJoined(document, left.Element, right.Element);
            switch (action)
            {
                case JoinAction.Join:
                    if (joined)
                    {
                        return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Элементы уже соединены.");
                    }

                    JoinGeometryUtils.JoinGeometry(document, left.Element, right.Element);
                    return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Done, "Элементы соединены.");

                case JoinAction.Unjoin:
                    if (!joined)
                    {
                        return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Элементы не соединены.");
                    }

                    JoinGeometryUtils.UnjoinGeometry(document, left.Element, right.Element);
                    return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Done, "Соединение снято.");

                case JoinAction.SwitchJoinOrder:
                    if (!joined)
                    {
                        return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Порядок можно инвертировать только у соединенных элементов.");
                    }

                    JoinGeometryUtils.SwitchJoinOrder(document, left.Element, right.Element);
                    return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Done, "Порядок соединения инвертирован.");

                default:
                    return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Skipped, "Неизвестное действие.");
            }
        }
        catch (Exception exception)
        {
            return CreateRow(ruleName, left, right, JoinCutOperationStatuses.Failed, exception.Message);
        }
    }

    private static JoinCutOperationRow PreviewCutAction(
        string ruleName,
        ElementCandidate cuttingElement,
        ElementCandidate cutElement,
        CutAction action)
    {
        try
        {
            CutState state = GetCutState(cuttingElement, cutElement);
            return action switch
            {
                CutAction.Cut when state.HasRequestedCut => CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Вырезание уже существует."),
                CutAction.Cut => PreviewCreateCut(ruleName, cuttingElement, cutElement),
                CutAction.Uncut when !state.HasRequestedCut => CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Эти элементы не связаны вырезанием в выбранном направлении."),
                CutAction.Uncut => CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Ready, "Можно отменить вырезание."),
                _ => CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Неизвестное действие.")
            };
        }
        catch (Exception exception)
        {
            return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, $"Revit не разрешает проверить вырезание: {exception.Message}");
        }
    }

    private static JoinCutOperationRow PreviewCreateCut(
        string ruleName,
        ElementCandidate cuttingElement,
        ElementCandidate cutElement)
    {
        CutCapability capability = GetCutCapability(cuttingElement, cutElement);
        return capability.CanCut
            ? CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Ready, capability.IsVoidCut ? "Можно вырезать пустотным семейством." : "Можно вырезать твердотельной геометрией.")
            : CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, capability.Message);
    }

    private static JoinCutOperationRow ApplyCutAction(
        Document document,
        string ruleName,
        ElementCandidate cuttingElement,
        ElementCandidate cutElement,
        CutAction action,
        bool splitFacesOfCuttingSolid)
    {
        try
        {
            CutState state = GetCutState(cuttingElement, cutElement);
            switch (action)
            {
                case CutAction.Cut:
                    if (state.HasRequestedCut)
                    {
                        return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Вырезание уже существует.");
                    }

                    CutCapability capability = GetCutCapability(cuttingElement, cutElement);
                    if (!capability.CanCut)
                    {
                        return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Failed, capability.Message);
                    }

                    if (capability.IsVoidCut)
                    {
                        InstanceVoidCutUtils.AddInstanceVoidCut(document, cutElement.Element, cuttingElement.Element);
                        return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Done, "Вырезание пустотным семейством выполнено.");
                    }

                    SolidSolidCutUtils.AddCutBetweenSolids(document, cutElement.Element, cuttingElement.Element, splitFacesOfCuttingSolid);
                    return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Done, "Вырезание твердотельной геометрией выполнено.");

                case CutAction.Uncut:
                    if (state.VoidCutExists)
                    {
                        InstanceVoidCutUtils.RemoveInstanceVoidCut(document, cutElement.Element, cuttingElement.Element);
                        return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Done, "Вырезание пустотным семейством отменено.");
                    }

                    if (state.SolidCutExists && state.CuttingElementCutsCutElement)
                    {
                        SolidSolidCutUtils.RemoveCutBetweenSolids(document, cutElement.Element, cuttingElement.Element);
                        return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Done, "Вырезание твердотельной геометрией отменено.");
                    }

                    return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Эти элементы не связаны вырезанием в выбранном направлении.");

                default:
                    return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Skipped, "Неизвестное действие.");
            }
        }
        catch (Exception exception)
        {
            return CreateRow(ruleName, cuttingElement, cutElement, JoinCutOperationStatuses.Failed, exception.Message);
        }
    }

    private static CutState GetCutState(ElementCandidate cuttingElement, ElementCandidate cutElement)
    {
        bool solidCutExists = false;
        bool cuttingElementCutsCutElement = false;
        try
        {
            solidCutExists = SolidSolidCutUtils.CutExistsBetweenElements(cuttingElement.Element, cutElement.Element, out bool firstCutsSecond);
            cuttingElementCutsCutElement = solidCutExists && firstCutsSecond;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }

        bool voidCutExists = false;
        try
        {
            voidCutExists = InstanceVoidCutUtils.InstanceVoidCutExists(cutElement.Element, cuttingElement.Element);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
        }

        return new CutState(solidCutExists, cuttingElementCutsCutElement, voidCutExists);
    }

    private static CutCapability GetCutCapability(ElementCandidate cuttingElement, ElementCandidate cutElement)
    {
        if (CanCreateVoidCut(cuttingElement.Element, cutElement.Element))
        {
            return new CutCapability(true, true, string.Empty);
        }

        try
        {
            if (!SolidSolidCutUtils.IsAllowedForSolidCut(cuttingElement.Element)
                || !SolidSolidCutUtils.IsAllowedForSolidCut(cutElement.Element))
            {
                return new CutCapability(false, false, "Один из элементов не поддерживает твердотельное вырезание Revit.");
            }

            bool canCut = SolidSolidCutUtils.CanElementCutElement(cuttingElement.Element, cutElement.Element, out CutFailureReason reason);
            return canCut
                ? new CutCapability(true, false, string.Empty)
                : new CutCapability(false, false, $"Revit не разрешает вырезание этой пары: {reason}.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new CutCapability(false, false, $"Revit не разрешает вырезание этой пары: {exception.Message}");
        }
    }

    private static bool CanCreateVoidCut(Element cuttingElement, Element cutElement)
    {
        try
        {
            return InstanceVoidCutUtils.IsVoidInstanceCuttingElement(cuttingElement)
                && InstanceVoidCutUtils.CanBeCutWithVoid(cutElement);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static JoinCutOperationRow CreateRow(
        string ruleName,
        ElementCandidate left,
        ElementCandidate right,
        string status,
        string message)
    {
        return new JoinCutOperationRow(
            ruleName,
            left.ElementId,
            left.CategoryName,
            left.ElementName,
            right.ElementId,
            right.CategoryName,
            right.ElementName,
            status,
            message);
    }

    private static bool MatchesFilter(Element element, ElementFilterDefinition filter)
    {
        bool categoryMatches = filter.Categories.Count == 0 || MatchesCategory(element, filter.Categories);
        bool parameterMatches = filter.ParameterConditions.Count == 0 || filter.ParameterConditions.All(condition => MatchesParameter(element, condition));

        return filter.CategoryAndParameterOperator == FilterLogicalOperator.And
            ? categoryMatches && parameterMatches
            : categoryMatches || parameterMatches;
    }

    private static bool MatchesCategory(Element element, IReadOnlyCollection<BuiltInCategory> categories)
    {
        Category? category = element.Category;
        if (category is null || category.Id == ElementId.InvalidElementId)
        {
            return false;
        }

        long categoryId = RevitElementIds.GetValue(category.Id);
        return categories.Any(item => categoryId == (long)(int)item);
    }

    private static bool MatchesParameter(Element element, ParameterFilterCondition condition)
    {
        Parameter? parameter = FindParameter(element, condition);
        bool exists = parameter is not null;
        if (condition.Operator == ParameterCompareOperator.Exists)
        {
            return exists;
        }

        if (condition.Operator == ParameterCompareOperator.NotExists)
        {
            return !exists;
        }

        if (!exists || parameter is null)
        {
            return false;
        }

        string actualValue = GetParameterValue(parameter);
        string expectedValue = condition.Value ?? string.Empty;
        return condition.Operator switch
        {
            ParameterCompareOperator.Equals => string.Equals(actualValue, expectedValue, StringComparison.CurrentCultureIgnoreCase),
            ParameterCompareOperator.NotEquals => !string.Equals(actualValue, expectedValue, StringComparison.CurrentCultureIgnoreCase),
            ParameterCompareOperator.Contains => actualValue.IndexOf(expectedValue, StringComparison.CurrentCultureIgnoreCase) >= 0,
            ParameterCompareOperator.NotContains => actualValue.IndexOf(expectedValue, StringComparison.CurrentCultureIgnoreCase) < 0,
            ParameterCompareOperator.Greater => CompareDouble(actualValue, expectedValue, static value => value > 0),
            ParameterCompareOperator.GreaterOrEqual => CompareDouble(actualValue, expectedValue, static value => value >= 0),
            ParameterCompareOperator.Less => CompareDouble(actualValue, expectedValue, static value => value < 0),
            ParameterCompareOperator.LessOrEqual => CompareDouble(actualValue, expectedValue, static value => value <= 0),
            _ => false
        };
    }

    private static Parameter? FindParameter(Element element, ParameterFilterCondition condition)
    {
        if (condition.BuiltInParameter.HasValue)
        {
            Parameter? parameter = element.get_Parameter(condition.BuiltInParameter.Value);
            if (parameter is not null)
            {
                return parameter;
            }
        }

        if (condition.ParameterGuid.HasValue)
        {
            Parameter? parameter = element.get_Parameter(condition.ParameterGuid.Value);
            if (parameter is not null)
            {
                return parameter;
            }
        }

        return string.IsNullOrWhiteSpace(condition.ParameterName)
            ? null
            : element.LookupParameter(condition.ParameterName);
    }

    private static string GetParameterValue(Parameter parameter)
    {
        string? valueString = parameter.AsValueString();
        if (!string.IsNullOrWhiteSpace(valueString))
        {
            return valueString!;
        }

        return parameter.StorageType switch
        {
            StorageType.String => parameter.AsString() ?? string.Empty,
            StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
            StorageType.Double => parameter.AsDouble().ToString(CultureInfo.InvariantCulture),
            StorageType.ElementId => RevitElementIds.GetValue(parameter.AsElementId()).ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }

    private static bool CompareDouble(string actualValue, string expectedValue, Func<int, bool> predicate)
    {
        if (!double.TryParse(actualValue, NumberStyles.Float, CultureInfo.CurrentCulture, out double actual)
            && !double.TryParse(actualValue, NumberStyles.Float, CultureInfo.InvariantCulture, out actual))
        {
            return false;
        }

        if (!double.TryParse(expectedValue, NumberStyles.Float, CultureInfo.CurrentCulture, out double expected)
            && !double.TryParse(expectedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out expected))
        {
            return false;
        }

        return predicate(actual.CompareTo(expected));
    }

    private static bool CanUseElement(Element element)
    {
        Category? category = element.Category;
        return category is not null
            && category.Id != ElementId.InvalidElementId
            && category.CategoryType == CategoryType.Model
            && element is not RevitLinkInstance
            && !element.ViewSpecific;
    }

    private static bool BoxesIntersect(ElementCandidate first, ElementCandidate second)
    {
        return first.MinX <= second.MaxX
            && first.MaxX >= second.MinX
            && first.MinY <= second.MaxY
            && first.MaxY >= second.MinY
            && first.MinZ <= second.MaxZ
            && first.MaxZ >= second.MinZ;
    }

    private static bool AreParallelWalls(Element first, Element second)
    {
        return first is Wall firstWall
            && second is Wall secondWall
            && TryGetWallDirection(firstWall, out XYZ firstDirection)
            && TryGetWallDirection(secondWall, out XYZ secondDirection)
            && firstDirection.CrossProduct(secondDirection).GetLength() < 0.001;
    }

    private static bool TryGetWallDirection(Wall wall, out XYZ direction)
    {
        direction = XYZ.Zero;
        if (wall.Location is not LocationCurve locationCurve)
        {
            return false;
        }

        Curve curve = locationCurve.Curve;
        XYZ vector = curve.GetEndPoint(1) - curve.GetEndPoint(0);
        if (vector.GetLength() < 0.0001)
        {
            return false;
        }

        direction = vector.Normalize();
        return true;
    }

    private sealed record ElementCandidate(
        Element Element,
        long ElementId,
        string CategoryName,
        string ElementName,
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ)
    {
        public static ElementCandidate Create(Element element, BoundingBoxXYZ boundingBox)
        {
            return new ElementCandidate(
                element,
                RevitElementIds.GetValue(element.Id),
                element.Category?.Name ?? "Без категории",
                GetElementName(element),
                boundingBox.Min.X,
                boundingBox.Min.Y,
                boundingBox.Min.Z,
                boundingBox.Max.X,
                boundingBox.Max.Y,
                boundingBox.Max.Z);
        }

        private static string GetElementName(Element element)
        {
            try
            {
                return string.IsNullOrWhiteSpace(element.Name)
                    ? element.GetType().Name
                    : element.Name;
            }
            catch (InvalidOperationException)
            {
                return element.GetType().Name;
            }
        }
    }

    private sealed record CutState(
        bool SolidCutExists,
        bool CuttingElementCutsCutElement,
        bool VoidCutExists)
    {
        public bool HasRequestedCut => VoidCutExists || (SolidCutExists && CuttingElementCutsCutElement);
    }

    private sealed record CutCapability(
        bool CanCut,
        bool IsVoidCut,
        string Message);
}

public sealed record JoinCutProcessingResult(
    IReadOnlyList<JoinCutOperationRow> Rows,
    IReadOnlyList<string> Messages,
    bool ChangedModel,
    bool Truncated);

public sealed record JoinCutOperationRow(
    string RuleName,
    long LeftElementId,
    string LeftCategoryName,
    string LeftElementName,
    long RightElementId,
    string RightCategoryName,
    string RightElementName,
    string Status,
    string Message);

public static class JoinCutOperationStatuses
{
    public const string Ready = "Готово";
    public const string Done = "Выполнено";
    public const string Skipped = "Пропущено";
    public const string Failed = "Ошибка";
}
