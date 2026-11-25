using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.SystemDesignation;

internal sealed class DesignationPlan
{
    public const string ParameterName = "• Обозначение системы";
    private readonly List<(Parameter Target, string Value)> changes = [];
    public List<(ElementId Id, string Reason)> Skipped { get; } = [];
    public int Unchanged { get; private set; }
    public int Updated { get; private set; }
    public int Cleared { get; private set; }

    public static DesignationPlan Prepare(Document doc, Dictionary<ElementId, Element> sources, BuiltInParameter sourceParameter)
    {
        DesignationPlan plan = new();
        Dictionary<ElementId, string> cache = [];
        foreach (var entry in sources)
        {
            Element element = doc.GetElement(entry.Key);
            var matches = element.GetParameters(ParameterName).Where(p => p.IsShared).ToList();
            if (matches.Count != 1)
            {
                plan.Skipped.Add((element.Id, matches.Count == 0 ? "Отсутствует общий параметр" : "Несколько одноимённых общих параметров"));
                continue;
            }

            var target = matches[0];
            if (target.StorageType != StorageType.String)
            {
                plan.Skipped.Add((element.Id, "Параметр не текстовый"));
                continue;
            }
            if (!cache.TryGetValue(entry.Value.Id, out string? value))
                cache.Add(entry.Value.Id, value = SystemDesignationValues.Read(entry.Value, sourceParameter));

            if (string.Equals(target.AsString() ?? "", value, StringComparison.Ordinal))
                plan.Unchanged++;
            else if (target.IsReadOnly)
                plan.Skipped.Add((element.Id, "Параметр только для чтения"));
            else
                plan.changes.Add((target, value));
        }
        plan.CheckGroupWrites(doc);
        return plan;
    }

    // Revit can propagate a non-varying parameter to matching members of other groups.
    // Only allow this when all corresponding changes belong to the same plan.
    private void CheckGroupWrites(Document doc)
    {
        var intended = changes.ToDictionary(change => change.Target.Element.Id, change => change.Value);
        changes.RemoveAll(change =>
        {
            Element element = change.Target.Element;
            if (element.GroupId == ElementId.InvalidElementId ||
                change.Target.Definition is not InternalDefinition { VariesAcrossGroups: false } ||
                doc.GetElement(element.GroupId) is not Group group) return false;

            var members = group.GetMemberIds();
            int index = members.IndexOf(element.Id);
            foreach (Group peer in group.GroupType.Groups)
            {
                var other = peer.GetMemberIds();
                if (index < 0 || members.Count != other.Count ||
                    !intended.TryGetValue(other[index], out string? expected) || expected != change.Value)
                {
                    Skipped.Add((element.Id, "Общее значение группы затрагивает другой экземпляр группы"));
                    return true;
                }
            }
            return false;
        });
    }

    public void Apply(Document doc)
    {
        if (changes.Count == 0) return;
        using Transaction transaction = new(doc, "Обозначение системы");
        transaction.Start();
        foreach (var (target, value) in changes)
        {
            try
            {
                if (!target.Set(value))
                {
                    Skipped.Add((target.Element.Id, "Revit отклонил запись"));
                    continue;
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException error)
            {
                Skipped.Add((target.Element.Id, error.Message));
                continue;
            }
            if (value.Length == 0) Cleared++;
            else Updated++;
        }
        if (transaction.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit отменил транзакцию обновления обозначений.");
    }
}
