using System.IO;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed record FinishScheduleDefaultParameterResult(
    ParameterCatalog Catalog,
    int CreatedCount,
    int UpdatedCount,
    int ExistingCount,
    IReadOnlyList<string> Warnings);

public sealed class FinishScheduleDefaultParameterService
{
    private const string SharedParameterGroup = "TrueBIM — Ведомость отделки";

    private static readonly DefaultParameterSpec[] Parameters =
    [
        Type(
            FinishSchedulePreferredParameterNames.Description,
            "3e1d954c-6ce2-4da3-a255-a7fe229178f4",
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings),
        Instance(
            FinishSchedulePreferredParameterNames.RoomListOutput,
            "598772c4-4d97-4aac-bf83-3f75db857eb1",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.WallsDescription,
            "0caa9468-3c61-4e92-89c4-f23272f29b91",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.WallsArea,
            "d4e5be11-bf07-4245-8463-9f6368e32f06",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.FloorsDescription,
            "b3ce8d43-8b77-4e54-8f1c-c7d1016fe641",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.FloorsArea,
            "374fd552-bfca-4bf1-95a8-34ae6b353c14",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.CeilingsDescription,
            "ac487c01-1b25-4444-900c-f2f10f275199",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.CeilingsArea,
            "99bd220f-4653-4497-84ed-a1b76554c3c5",
            BuiltInCategory.OST_Rooms),
        Instance(
            FinishSchedulePreferredParameterNames.WallsOwnership,
            "cde534e8-799c-479d-8bbf-2dd984745b25",
            BuiltInCategory.OST_Walls),
        Instance(
            FinishSchedulePreferredParameterNames.FloorsOwnership,
            "3cb64b77-c0c9-40b7-abda-d26fa9fb8f3f",
            BuiltInCategory.OST_Floors),
        Instance(
            FinishSchedulePreferredParameterNames.CeilingsOwnership,
            "1529cb1b-f675-4e30-b191-1d4e3e6f31fc",
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings)
    ];

    private readonly ITrueBimLogger logger;

    public FinishScheduleDefaultParameterService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishScheduleDefaultParameterResult CreateOrUpdate(
        Application application,
        Document document)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string sharedParameterPath = EnsureSharedParameterFile();
        string previousPath = application.SharedParametersFilename ?? string.Empty;
        int created = 0;
        int updated = 0;
        int existing = 0;
        List<string> warnings = [];
        try
        {
            application.SharedParametersFilename = sharedParameterPath;
            DefinitionFile definitionFile = application.OpenSharedParameterFile()
                ?? throw new InvalidOperationException("Revit не открыл служебный файл общих параметров TrueBIM.");
            DefinitionGroup definitionGroup = GetOrCreateGroup(definitionFile);

            using Transaction transaction = new(document, "TrueBIM: добавить параметры ведомости отделки");
            FinishTransactionStatus.EnsureStarted(transaction);
            try
            {
                foreach (DefaultParameterSpec spec in Parameters)
                {
                    try
                    {
                        ParameterUpdateStatus status = EnsureParameter(
                            application,
                            document,
                            definitionGroup,
                            spec,
                            out string? warning);
                        created += status == ParameterUpdateStatus.Created ? 1 : 0;
                        updated += status == ParameterUpdateStatus.Updated ? 1 : 0;
                        existing += status == ParameterUpdateStatus.Existing ? 1 : 0;
                        if (!string.IsNullOrWhiteSpace(warning))
                        {
                            warnings.Add(warning!);
                        }
                    }
                    catch (Exception exception)
                    {
                        warnings.Add($"«{spec.Name}»: {exception.Message}");
                        logger.Error($"Failed to create default Finish Schedule parameter '{spec.Name}'.", exception);
                    }
                }

                FinishTransactionStatus.EnsureCommitted(transaction);
            }
            catch
            {
                FinishTransactionStatus.RollBackIfStarted(transaction);
                throw;
            }
        }
        finally
        {
            application.SharedParametersFilename = previousPath;
        }

