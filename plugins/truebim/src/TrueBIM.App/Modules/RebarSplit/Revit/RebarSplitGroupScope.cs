using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Сопоставляет выбранный набор с тем же членом всех экземпляров типа группы.</summary>
internal sealed class RebarSplitGroupScope
{
    private readonly Dictionary<ElementId, IReadOnlyList<ElementId>> corresponding = new();
    private readonly Dictionary<ElementId, ElementId> rootGroups = new();

    internal RebarSplitGroupScope(Document document, IEnumerable<ElementId> selected)
    {
        foreach (ElementId id in selected.Distinct())
        {
            if (corresponding.ContainsKey(id) || !RebarSplitGeometry.IsSupportedSet(document.GetElement(id))) continue;
            Element source = document.GetElement(id);
            if (document.GetElement(source.GroupId) is not Group group)
            {
                corresponding.Add(id, new[] { id });
                continue;
            }
            IList<ElementId> members = group.GetMemberIds();
            int index = members.IndexOf(id);
            List<ElementId> matches = new();
            foreach (Group instance in group.GroupType.Groups.Cast<Group>())
            {
                IList<ElementId> instanceMembers = instance.GetMemberIds();
                // Revit documents this order as the correspondence between group instances.
                // Do not guess when an instance has excluded or otherwise incompatible members.
                if (index < 0 || instanceMembers.Count != members.Count
                    || !RebarSplitGeometry.IsSupportedSet(document.GetElement(instanceMembers[index])))
                    throw new InvalidOperationException($"Не удалось сопоставить набор {id} с экземпляром группы {instance.Id}. "
                        + "Состав экземпляров различается. Операция отменена без изменений.");
                matches.Add(instanceMembers[index]);
            }
            if (!matches.Contains(id))
                throw new InvalidOperationException($"Набор {id} не найден среди экземпляров своего типа группы.");
            foreach (ElementId match in matches) corresponding[match] = matches;
        }
        SourceIds = corresponding.Keys.ToList();
        foreach (ElementId id in SourceIds)
        {
            if (document.GetElement(document.GetElement(id).GroupId) is not Group group) continue;
            while (document.GetElement(group.GroupId) is Group parent) group = parent;
            rootGroups[id] = group.Id;
        }
    }

    internal IReadOnlyList<ElementId> SourceIds { get; }

    internal IEnumerable<ElementId> Expand(IEnumerable<ElementId> ids)
    {
        HashSet<ElementId> skipped = new(ids);
        int previousCount;
        do
        {
            previousCount = skipped.Count;
            foreach (ElementId id in skipped.ToList())
                if (corresponding.TryGetValue(id, out IReadOnlyList<ElementId>? matches)) skipped.UnionWith(matches);
            HashSet<ElementId> roots = new(skipped.Where(rootGroups.ContainsKey).Select(id => rootGroups[id]));
            skipped.UnionWith(rootGroups.Where(pair => roots.Contains(pair.Value)).Select(pair => pair.Key));
        } while (skipped.Count != previousCount);
        // Если один выбранный набор пропущен, не разгруппировываем его ради другого набора.
        return skipped;
    }
}
