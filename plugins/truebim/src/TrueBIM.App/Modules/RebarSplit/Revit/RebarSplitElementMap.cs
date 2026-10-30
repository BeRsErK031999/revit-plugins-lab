using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Связи исходных элементов и новых элементов, включая исходные позиции набора.</summary>
internal sealed class RebarSplitElementMap : Dictionary<ElementId, IReadOnlyList<ElementId>>
{
    internal Dictionary<ElementId, IReadOnlyDictionary<int, ElementId>> BarPositions { get; } = new();

    internal ElementId Single(ElementId original) => TryGetValue(original, out IReadOnlyList<ElementId>? ids)
        ? ids.Count == 1 ? ids[0] : throw new InvalidOperationException($"Для элемента {original} нет единственной замены.")
        : original;

    internal void SetBars(ElementId original, IReadOnlyList<RebarSplitBar> bars)
    {
        this[original] = bars.Select(bar => bar.Id).ToList();
        BarPositions[original] = bars.ToDictionary(bar => bar.Position, bar => bar.Id);
    }
}
