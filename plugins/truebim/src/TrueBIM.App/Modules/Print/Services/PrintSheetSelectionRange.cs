namespace TrueBIM.App.Modules.Print.Services;

public sealed record PrintSheetSelectionRange(
    int StartIndex,
    int EndIndex,
    bool IsSelected)
{
    public int Count => EndIndex - StartIndex + 1;

    public static PrintSheetSelectionRange? Resolve(
        int itemCount,
        int anchorIndex,
        int targetIndex,
        bool anchorIsSelected)
    {
        if (itemCount <= 0
            || anchorIndex < 0
            || targetIndex < 0
            || anchorIndex >= itemCount
            || targetIndex >= itemCount)
        {
            return null;
        }

        return new PrintSheetSelectionRange(
            Math.Min(anchorIndex, targetIndex),
            Math.Max(anchorIndex, targetIndex),
            anchorIsSelected);
    }
}
