using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Извлекает наборы через разгруппирование без копирования или замены остальных элементов.</summary>
internal sealed class RebarSplitGroupExtraction
{
    private readonly Dictionary<ElementId, int> groups = new();
    private readonly Dictionary<ElementId, Member> members = new();
    private readonly HashSet<ElementId> processed;
    private readonly IReadOnlyDictionary<ElementId, string> modelBefore;

    private RebarSplitGroupExtraction(Document document, IReadOnlyList<RebarSplitSet> replacements,
        IReadOnlyDictionary<ElementId, string> modelBefore, List<string> warnings)
    {
        this.modelBefore = modelBefore;
        processed = new(replacements.Select(set => set.SourceId));
        HashSet<ElementId> roots = new();
        foreach (RebarSplitSet set in replacements)
        {
            if (document.GetElement(set.GroupId) is not Group group) continue;
            List<Group> chain = new() { group };
            while (document.GetElement(group.GroupId) is Group parent)
            {
                chain.Add(parent);
                group = parent;
            }
            for (int index = 0; index < chain.Count; index++) groups[chain[index].Id] = chain.Count - index - 1;
            roots.Add(group.Id);
        }
        foreach (ElementId root in roots) CaptureMembers((Group)document.GetElement(root));
        foreach (ElementId id in groups.Keys)
        {
            Group group = (Group)document.GetElement(id);
            if (typeof(GroupType).GetMethod("GetAvailableAttachedDetailGroupTypeIds")?.Invoke(group.GroupType, null)
                is IEnumerable<ElementId> detailTypes && detailTypes.Any())
                warnings.Add($"Группа ID {id}: присоединённые группы узлов не сохраняются при разгруппировании. "
                    + "Поддерживаемые марки и размеры переносятся при включённой опции; другое оформление может быть потеряно.");
        }

        void CaptureMembers(Group group)
        {
            foreach (ElementId id in group.GetMemberIds())
            {
                Element member = document.GetElement(id);
                if (!members.ContainsKey(id)) members.Add(id, Member.Capture(member));
                if (member is Group child) CaptureMembers(child);
            }
        }
    }

    internal static IReadOnlyDictionary<ElementId, string> CaptureModel(Document document) =>
        new FilteredElementCollector(document).WhereElementIsNotElementType().Where(element => !element.ViewSpecific)
            .ToDictionary(element => element.Id, element => element.GetType().Name + "/" + element.Category?.Name);

    internal static RebarSplitGroupExtraction Capture(Document document, IReadOnlyList<RebarSplitSet> replacements,
        IReadOnlyDictionary<ElementId, string> modelBefore, List<string> warnings)
        => new(document, replacements, modelBefore, warnings);

    internal IEnumerable<ElementId> AffectedIds => members.Keys;

    internal RebarSplitElementMap Extract(Document document, IReadOnlyList<RebarSplitSet> replacements)
    {
        RebarSplitElementMap map = new();
        foreach (ElementId id in groups.OrderBy(pair => pair.Value).Select(pair => pair.Key))
        {
            Group group = document.GetElement(id) as Group
                ?? throw new RebarSplitGroupException($"Группа {id} исчезла до извлечения арматуры. Операция отменена.");
            if (group.Pinned) group.Pinned = false;
            group.UngroupMembers();
            map[id] = Array.Empty<ElementId>();
        }
        document.Regenerate();
        foreach (Member original in members.Values.Where(member => !groups.ContainsKey(member.Id)))
        {
            Element target = document.GetElement(original.Id)
                ?? throw new RebarSplitGroupException($"Элемент {original.Id} потерян при разгруппировании. Операция отменена.");
            if (target.Pinned != original.Pinned) target.Pinned = original.Pinned;
            if (!processed.Contains(original.Id)) original.RestoreRebarPositions(document, target);
        }
        foreach (RebarSplitSet set in replacements) map.SetBars(set.SourceId, set.Bars);
        return map;
    }

    internal void Validate(Document document, bool allowPendingGroupDeletion = false)
    {
        foreach (ElementId id in groups.Keys)
            if (!allowPendingGroupDeletion && document.GetElement(id) is Group { IsValidObject: true } group)
                throw new RebarSplitGroupException($"Группа {id} не разгруппирована (IsModifiable={document.IsModifiable}, "
                    + $"члены: {string.Join(", ", group.GetMemberIds())}). Операция отменена.");
        foreach (Member original in members.Values.Where(member => !groups.ContainsKey(member.Id) && !processed.Contains(member.Id)))
        {
            Element? target = document.GetElement(original.Id);
            ElementId expectedGroup = groups.ContainsKey(original.GroupId) ? ElementId.InvalidElementId : original.GroupId;
            bool pendingGroup = allowPendingGroupDeletion && groups.ContainsKey(original.GroupId) && target?.GroupId == original.GroupId;
            if (target is null || target.GetTypeId() != original.TypeId || target.Pinned != original.Pinned
                || (target.GroupId != expectedGroup && !pendingGroup) || !original.SameGeometry(target)
                || original.Parameters.Validate(target).Count > 0)
                throw new RebarSplitGroupException($"Элемент {original.Id}: геометрия, свойства или закрепление изменились при извлечении арматуры.");
        }
        foreach (KeyValuePair<ElementId, string> original in modelBefore)
            if (!processed.Contains(original.Key) && !groups.ContainsKey(original.Key) && document.GetElement(original.Key) is null)
                throw new RebarSplitGroupException($"Посторонний элемент {original.Key} ({original.Value}) потерян. Операция отменена.");
    }

