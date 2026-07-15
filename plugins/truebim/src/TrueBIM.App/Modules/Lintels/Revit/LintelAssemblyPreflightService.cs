using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelAssemblyPreflightService
{
    private readonly ITrueBimLogger logger;

    public LintelAssemblyPreflightService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelAssemblyPreflightResult Inspect(
        Document document,
        IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (selectedTypes is null)
        {
            throw new ArgumentNullException(nameof(selectedTypes));
        }

        HashSet<string> existingAssemblyNames = CollectExistingAssemblyNames(document);
        LintelAssemblyPreflightItem[] items = selectedTypes
            .OrderBy(type => type.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(type => type.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .Select(type => InspectType(document, type, existingAssemblyNames))
            .ToArray();
        LintelAssemblyPreflightResult result = new(items);
        logger.Info(
            $"Lintels assembly preflight completed. Types={items.Length}; Ready={result.ReadyCount}; Existing={result.ExistingCount}; Blocked={result.BlockedCount}.");
        return result;
    }

    private LintelAssemblyPreflightItem InspectType(
        Document document,
        LintelTypeDiagnostic type,
        ISet<string> existingAssemblyNames)
    {
        LintelArtifactPreview artifact = LintelArtifactNameBuilder.Build(type);
        IReadOnlyList<long> memberIds = type.RepresentativeAssemblyMemberIds;
        if (existingAssemblyNames.Contains(artifact.AssemblyName))
        {
            return CreateItem(
                type,
                artifact.AssemblyName,
                memberIds,
                null,
                LintelAssemblyPreflightStatus.AlreadyExists,
                "Сборка с таким TrueBIM-именем уже существует; повторный запуск должен пропустить её.");
        }

        if (memberIds.Count == 0)
        {
            return CreateBlocked(
                type,
                artifact.AssemblyName,
                memberIds,
                "У представителя нет вложенных компонентов с модельной геометрией.");
        }

        try
        {
            Element? representative = document.GetElement(RevitElementIds.Create(type.RepresentativeElementId));
            if (representative is not FamilyInstance representativeFamilyInstance)
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    "Представитель типоразмера удалён или больше не является экземпляром семейства. Обновите данные из Revit.");
            }

            long currentTypeId = RevitElementIds.GetValue(representativeFamilyInstance.GetTypeId());
            if (currentTypeId <= 0)
            {
                currentTypeId = type.RepresentativeElementId;
            }

            if (currentTypeId != type.TypeId)
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    "После диагностики у представителя изменился типоразмер. Обновите данные из Revit.");
            }

            HashSet<long> currentSubcomponentIds = CollectSubcomponentIds(
                document,
                representativeFamilyInstance);
            long[] detachedMemberIds = memberIds
                .Where(memberId => !currentSubcomponentIds.Contains(memberId))
                .ToArray();
            if (detachedMemberIds.Length > 0)
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    $"После диагностики изменился состав вложенных компонентов: {string.Join(", ", detachedMemberIds)}. Обновите данные из Revit.");
            }

            List<Element> members = [];
            List<long> missingIds = [];
            foreach (long memberId in memberIds)
            {
                Element? member = document.GetElement(RevitElementIds.Create(memberId));
                if (member is null)
                {
                    missingIds.Add(memberId);
                }
                else
                {
                    members.Add(member);
                }
            }

            if (missingIds.Count > 0)
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    $"После диагностики элементы были удалены или заменены: {string.Join(", ", missingIds)}. Обновите данные из Revit.");
            }

            List<ElementId> revitMemberIds = members.Select(member => member.Id).ToList();
            if (!AssemblyInstance.AreElementsValidForAssembly(
                    document,
                    revitMemberIds,
                    ElementId.InvalidElementId))
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    "Revit запретил состав сборки: проверьте категории компонентов и принадлежность другой сборке.");
            }

            ElementId? namingCategoryId = FindNamingCategory(document, members, revitMemberIds);
            if (namingCategoryId is null)
            {
                return CreateBlocked(
                    type,
                    artifact.AssemblyName,
                    memberIds,
                    "Revit не подтвердил ни одну категорию компонентов как категорию именования сборки.");
            }

            long namingCategoryValue = RevitElementIds.GetValue(namingCategoryId);
            return CreateItem(
                type,
                artifact.AssemblyName,
                memberIds,
                namingCategoryValue,
                LintelAssemblyPreflightStatus.Ready,
                "Состав и категория именования приняты Revit API. Транзакция не открывалась.");
        }
        catch (Exception exception)
        {
            logger.Error($"Lintels assembly preflight failed for TypeId {type.TypeId}.", exception);
            return CreateBlocked(
                type,
                artifact.AssemblyName,
                memberIds,
                "Не удалось проверить состав через Revit API; подробности записаны в лог.");
        }
    }

    private static ElementId? FindNamingCategory(
        Document document,
        IReadOnlyCollection<Element> members,
        ICollection<ElementId> memberIds)
    {
        return members
            .Where(member => member.Category is not null)
            .Select(member => member.Category!.Id)
            .GroupBy(RevitElementIds.GetValue)
            .OrderBy(group => group.Key)
            .Select(group => group.First())
            .FirstOrDefault(categoryId => AssemblyInstance.IsValidNamingCategory(
                document,
                categoryId,
                memberIds));
    }

    private static HashSet<long> CollectSubcomponentIds(Document document, FamilyInstance parent)
    {
        HashSet<long> result = [];
        Queue<ElementId> pending = new(parent.GetSubComponentIds());
        while (pending.Count > 0)
        {
            ElementId elementId = pending.Dequeue();
            long elementIdValue = RevitElementIds.GetValue(elementId);
            if (!result.Add(elementIdValue)
                || document.GetElement(elementId) is not FamilyInstance nestedFamilyInstance)
            {
                continue;
            }

            foreach (ElementId nestedId in nestedFamilyInstance.GetSubComponentIds())
            {
                pending.Enqueue(nestedId);
            }
        }

        return result;
    }

    private static HashSet<string> CollectExistingAssemblyNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(AssemblyInstance))
            .Cast<AssemblyInstance>()
            .Select(assembly => assembly.AssemblyTypeName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    private static LintelAssemblyPreflightItem CreateBlocked(
        LintelTypeDiagnostic type,
        string assemblyName,
        IReadOnlyList<long> memberIds,
        string message)
    {
        return CreateItem(
            type,
            assemblyName,
            memberIds,
            null,
            LintelAssemblyPreflightStatus.Blocked,
            message);
    }

    private static LintelAssemblyPreflightItem CreateItem(
        LintelTypeDiagnostic type,
        string assemblyName,
        IReadOnlyList<long> memberIds,
        long? namingCategoryId,
        LintelAssemblyPreflightStatus status,
        string message)
    {
        return new LintelAssemblyPreflightItem(
            type.TypeId,
            type.FamilyName,
            type.TypeName,
            assemblyName,
            type.RepresentativeElementId,
            memberIds,
            namingCategoryId,
            status,
            message);
    }
}
