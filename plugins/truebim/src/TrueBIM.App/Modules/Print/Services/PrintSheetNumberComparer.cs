using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetNumberComparer : IComparer<string>
{
    public static PrintSheetNumberComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int xIndex = 0;
        int yIndex = 0;
        while (xIndex < x.Length && yIndex < y.Length)
        {
            bool xIsDigit = char.IsDigit(x[xIndex]);
            bool yIsDigit = char.IsDigit(y[yIndex]);
            int xTokenEnd = FindTokenEnd(x, xIndex, xIsDigit);
            int yTokenEnd = FindTokenEnd(y, yIndex, yIsDigit);
            string xToken = x.Substring(xIndex, xTokenEnd - xIndex);
            string yToken = y.Substring(yIndex, yTokenEnd - yIndex);

            int tokenComparison = xIsDigit && yIsDigit
                ? CompareNumericTokens(xToken, yToken)
                : StringComparer.CurrentCultureIgnoreCase.Compare(xToken, yToken);
            if (tokenComparison != 0)
            {
                return tokenComparison;
            }

            xIndex = xTokenEnd;
            yIndex = yTokenEnd;
        }

        return (x.Length - xIndex).CompareTo(y.Length - yIndex);
    }

    private static int FindTokenEnd(string value, int startIndex, bool isDigit)
    {
        int index = startIndex + 1;
        while (index < value.Length && char.IsDigit(value[index]) == isDigit)
        {
            index++;
        }

        return index;
    }

    private static int CompareNumericTokens(string x, string y)
    {
        string xSignificant = TrimLeadingZeros(x);
        string ySignificant = TrimLeadingZeros(y);
        int lengthComparison = xSignificant.Length.CompareTo(ySignificant.Length);
        if (lengthComparison != 0)
        {
            return lengthComparison;
        }

        int valueComparison = string.CompareOrdinal(xSignificant, ySignificant);
        return valueComparison != 0
            ? valueComparison
            : x.Length.CompareTo(y.Length);
    }

    private static string TrimLeadingZeros(string value)
    {
        int index = 0;
        while (index < value.Length - 1 && value[index] == '0')
        {
            index++;
        }

        return value.Substring(index);
    }
}

public sealed class PrintSheetComparer : IComparer<PrintSheetInfo>
{
    public static PrintSheetComparer Ascending { get; } = new(descendingSheetNumbers: false);

    public static PrintSheetComparer Descending { get; } = new(descendingSheetNumbers: true);

    private readonly bool descendingSheetNumbers;

    private PrintSheetComparer(bool descendingSheetNumbers)
    {
        this.descendingSheetNumbers = descendingSheetNumbers;
    }

    public int Compare(PrintSheetInfo? x, PrintSheetInfo? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.GroupName, y.GroupName);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = PrintSheetNumberComparer.Instance.Compare(x.SheetNumber, y.SheetNumber);
        if (comparison != 0)
        {
            return descendingSheetNumbers ? -comparison : comparison;
        }

        comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.SheetName, y.SheetName);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = x.ElementId.CompareTo(y.ElementId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.SourceName, y.SourceName);
        return comparison != 0
            ? comparison
            : StringComparer.Ordinal.Compare(x.SourceId, y.SourceId);
    }
}

public sealed class PrintSheetHierarchyComparer : IComparer<PrintSheetInfo>
{
    private readonly IReadOnlyDictionary<string, int> sourceOrderById;
    private readonly PrintSheetComparer sheetComparer;

    public PrintSheetHierarchyComparer(
        IReadOnlyDictionary<string, int> sourceOrderById,
        bool descendingSheetNumbers = false)
    {
        Guard.NotNull(sourceOrderById, nameof(sourceOrderById));
        this.sourceOrderById = sourceOrderById;
        sheetComparer = descendingSheetNumbers
            ? PrintSheetComparer.Descending
            : PrintSheetComparer.Ascending;
    }

    public int Compare(PrintSheetInfo? x, PrintSheetInfo? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        if (!string.Equals(x.SourceId, y.SourceId, StringComparison.Ordinal))
        {
            int comparison = GetSourceOrder(x.SourceId).CompareTo(GetSourceOrder(y.SourceId));
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = x.SourceIsLinked.CompareTo(y.SourceIsLinked);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.CurrentCultureIgnoreCase.Compare(x.SourceName, y.SourceName);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(x.SourceId, y.SourceId);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return sheetComparer.Compare(x, y);
    }

    private int GetSourceOrder(string sourceId)
    {
        return sourceOrderById.TryGetValue(sourceId, out int sourceOrder)
            ? sourceOrder
            : int.MaxValue;
    }
}