        ParameterCatalog catalog = new ParameterCatalogService(logger).Collect(document);
        logger.Info(
            $"Finish Schedule default parameters processed. Created={created}; Updated={updated}; Existing={existing}; Warnings={warnings.Count}.");
        return new FinishScheduleDefaultParameterResult(catalog, created, updated, existing, warnings);
    }

    private static ParameterUpdateStatus EnsureParameter(
        Application application,
        Document document,
        DefinitionGroup definitionGroup,
        DefaultParameterSpec spec,
        out string? warning)
    {
        warning = null;
        ExistingBinding? existing = FindExistingBinding(document, spec.Name);
        if (existing is not null)
        {
            if (existing.BindingKind != spec.BindingKind)
            {
                warning = $"«{spec.Name}» уже привязан как {existing.BindingKind}; ожидалась привязка {spec.BindingKind}.";
                return ParameterUpdateStatus.Existing;
            }

            if (!IsText(existing.Definition))
            {
                warning = $"«{spec.Name}» уже существует, но не является текстовым параметром.";
                return ParameterUpdateStatus.Existing;
            }

            CategorySet merged = CreateCategorySet(application, document, spec.Categories, existing.Categories);
            if (ContainsAll(existing.Categories, document, spec.Categories))
            {
                return ParameterUpdateStatus.Existing;
            }

            Binding updatedBinding = spec.BindingKind == ParameterBindingKind.Type
                ? application.Create.NewTypeBinding(merged)
                : application.Create.NewInstanceBinding(merged);
            bool reinserted = ReInsert(document, existing.Definition, updatedBinding);
            if (!reinserted)
            {
                throw new InvalidOperationException("Revit не обновил привязку к требуемым категориям.");
            }

            return ParameterUpdateStatus.Updated;
        }

        ExternalDefinition definition = FindDefinition(definitionGroup, spec.Guid)
            ?? CreateDefinition(definitionGroup, spec);
        CategorySet categories = CreateCategorySet(application, document, spec.Categories, []);
        Binding binding = spec.BindingKind == ParameterBindingKind.Type
            ? application.Create.NewTypeBinding(categories)
            : application.Create.NewInstanceBinding(categories);
        if (!Insert(document, definition, binding))
        {
            throw new InvalidOperationException("Revit не добавил привязку параметра к проекту.");
        }

        return ParameterUpdateStatus.Created;
    }

    private static string EnsureSharedParameterFile()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM",
            "BimTools",
            "finish-schedule");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "default-shared-parameters.txt");
        if (!File.Exists(path))
        {
            const string content = "# This is a Revit shared parameter file.\r\n"
                + "# Do not edit manually.\r\n"
                + "*META\tVERSION\tMINVERSION\r\n"
                + "META\t2\t1\r\n"
                + "*GROUP\tID\tNAME\r\n"
                + "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n";
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return path;
    }

    private static DefinitionGroup GetOrCreateGroup(DefinitionFile file)
    {
        foreach (DefinitionGroup group in file.Groups)
        {
            if (string.Equals(group.Name, SharedParameterGroup, StringComparison.OrdinalIgnoreCase))
            {
                return group;
            }
        }

        return file.Groups.Create(SharedParameterGroup);
    }

    private static ExternalDefinition CreateDefinition(
        DefinitionGroup group,
        DefaultParameterSpec spec)
    {
#if REVIT2022_OR_GREATER
        ExternalDefinitionCreationOptions options = new(spec.Name, SpecTypeId.String.Text);
#else
        ExternalDefinitionCreationOptions options = new(spec.Name, ParameterType.Text);
#endif
        options.GUID = spec.Guid;
        options.Description = "Параметр TrueBIM для ведомости отделки помещений.";
        options.Visible = true;
        options.UserModifiable = true;
        return (ExternalDefinition)group.Definitions.Create(options);
    }

    private static ExternalDefinition? FindDefinition(DefinitionGroup group, Guid guid)
    {
        foreach (Definition definition in group.Definitions)
        {
            if (definition is ExternalDefinition external && external.GUID == guid)
            {
                return external;
            }
        }

        return null;
    }

    private static ExistingBinding? FindExistingBinding(Document document, string name)
    {
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            Definition definition = iterator.Key;
            if (!string.Equals(definition.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Binding binding = (Binding)iterator.Current;
            IReadOnlyList<Category> categories = binding is ElementBinding elementBinding
                ? elementBinding.Categories.Cast<Category>().ToArray()
                : [];
            return new ExistingBinding(
                definition,
                binding is TypeBinding ? ParameterBindingKind.Type : ParameterBindingKind.Instance,
                categories);
        }

        return null;
    }

    private static bool ContainsAll(
        IEnumerable<Category> existing,
        Document document,
        IEnumerable<BuiltInCategory> required)
    {
        HashSet<long> ids = existing.Select(category => RevitElementIds.GetValue(category.Id)).ToHashSet();
        return required.All(category =>
        {
            Category? resolved = Category.GetCategory(document, category);
            return resolved is not null && ids.Contains(RevitElementIds.GetValue(resolved.Id));
        });
    }

    private static CategorySet CreateCategorySet(
        Application application,
        Document document,
        IEnumerable<BuiltInCategory> required,
        IEnumerable<Category> existing)
    {
        CategorySet result = application.Create.NewCategorySet();
        foreach (Category category in existing)
        {
            if (!result.Contains(category))
            {
                result.Insert(category);
            }
        }

        foreach (BuiltInCategory builtInCategory in required)
        {
            Category category = Category.GetCategory(document, builtInCategory)
                ?? throw new InvalidOperationException($"Категория {builtInCategory} недоступна в документе.");
            if (!result.Contains(category))
            {
                result.Insert(category);
            }
        }

        return result;
    }

    private static bool IsText(Definition definition)
    {
#if REVIT2022_OR_GREATER
        return definition.GetDataType() == SpecTypeId.String.Text;
#else
#pragma warning disable CS0618
        return definition.ParameterType == ParameterType.Text;
#pragma warning restore CS0618
#endif
    }

    private static bool Insert(Document document, Definition definition, Binding binding)
    {
#if REVIT2022_OR_GREATER
        return document.ParameterBindings.Insert(definition, binding, GroupTypeId.Data);
#else
        return document.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA);
#endif
    }

    private static bool ReInsert(Document document, Definition definition, Binding binding)
    {
#if REVIT2022_OR_GREATER
        return document.ParameterBindings.ReInsert(definition, binding, GroupTypeId.Data);
#else
        return document.ParameterBindings.ReInsert(definition, binding, BuiltInParameterGroup.PG_DATA);
#endif
    }

    private static DefaultParameterSpec Instance(
        string name,
        string guid,
        params BuiltInCategory[] categories)
    {
        return new DefaultParameterSpec(
            name,
            Guid.Parse(guid),
            ParameterBindingKind.Instance,
            categories);
    }

    private static DefaultParameterSpec Type(
        string name,
        string guid,
        params BuiltInCategory[] categories)
    {
        return new DefaultParameterSpec(
            name,
            Guid.Parse(guid),
            ParameterBindingKind.Type,
            categories);
    }

    private enum ParameterUpdateStatus
    {
        Created,
        Updated,
        Existing
    }

    private sealed record DefaultParameterSpec(
        string Name,
        Guid Guid,
        ParameterBindingKind BindingKind,
        IReadOnlyList<BuiltInCategory> Categories);

    private sealed record ExistingBinding(
        Definition Definition,
        ParameterBindingKind BindingKind,
        IReadOnlyList<Category> Categories);
}