    private sealed record Member(ElementId Id, ElementId TypeId, ElementId GroupId, bool Pinned,
        RebarSplitParameters Parameters, XYZ? Min, XYZ? Max, IReadOnlyList<IList<Curve>> Curves,
        ElementId HostId, IReadOnlyList<ElementId>? GroupMembers, IReadOnlyList<int> Positions,
        int TotalPositions, RebarLayoutRule? Layout)
    {
        internal static Member Capture(Element element)
        {
            BoundingBoxXYZ? box = element.get_BoundingBox(null);
            List<IList<Curve>> curves = new();
            List<int> positions = new();
            if (element is Rebar rebar)
            {
                positions = RebarSplitGeometry.ExistingPositions(rebar);
                curves.AddRange(positions
                    .Select(index => RebarSplitGeometry.Curves(rebar, index).Select(curve => curve.Clone()).ToList()));
            }
            else if (element.Location is LocationCurve location) curves.Add(new[] { location.Curve.Clone() });
            return new(element.Id, element.GetTypeId(), element.GroupId, element.Pinned,
                RebarSplitParameters.Capture(element, element is Rebar bar ? RebarSplitGeometry.ShapeParameters(bar) : new HashSet<ElementId>()),
                box?.Transform.OfPoint(box.Min), box?.Transform.OfPoint(box.Max), curves,
                element is Rebar hosted ? hosted.GetHostId() : ElementId.InvalidElementId,
                element is Group group ? group.GetMemberIds().ToList() : null, positions,
                element is Rebar set ? set.NumberOfBarPositions : 0,
                element is Rebar layout ? layout.LayoutRule : null);
        }

        internal void RestoreRebarPositions(Document document, Element target)
        {
            if (target is not Rebar rebar) return;
            if (!Positions.SequenceEqual(RebarSplitGeometry.ExistingPositions(rebar))
                || rebar.NumberOfBarPositions != TotalPositions || rebar.LayoutRule != Layout)
                throw new RebarSplitGroupException($"Набор {Id}: схема распределения изменилась при разгруппировании. Операция отменена.");
            if (Curves.Select((expected, index) => RebarSplitGeometry.SameCurves(expected,
                    RebarSplitGeometry.Curves(rebar, Positions[index]))).All(matches => matches)) return;
            bool pinned = rebar.Pinned;
            if (pinned) rebar.Pinned = false;
            try
            {
#if REVIT2022_OR_GREATER
                for (int index = 0; index < Positions.Count; index++)
                {
                    IList<Curve> actual = RebarSplitGeometry.Curves(rebar, Positions[index]);
                    if (RebarSplitGeometry.SameCurves(Curves[index], actual)) continue;
                    XYZ offset = Translation(Curves[index], actual)
                        ?? throw new RebarSplitGroupException($"Набор {Id}, позиция {Positions[index] + 1}: форма изменилась при разгруппировании. Операция отменена.");
                    // UngroupMembers может сбросить перемещения отдельных позиций. Восстанавливаем
                    // только доказанный перенос; форму, длину и поворот не подгоняем.
                    rebar.MoveBarInSet(Positions[index], Transform.CreateTranslation(offset));
                    document.Regenerate();
                }
#else
                XYZ? offset = Translation(Curves[0], RebarSplitGeometry.Curves(rebar, Positions[0]));
                if (offset is null || !Curves.Select((expected, index) => RebarSplitGeometry.SameCurves(expected,
                        RebarSplitGeometry.Curves(rebar, Positions[index]).Select(curve => curve.CreateTransformed(Transform.CreateTranslation(offset))).ToList()))
                    .All(matches => matches))
                    throw new RebarSplitGroupException($"Набор {Id}: положение стержней изменилось при разгруппировании. Операция отменена.");
                ElementTransformUtils.MoveElement(document, rebar.Id, offset);
                document.Regenerate();
#endif
            }
            finally { if (pinned) rebar.Pinned = true; }
        }

        private static XYZ? Translation(IList<Curve> expected, IList<Curve> actual)
        {
            foreach (XYZ anchor in new[] { actual[0].GetEndPoint(0), actual[actual.Count - 1].GetEndPoint(1) })
            {
                XYZ offset = expected[0].GetEndPoint(0) - anchor;
                if (RebarSplitGeometry.SameCurves(expected, actual.Select(curve => curve.CreateTransformed(Transform.CreateTranslation(offset))).ToList()))
                    return offset;
            }
            return null;
        }

        internal bool SameGeometry(Element element)
        {
            Member actual = Capture(element);
            if (HostId != actual.HostId || Curves.Count != actual.Curves.Count || TotalPositions != actual.TotalPositions
                || Layout != actual.Layout || !Positions.SequenceEqual(actual.Positions)) return false;
            if (GroupMembers is not null && (actual.GroupMembers is null || !GroupMembers.SequenceEqual(actual.GroupMembers))) return false;
            for (int index = 0; index < Curves.Count; index++)
                if (!RebarSplitGeometry.SameCurves(Curves[index], actual.Curves[index])) return false;
            // Габарит Rebar включает служебную область набора и может измениться после
            // разгруппирования. Физическую геометрию проверяют кривые каждой позиции выше.
            if (element is Rebar) return true;
            return SamePoint(Min, actual.Min) && SamePoint(Max, actual.Max);
        }

        private static bool SamePoint(XYZ? first, XYZ? second) => first is null ? second is null
            : second is not null && first.DistanceTo(second) <= RebarSplitPolicy.LengthToleranceFeet;
    }
}

internal sealed class RebarSplitGroupException : InvalidOperationException
{
    internal RebarSplitGroupException(string message) : base(message) { }
}
