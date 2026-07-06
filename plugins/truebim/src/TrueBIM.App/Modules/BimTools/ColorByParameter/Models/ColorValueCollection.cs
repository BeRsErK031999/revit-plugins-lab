namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class ColorValueCollection
{
    public ColorValueCollection(IReadOnlyList<ColorRuleRow> rows, int totalValueCount)
    {
        Rows = rows ?? [];
        TotalValueCount = totalValueCount;
    }

    public IReadOnlyList<ColorRuleRow> Rows { get; }

    public int TotalValueCount { get; }

    public bool WasTruncated => TotalValueCount > Rows.Count;
}
