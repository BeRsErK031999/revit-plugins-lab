using System.Globalization;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterFamilyAnalysisService
{
    private readonly ISharedParameterVersionAdapter versionAdapter;
    private readonly FamilyFormulaDependencyParser formulaParser;
    private readonly ITrueBimLogger logger;

    public SharedParameterFamilyAnalysisService(
        ISharedParameterVersionAdapter versionAdapter,
        FamilyFormulaDependencyParser formulaParser,
        ITrueBimLogger logger)
    {
        this.versionAdapter = versionAdapter ?? throw new ArgumentNullException(nameof(versionAdapter));
        this.formulaParser = formulaParser ?? throw new ArgumentNullException(nameof(formulaParser));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ProjectFamilyPresence> CollectProjectFamilyPresence(
        Document projectDocument,
        Guid targetGuid,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(projectDocument, nameof(projectDocument));
        if (projectDocument.IsFamilyDocument)
        {
            return [];
        }

        List<ProjectFamilyPresence> results = [];
        foreach (Family family in new FilteredElementCollector(projectDocument)
                     .OfClass(typeof(Family))
                     .Cast<Family>()
                     .OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(CheckProjectFamilyPresence(projectDocument, family, targetGuid));
        }

        logger.Info(
            $"Shared Parameter Inspector family presence completed. "
            + $"Guid={targetGuid:D}; Families={results.Count}; Found={results.Count(result => result.ContainsParameter)}.");
        return results;
    }

    public ProjectFamilyPresence CheckProjectFamilyPresence(
        Document projectDocument,
        Family family,
        Guid targetGuid)
    {
        Guard.NotNull(projectDocument, nameof(projectDocument));
        Guard.NotNull(family, nameof(family));

        long familyId = RevitElementIds.GetValue(family.Id);
        string categoryName = family.FamilyCategory?.Name ?? "Без категории";
        if (family.IsInPlace)
        {
            return new ProjectFamilyPresence(
                familyId,
                family.Name,
                categoryName,
                FamilyPresenceStatus.Unsupported,
                false,
                "In-place семейства не открываются и не изменяются автоматически.");
        }

        if (!family.IsEditable)
        {
            return new ProjectFamilyPresence(
                familyId,
                family.Name,
                categoryName,
                FamilyPresenceStatus.Unsupported,
                false,
                "Семейство недоступно для EditFamily.");
        }

        Document? familyDocument = null;
        try
        {
            familyDocument = projectDocument.EditFamily(family);
            FamilyParameter? parameter = versionAdapter.FindFamilyParameter(familyDocument.FamilyManager, targetGuid);
            return new ProjectFamilyPresence(
                familyId,
                family.Name,
                categoryName,
                parameter is null ? FamilyPresenceStatus.NotFound : FamilyPresenceStatus.Found,
                parameter is not null,
                null);
        }
        catch (Exception exception)
        {
            logger.Warning($"Failed to inspect family presence for '{family.Name}': {exception.Message}");
            return new ProjectFamilyPresence(
                familyId,
                family.Name,
                categoryName,
                FamilyPresenceStatus.CannotOpen,
                false,
                exception.Message);
        }
        finally
        {
            if (familyDocument is not null && familyDocument.IsValidObject)
            {
                familyDocument.Close(false);
            }
        }
    }

    public FamilyParameterUsageReport AnalyzeProjectFamily(
        Document projectDocument,
        long familyId,
        Guid targetGuid)
    {
        Guard.NotNull(projectDocument, nameof(projectDocument));
        if (projectDocument.GetElement(RevitElementIds.Create(familyId)) is not Family family)
        {
            return CreateFailedReport(
                new FamilyDocumentDescriptor(
                    familyId.ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    "Неизвестно",
                    FamilySourceKind.ActiveProject,
                    familyId),
                targetGuid,
                "Семейство не найдено в активном проекте.");
        }

        FamilyDocumentDescriptor descriptor = new(
            family.Name,
            string.Empty,
            family.FamilyCategory?.Name ?? "Без категории",
            FamilySourceKind.ActiveProject,
            familyId);
        if (family.IsInPlace || !family.IsEditable)
        {
            return CreateFailedReport(
                descriptor,
                targetGuid,
                family.IsInPlace
                    ? "In-place семейство не поддерживает автоматический глубокий анализ."
                    : "Семейство недоступно для EditFamily.");
        }

        Document? familyDocument = null;
        try
        {
            familyDocument = projectDocument.EditFamily(family);
            return AnalyzeOpenFamily(familyDocument, descriptor, targetGuid);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to deep-analyze project family '{family.Name}'.", exception);
            return CreateFailedReport(descriptor, targetGuid, exception.Message);
        }
        finally
        {
            if (familyDocument is not null && familyDocument.IsValidObject)
            {
                familyDocument.Close(false);
            }
        }
    }

    public FamilyParameterUsageReport AnalyzeExternalFamily(
        Application application,
        string filePath,
        Guid targetGuid)
    {
        Guard.NotNull(application, nameof(application));
        Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
        string fullPath = Path.GetFullPath(filePath);
        FamilyDocumentDescriptor descriptor = new(
            Path.GetFileNameWithoutExtension(fullPath),
            fullPath,
            "Не определена",
            FamilySourceKind.SelectedFiles,
            null);
        if (!File.Exists(fullPath))
        {
            return CreateFailedReport(descriptor, targetGuid, "RFA-файл не найден.");
        }

        Document? familyDocument = null;
        try
        {
            familyDocument = application.OpenDocumentFile(fullPath);
            if (!familyDocument.IsFamilyDocument)
            {
                return CreateFailedReport(descriptor, targetGuid, "Открытый документ не является семейством.");
            }

            descriptor = descriptor with
            {
                Name = familyDocument.Title,
                CategoryName = familyDocument.OwnerFamily?.FamilyCategory?.Name ?? "Без категории"
            };
            return AnalyzeOpenFamily(familyDocument, descriptor, targetGuid);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to deep-analyze external family '{fullPath}'.", exception);
            return CreateFailedReport(descriptor, targetGuid, exception.Message);
        }
        finally
        {
            if (familyDocument is not null && familyDocument.IsValidObject)
            {
                familyDocument.Close(false);
            }
        }
    }

    public FamilySharedParameterCatalogScan ScanExternalFamilyCatalog(
        Application application,
        string filePath,
        FamilySourceKind sourceKind)
    {
        Guard.NotNull(application, nameof(application));
        Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));
        string fullPath = Path.GetFullPath(filePath);
        FamilyDocumentDescriptor descriptor = new(
            Path.GetFileNameWithoutExtension(fullPath),
            fullPath,
            "Не определена",
            sourceKind,
            null);
        if (!File.Exists(fullPath))
        {
            return new FamilySharedParameterCatalogScan(
                descriptor,
                [],
                [new AnalysisError("Предварительный просмотр GUID", "RFA-файл не найден.", fullPath)]);
        }

        Document? familyDocument = null;
        try
        {
            familyDocument = application.OpenDocumentFile(fullPath);
            if (!familyDocument.IsFamilyDocument)
            {
                return new FamilySharedParameterCatalogScan(
                    descriptor,
                    [],
                    [new AnalysisError(
                        "Предварительный просмотр GUID",
                        "Открытый документ не является семейством.",
                        fullPath)]);
            }

            descriptor = descriptor with
            {
                Name = familyDocument.Title,
                CategoryName = familyDocument.OwnerFamily?.FamilyCategory?.Name ?? "Без категории"
            };
            return new FamilySharedParameterCatalogScan(
                descriptor,
                CollectSharedParameters(familyDocument),
                []);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to scan shared parameter catalog in external family '{fullPath}'.", exception);
            return new FamilySharedParameterCatalogScan(
                descriptor,
                [],
                [new AnalysisError("Предварительный просмотр GUID", exception.Message, fullPath)]);
        }
        finally
        {
            if (familyDocument is not null && familyDocument.IsValidObject)
            {
                familyDocument.Close(false);
            }
        }
    }

    public FamilyParameterUsageReport AnalyzeCurrentFamily(
        Document familyDocument,
        Guid targetGuid)
    {
        Guard.NotNull(familyDocument, nameof(familyDocument));
        if (!familyDocument.IsFamilyDocument)
        {
            return CreateFailedReport(
                new FamilyDocumentDescriptor(
                    familyDocument.Title,
                    familyDocument.PathName ?? string.Empty,
                    "Не применимо",
                    FamilySourceKind.CurrentFamily,
                    null),
                targetGuid,
                "Текущий документ не является семейством.");
        }

        return AnalyzeOpenFamily(
            familyDocument,
            new FamilyDocumentDescriptor(
                familyDocument.Title,
                familyDocument.PathName ?? string.Empty,
                familyDocument.OwnerFamily?.FamilyCategory?.Name ?? "Без категории",
                FamilySourceKind.CurrentFamily,
                null),
            targetGuid);
    }

    public IReadOnlyList<SharedParameterDescriptor> CollectSharedParameters(Document familyDocument)
    {
        Guard.NotNull(familyDocument, nameof(familyDocument));
        if (!familyDocument.IsFamilyDocument)
        {
            return [];
        }

        FamilyManager manager = familyDocument.FamilyManager;
        return manager.Parameters
            .Cast<FamilyParameter>()
            .Where(parameter => parameter.IsShared)
            .Select(parameter => new SharedParameterDescriptor(
                RevitElementIds.GetValue(parameter.Id),
                parameter.GUID,
                parameter.Definition.Name,
                versionAdapter.GetFamilyParameterDataTypeName(parameter),
                versionAdapter.GetFamilyParameterGroupName(parameter),
                parameter.IsInstance ? SharedParameterBindingKind.Instance : SharedParameterBindingKind.Type,
                [],
                false,
                false))
            .OrderBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(parameter => parameter.Guid)
            .ToList();
    }

    private FamilyParameterUsageReport AnalyzeOpenFamily(
        Document familyDocument,
        FamilyDocumentDescriptor descriptor,
        Guid targetGuid)
    {
        FamilyManager manager = familyDocument.FamilyManager;
        FamilyParameter? target = versionAdapter.FindFamilyParameter(manager, targetGuid);
        if (target is null)
        {
            return new FamilyParameterUsageReport(
                descriptor,
                targetGuid,
                false,
                null,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
        }

        List<AnalysisError> errors = [];
        IReadOnlyList<FamilyTypeValueUsage> values = CollectTypeValues(manager, target, errors);
        IReadOnlyList<FormulaUsage> formulas = CollectFormulas(manager, target);
        IReadOnlyList<DimensionUsage> dimensions = CollectDimensions(familyDocument, target, errors);
        IReadOnlyList<AssociatedParameterUsage> associations = CollectAssociations(target);
        IReadOnlyList<NestedFamilyUsage> nestedFamilies = CollectNestedFamilyUsages(
            familyDocument,
            manager,
            target,
            errors);
        IReadOnlyList<AnnotationUsage> annotations = CollectAnnotationUsages(
            familyDocument,
            descriptor,
            target);
        List<DeletionBlocker> blockers = BuildBlockers(
            descriptor,
            formulas,
            dimensions,
            associations,
            annotations,
            errors);
        FamilyParameterDescriptor parameterDescriptor = new(
            target.Definition.Name,
            target.GUID,
            versionAdapter.GetFamilyParameterDataTypeName(target),
            versionAdapter.GetFamilyParameterGroupName(target),
            target.IsShared,
            target.IsInstance,
            target.IsReporting,
            target.Formula ?? string.Empty,
            values.Count,
            values.Count(value => value.HasValue),
            values.Count(value => !value.HasValue));

        return new FamilyParameterUsageReport(
            descriptor,
            targetGuid,
            true,
            parameterDescriptor,
            values,
            formulas,
            dimensions,
            associations,
            nestedFamilies,
            annotations,
            blockers,
            errors);
    }

    private static IReadOnlyList<FamilyTypeValueUsage> CollectTypeValues(
        FamilyManager manager,
        FamilyParameter target,
        ICollection<AnalysisError> errors)
    {
        List<FamilyTypeValueUsage> values = [];
        foreach (FamilyType familyType in manager.Types.Cast<FamilyType>())
        {
            try
            {
                bool hasValue = familyType.HasValue(target);
                values.Add(new FamilyTypeValueUsage(
                    familyType.Name,
                    hasValue,
                    hasValue ? familyType.AsValueString(target) ?? string.Empty : string.Empty,
                    hasValue ? GetInternalValue(familyType, target) : string.Empty,
                    !string.IsNullOrWhiteSpace(target.Formula)));
            }
            catch (Exception exception)
            {
                errors.Add(new AnalysisError(
                    "Значения типов",
                    exception.Message,
                    familyType.Name));
            }
        }

        return values;
    }

    private IReadOnlyList<FormulaUsage> CollectFormulas(
        FamilyManager manager,
        FamilyParameter target)
    {
        List<FormulaUsage> formulas = [];
        if (!string.IsNullOrWhiteSpace(target.Formula))
        {
            formulas.Add(new FormulaUsage(
                target.Definition.Name,
                target.Formula,
                true,
                DetectionConfidence.Exact));
        }

        foreach (FamilyParameter parameter in manager.Parameters.Cast<FamilyParameter>())
        {
            if (parameter.Id == target.Id || string.IsNullOrWhiteSpace(parameter.Formula))
            {
                continue;
            }

            DetectionConfidence confidence = formulaParser.ClassifyReference(
                parameter.Formula,
                target.Definition.Name);
            if (confidence is DetectionConfidence.Exact or DetectionConfidence.Probable)
            {
                formulas.Add(new FormulaUsage(
                    parameter.Definition.Name,
                    parameter.Formula,
                    false,
                    confidence));
            }
        }

        return formulas;
    }

    private static IReadOnlyList<DimensionUsage> CollectDimensions(
        Document familyDocument,
        FamilyParameter target,
        ICollection<AnalysisError> errors)
    {
        List<DimensionUsage> usages = [];
        foreach (Dimension dimension in new FilteredElementCollector(familyDocument)
                     .OfClass(typeof(Dimension))
                     .Cast<Dimension>())
        {
            try
            {
                FamilyParameter? label = dimension.FamilyLabel;
                if (label is null || label.Id != target.Id)
                {
                    continue;
                }

                usages.Add(new DimensionUsage(
                    RevitElementIds.GetValue(dimension.Id),
                    dimension.View?.Name ?? string.Empty,
                    dimension.NumberOfSegments,
                    target.IsReporting,
                    dimension.ValueString ?? string.Empty,
                    DetectionConfidence.Exact));
            }
            catch (Exception exception)
            {
                errors.Add(new AnalysisError(
                    "Размеры",
                    exception.Message,
                    RevitElementIds.GetValue(dimension.Id).ToString(CultureInfo.InvariantCulture)));
            }
        }

        return usages;
    }

    private static IReadOnlyList<AssociatedParameterUsage> CollectAssociations(
        FamilyParameter target)
    {
        List<AssociatedParameterUsage> usages = [];
        foreach (Parameter parameter in target.AssociatedParameters.Cast<Parameter>())
        {
            Element element = parameter.Element;
            usages.Add(new AssociatedParameterUsage(
                RevitElementIds.GetValue(element.Id),
                element.Name ?? string.Empty,
                element.Category?.Name ?? "Без категории",
                parameter.Definition.Name,
                element.GetType().Name,
                "FamilyParameter → ElementParameter",
                DetectionConfidence.Exact));
        }

        return usages;
    }

    private static IReadOnlyList<NestedFamilyUsage> CollectNestedFamilyUsages(
        Document familyDocument,
        FamilyManager manager,
        FamilyParameter target,
        ICollection<AnalysisError> errors)
    {
        List<NestedFamilyUsage> usages = [];
        foreach (FamilyInstance instance in new FilteredElementCollector(familyDocument)
                     .OfClass(typeof(FamilyInstance))
                     .Cast<FamilyInstance>())
        {
            try
            {
                foreach (Parameter parameter in instance.GetOrderedParameters())
                {
                    FamilyParameter? associated = manager.GetAssociatedFamilyParameter(parameter);
                    if (associated?.Id == target.Id)
                    {
                        usages.Add(new NestedFamilyUsage(
                            RevitElementIds.GetValue(instance.Id),
                            instance.Symbol?.FamilyName ?? string.Empty,
                            instance.Symbol?.Name ?? string.Empty,
                            parameter.Definition.Name,
                            "Экземпляр",
                            1,
                            DetectionConfidence.Exact));
                    }
                }

                if (instance.Symbol is null)
                {
                    continue;
                }

                foreach (Parameter parameter in instance.Symbol.GetOrderedParameters())
                {
                    FamilyParameter? associated = manager.GetAssociatedFamilyParameter(parameter);
                    if (associated?.Id == target.Id)
                    {
                        usages.Add(new NestedFamilyUsage(
                            RevitElementIds.GetValue(instance.Id),
                            instance.Symbol.FamilyName,
                            instance.Symbol.Name,
                            parameter.Definition.Name,
                            "Тип",
                            1,
                            DetectionConfidence.Exact));
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add(new AnalysisError(
                    "Вложенные семейства",
                    exception.Message,
                    RevitElementIds.GetValue(instance.Id).ToString(CultureInfo.InvariantCulture)));
            }
        }

        return usages
            .GroupBy(usage => (usage.ElementId, usage.ParameterName, usage.AssociationKind))
            .Select(group => group.First())
            .ToList();
    }

    private static IReadOnlyList<AnnotationUsage> CollectAnnotationUsages(
        Document familyDocument,
        FamilyDocumentDescriptor descriptor,
        FamilyParameter target)
    {
        Category? category = familyDocument.OwnerFamily?.FamilyCategory;
        if (category?.CategoryType != CategoryType.Annotation)
        {
            return [];
        }

        return
        [
            new AnnotationUsage(
                descriptor.Name,
                descriptor.CategoryName,
                DetectionConfidence.ManualCheckRequired,
                $"Параметр «{target.Definition.Name}» присутствует в семействе аннотации. "
                + "Точное использование в составе текстовой метки не подтверждено публичным API этой версии Revit.")
        ];
    }

    private static List<DeletionBlocker> BuildBlockers(
        FamilyDocumentDescriptor descriptor,
        IReadOnlyList<FormulaUsage> formulas,
        IReadOnlyList<DimensionUsage> dimensions,
        IReadOnlyList<AssociatedParameterUsage> associations,
        IReadOnlyList<AnnotationUsage> annotations,
        IReadOnlyList<AnalysisError> errors)
    {
        List<DeletionBlocker> blockers = [];
        blockers.AddRange(formulas.Select(formula => new DeletionBlocker(
            formula.IsTargetFormula ? "FAMILY_TARGET_FORMULA" : "FAMILY_DEPENDENT_FORMULA",
            $"Семейство «{descriptor.Name}»: формула параметра «{formula.ParameterName}» — {formula.Formula}",
            "FamilyFormula",
            descriptor.ProjectFamilyId,
            formula.Confidence)));
        blockers.AddRange(dimensions.Select(dimension => new DeletionBlocker(
            "FAMILY_DIMENSION_LABEL",
            $"Семейство «{descriptor.Name}»: параметр подписывает размер {dimension.DimensionId}.",
            "Dimension",
            dimension.DimensionId,
            dimension.Confidence)));
        blockers.AddRange(associations.Select(association => new DeletionBlocker(
            "FAMILY_PARAMETER_ASSOCIATION",
            $"Семейство «{descriptor.Name}»: ассоциация с «{association.ParameterName}» элемента {association.ElementId}.",
            "FamilyParameterAssociation",
            association.ElementId,
            association.Confidence)));
        blockers.AddRange(annotations.Select(annotation => new DeletionBlocker(
            "ANNOTATION_MANUAL_CHECK_REQUIRED",
            annotation.Message,
            "AnnotationFamily",
            descriptor.ProjectFamilyId,
            annotation.Confidence)));
        blockers.AddRange(errors.Select(error => new DeletionBlocker(
            "FAMILY_ANALYSIS_ERROR",
            $"Семейство «{descriptor.Name}»: {error.Phase}: {error.Message}",
            "Family",
            descriptor.ProjectFamilyId,
            DetectionConfidence.ManualCheckRequired)));
        return blockers;
    }

    private static string GetInternalValue(FamilyType familyType, FamilyParameter parameter)
    {
        return parameter.StorageType switch
        {
            StorageType.String => familyType.AsString(parameter) ?? string.Empty,
            StorageType.Integer => familyType.AsInteger(parameter)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            StorageType.Double => familyType.AsDouble(parameter)?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            StorageType.ElementId => familyType.AsElementId(parameter) is ElementId elementId
                ? RevitElementIds.GetValue(elementId).ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            _ => string.Empty
        };
    }

    private static FamilyParameterUsageReport CreateFailedReport(
        FamilyDocumentDescriptor descriptor,
        Guid targetGuid,
        string message)
    {
        AnalysisError error = new("Открытие семейства", message, descriptor.Path);
        return new FamilyParameterUsageReport(
            descriptor,
            targetGuid,
            false,
            null,
            [],
            [],
            [],
            [],
            [],
            [],
            [new DeletionBlocker(
                "FAMILY_NOT_VERIFIED",
                message,
                "Family",
                descriptor.ProjectFamilyId,
                DetectionConfidence.ManualCheckRequired)],
            [error]);
    }
}
