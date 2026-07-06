using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Services;

public sealed class ParameterCopyService
{
    private readonly ParameterCompatibilityService compatibilityService;
    private readonly ITrueBimLogger logger;

    public ParameterCopyService(ParameterCompatibilityService compatibilityService, ITrueBimLogger logger)
    {
        this.compatibilityService = compatibilityService ?? throw new ArgumentNullException(nameof(compatibilityService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<CopyParameterRow> CollectCopyableParameters(Document document, Element sourceElement)
    {
        List<CopyParameterRow> rows = new();
        HashSet<string> seenKeys = new(StringComparer.Ordinal);

        CollectFromElement(sourceElement, ParameterSourceKind.Instance, rows, seenKeys);

        Element? sourceType = ResolveTypeElement(document, sourceElement);
        if (sourceType is not null)
        {
            CollectFromElement(sourceType, ParameterSourceKind.Type, rows, seenKeys);
        }

        return rows
            .OrderBy(row => row.SourceKind)
            .ThenBy(row => row.ParameterName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public ParameterCopyResult Copy(
        Document document,
        Element sourceElement,
        IReadOnlyList<CopyParameterRow> selectedParameters,
        IReadOnlyList<Element> targetElements)
    {
        List<ElementCopyReportRow> rows = new();
        string sourceElementLabel = BuildElementLabel(sourceElement);

        using Transaction transaction = new(document, "TrueBIM: копирование параметров");
        transaction.Start();

        try
        {
            foreach (Element targetElement in targetElements)
            {
                string targetLabel = BuildElementLabel(targetElement);
                foreach (CopyParameterRow parameterRow in selectedParameters)
                {
                    CopyOneParameter(document, targetElement, targetLabel, parameterRow, rows);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.RollBack();
            throw;
        }

        logger.Info(
            $"Copied parameters from '{sourceElementLabel}'. Targets: {targetElements.Count}; parameters: {selectedParameters.Count}; copied: {rows.Count(row => row.Succeeded)}; skipped: {rows.Count(row => !row.Succeeded)}.");

        return new ParameterCopyResult(
            sourceElementLabel,
            selectedParameters.Count,
            targetElements.Count,
            rows);
    }

    public static string BuildElementLabel(Element element)
    {
        string categoryName = element.Category?.Name ?? "Элемент";
        string name = string.IsNullOrWhiteSpace(element.Name) ? string.Empty : $" {element.Name}";
        return $"{categoryName}{name} [{RevitElementIds.GetValue(element.Id)}]";
    }

    private void CollectFromElement(
        Element element,
        ParameterSourceKind sourceKind,
        List<CopyParameterRow> rows,
        HashSet<string> seenKeys)
    {
        foreach (Parameter parameter in element.Parameters.Cast<Parameter>())
        {
            if (!compatibilityService.CanCollect(parameter))
            {
                continue;
            }

            ParameterValueSnapshot? value = ParameterValueSnapshot.TryCreate(parameter);
            if (value is null)
            {
                continue;
            }

            ParameterIdentity identity = CreateIdentity(parameter);
            string key = CreateParameterKey(identity, sourceKind);
            if (!seenKeys.Add(key))
            {
                continue;
            }

            rows.Add(new CopyParameterRow(
                identity,
                value,
                sourceKind,
                compatibilityService.BuildWarning(parameter, sourceKind)));
        }
    }

    private void CopyOneParameter(
        Document document,
        Element targetElement,
        string targetLabel,
        CopyParameterRow parameterRow,
        List<ElementCopyReportRow> rows)
    {
        Parameter? targetParameter = FindTargetParameter(document, targetElement, parameterRow, out string? missingReason);
        if (targetParameter is null)
        {
            rows.Add(new ElementCopyReportRow(targetLabel, parameterRow.ParameterName, false, missingReason ?? "параметр отсутствует"));
            return;
        }

        if (!compatibilityService.CanWrite(targetParameter, parameterRow.Value, out string reason))
        {
            rows.Add(new ElementCopyReportRow(targetLabel, parameterRow.ParameterName, false, reason));
            return;
        }

        try
        {
            parameterRow.Value.ApplyTo(targetParameter);
            rows.Add(new ElementCopyReportRow(targetLabel, parameterRow.ParameterName, true, "скопировано"));
        }
        catch (Exception exception)
        {
            rows.Add(new ElementCopyReportRow(targetLabel, parameterRow.ParameterName, false, NormalizeExceptionMessage(exception)));
        }
    }

    private static Parameter? FindTargetParameter(
        Document document,
        Element targetElement,
        CopyParameterRow parameterRow,
        out string? missingReason)
    {
        missingReason = null;
        Element parameterOwner = targetElement;
        if (parameterRow.SourceKind == ParameterSourceKind.Type)
        {
            Element? targetType = ResolveTypeElement(document, targetElement);
            if (targetType is null)
            {
                missingReason = "тип элемента-получателя не найден";
                return null;
            }

            parameterOwner = targetType;
        }

        ParameterIdentity identity = parameterRow.Identity;
        if (identity.SharedParameterGuid is Guid sharedParameterGuid)
        {
            Parameter? sharedParameter = parameterOwner.get_Parameter(sharedParameterGuid);
            if (sharedParameter is not null)
            {
                return sharedParameter;
            }
        }

        if (identity.BuiltInParameter is BuiltInParameter builtInParameter)
        {
            Parameter? builtInTargetParameter = parameterOwner.get_Parameter(builtInParameter);
            if (builtInTargetParameter is not null)
            {
                return builtInTargetParameter;
            }
        }

        return parameterOwner.LookupParameter(identity.Name);
    }

    private static Element? ResolveTypeElement(Document document, Element element)
    {
        ElementId typeId = element.GetTypeId();
        return typeId == ElementId.InvalidElementId ? null : document.GetElement(typeId);
    }

    private static ParameterIdentity CreateIdentity(Parameter parameter)
    {
        Guid? sharedGuid = null;
        try
        {
            if (parameter.IsShared)
            {
                sharedGuid = parameter.GUID;
            }
        }
        catch
        {
            sharedGuid = null;
        }

        return new ParameterIdentity(
            parameter.Definition.Name,
            sharedGuid,
            TryGetBuiltInParameter(parameter),
            parameter.StorageType);
    }

    private static BuiltInParameter? TryGetBuiltInParameter(Parameter parameter)
    {
        return parameter.Definition is InternalDefinition internalDefinition
            && internalDefinition.BuiltInParameter != BuiltInParameter.INVALID
                ? internalDefinition.BuiltInParameter
                : null;
    }

    private static string CreateParameterKey(ParameterIdentity identity, ParameterSourceKind sourceKind)
    {
        string identityKey = identity.SharedParameterGuid?.ToString("D")
            ?? identity.BuiltInParameter?.ToString()
            ?? identity.Name;
        return $"{sourceKind}:{identity.StorageType}:{identityKey}";
    }

    private static string NormalizeExceptionMessage(Exception exception)
    {
        return string.IsNullOrWhiteSpace(exception.Message)
            ? "ошибка записи значения"
            : exception.Message;
    }
}
