using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class ProjectParameterBindingService
{
    private readonly SharedParameterFileService sharedParameterFileService;
    private readonly CategoryResolveService categoryResolveService;
    private readonly ITrueBimLogger logger;

    public ProjectParameterBindingService(
        SharedParameterFileService sharedParameterFileService,
        CategoryResolveService categoryResolveService,
        ITrueBimLogger logger)
    {
        this.sharedParameterFileService = sharedParameterFileService ?? throw new ArgumentNullException(nameof(sharedParameterFileService));
        this.categoryResolveService = categoryResolveService ?? throw new ArgumentNullException(nameof(categoryResolveService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ISet<string> CollectExistingProjectParameterNames(Document document)
    {
        return CollectProjectParameters(document)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    public IReadOnlyList<ProjectParameterRow> CollectProjectParameters(Document document)
    {
        List<ProjectParameterRow> rows = [];
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            Definition definition = iterator.Key;
            Binding binding = (Binding)iterator.Current;
            ElementBinding? elementBinding = binding as ElementBinding;
            string categories = elementBinding is null
                ? string.Empty
                : string.Join(", ", elementBinding.Categories.Cast<Category>().Select(category => category.Name).OrderBy(name => name));
            bool isShared = definition is ExternalDefinition;
            string guid = definition is ExternalDefinition externalDefinition
                ? externalDefinition.GUID.ToString("D")
                : string.Empty;
            rows.Add(new ProjectParameterRow(
                definition.Name,
                GetDefinitionDataTypeDisplay(definition),
                binding is TypeBinding ? "Type" : "Instance",
                categories,
                GetDefinitionGroupDisplay(definition),
                isShared,
                guid));
        }

        return rows.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ParameterImportResult Import(
        Application application,
        Document document,
        string sharedParameterPath,
        IReadOnlyList<ParameterImportRow> rows)
    {
        DefinitionFile definitionFile = sharedParameterFileService.Open(application, sharedParameterPath);
        List<ParameterImportRow> resultRows = rows.ToList();

        using Transaction transaction = new(document, "TrueBIM: импорт параметров проекта");
        transaction.Start();
        foreach (ParameterImportRow row in resultRows)
        {
            if (!row.CanApply)
            {
                row.Status = ParameterImportStatus.Skipped;
                if (string.IsNullOrWhiteSpace(row.Message))
                {
                    row.Message = "Строка не прошла предварительную проверку.";
                }

                continue;
            }

            try
            {
                ApplyRow(application, document, definitionFile, row);
            }
            catch (Exception exception)
            {
                row.Status = ParameterImportStatus.Failed;
                row.Message = exception.Message;
                logger.Error($"Failed to import parameter '{row.ParameterName}'.", exception);
            }
        }

        transaction.Commit();

        ParameterImportResult result = new(resultRows);
        logger.Info($"ParaManager import completed. Created={result.CreatedCount}, Updated={result.UpdatedCount}, Failed={result.FailedCount}.");
        return result;
    }

    private void ApplyRow(Application application, Document document, DefinitionFile definitionFile, ParameterImportRow row)
    {
        IReadOnlyList<Category> categories = categoryResolveService.ResolveCategories(document, row.CategoryNames, out IReadOnlyList<string> missing);
        if (missing.Count > 0 || categories.Count == 0)
        {
            throw new InvalidOperationException($"Категории не найдены: {string.Join(", ", missing)}.");
        }

        row.TryGetBindingKind(out ParameterBindingKind bindingKind);
        ExternalDefinition definition = sharedParameterFileService.GetOrCreateDefinition(definitionFile, row, out _);
        ExistingBinding? existingBinding = FindExistingBindingByName(document, row.ParameterName);
        CategorySet categorySet = CreateCategorySet(application, categories, existingBinding?.Categories);
        Binding binding = bindingKind == ParameterBindingKind.Type
            ? application.Create.NewTypeBinding(categorySet)
            : application.Create.NewInstanceBinding(categorySet);

        if (existingBinding is not null)
        {
            if (existingBinding.BindingKind != bindingKind)
            {
                throw new InvalidOperationException($"Параметр уже привязан как {existingBinding.BindingKind}, а в CSV указан {bindingKind}.");
            }

            bool updated = ReInsertBinding(document, existingBinding.Definition, binding, row.GroupUnder);
            row.Status = updated ? ParameterImportStatus.Updated : ParameterImportStatus.Failed;
            row.Message = updated
                ? "Привязка параметра обновлена."
                : "Revit не обновил привязку параметра.";
            return;
        }

        bool inserted = InsertBinding(document, definition, binding, row.GroupUnder);
        row.Status = inserted ? ParameterImportStatus.Created : ParameterImportStatus.Failed;
        row.Message = inserted
            ? "Параметр создан и привязан к проекту."
            : "Revit не добавил привязку параметра.";
    }

    private static ExistingBinding? FindExistingBindingByName(Document document, string parameterName)
    {
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            Definition definition = iterator.Key;
            if (!string.Equals(definition.Name, parameterName, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            Binding binding = (Binding)iterator.Current;
            ElementBinding? elementBinding = binding as ElementBinding;
            IReadOnlyList<Category> categories = elementBinding is null
                ? []
                : elementBinding.Categories.Cast<Category>().ToList();
            return new ExistingBinding(
                definition,
                binding is TypeBinding ? ParameterBindingKind.Type : ParameterBindingKind.Instance,
                categories);
        }

        return null;
    }

    private static CategorySet CreateCategorySet(Application application, IReadOnlyList<Category> categories, IReadOnlyList<Category>? existingCategories)
    {
        CategorySet categorySet = application.Create.NewCategorySet();
        foreach (Category category in (existingCategories ?? []).Concat(categories))
        {
            if (!categorySet.Contains(category))
            {
                categorySet.Insert(category);
            }
        }

        return categorySet;
    }

    private static bool InsertBinding(Document document, Definition definition, Binding binding, string groupUnder)
    {
#if REVIT2022_OR_GREATER
        return document.ParameterBindings.Insert(definition, binding, ParameterGroupResolver.ResolveForgeTypeId(groupUnder));
#else
        return document.ParameterBindings.Insert(definition, binding, ParameterGroupResolver.ResolveBuiltInParameterGroup(groupUnder));
#endif
    }

    private static bool ReInsertBinding(Document document, Definition definition, Binding binding, string groupUnder)
    {
#if REVIT2022_OR_GREATER
        return document.ParameterBindings.ReInsert(definition, binding, ParameterGroupResolver.ResolveForgeTypeId(groupUnder));
#else
        return document.ParameterBindings.ReInsert(definition, binding, ParameterGroupResolver.ResolveBuiltInParameterGroup(groupUnder));
#endif
    }

    private static string GetDefinitionGroupDisplay(Definition definition)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId groupTypeId = definition.GetGroupTypeId();
        if (groupTypeId == GroupTypeId.IdentityData)
        {
            return "Identity Data";
        }

        if (groupTypeId == GroupTypeId.Text)
        {
            return "Text";
        }

        if (groupTypeId == GroupTypeId.Geometry)
        {
            return "Dimensions";
        }

        if (groupTypeId == GroupTypeId.General)
        {
            return "General";
        }

        if (groupTypeId == GroupTypeId.Data)
        {
            return "Data";
        }

        return groupTypeId.TypeId;
#else
        return definition.ParameterGroup.ToString();
#endif
    }

    private static string GetDefinitionDataTypeDisplay(Definition definition)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId dataType = definition.GetDataType();
        if (dataType == SpecTypeId.String.Text)
        {
            return "Text";
        }

        if (dataType == SpecTypeId.Int.Integer)
        {
            return "Integer";
        }

        if (dataType == SpecTypeId.Boolean.YesNo)
        {
            return "YesNo";
        }

        if (dataType == SpecTypeId.Number)
        {
            return "Number";
        }

        if (dataType == SpecTypeId.Length)
        {
            return "Length";
        }

        if (dataType == SpecTypeId.Area)
        {
            return "Area";
        }

        if (dataType == SpecTypeId.Volume)
        {
            return "Volume";
        }

        return dataType.TypeId;
#else
        return definition.ParameterType.ToString();
#endif
    }

    private sealed record ExistingBinding(
        Definition Definition,
        ParameterBindingKind BindingKind,
        IReadOnlyList<Category> Categories);
}
