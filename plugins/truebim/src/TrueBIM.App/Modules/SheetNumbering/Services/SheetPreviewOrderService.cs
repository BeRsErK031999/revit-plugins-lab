namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetPreviewOrderService
{
    public SheetPreviewOrderChange<T> MoveUp<T>(IReadOnlyList<T> items, int selectedIndex)
    {
        if (selectedIndex <= 0 || selectedIndex >= items.Count)
        {
            return SheetPreviewOrderChange<T>.NoChange(items);
        }

        List<T> orderedItems = items.ToList();
        (orderedItems[selectedIndex - 1], orderedItems[selectedIndex]) = (orderedItems[selectedIndex], orderedItems[selectedIndex - 1]);
        return SheetPreviewOrderChange<T>.WithChange(orderedItems, selectedIndex - 1);
    }

    public SheetPreviewOrderChange<T> MoveDown<T>(IReadOnlyList<T> items, int selectedIndex)
    {
        if (selectedIndex < 0 || selectedIndex >= items.Count - 1)
        {
            return SheetPreviewOrderChange<T>.NoChange(items);
        }

        List<T> orderedItems = items.ToList();
        (orderedItems[selectedIndex], orderedItems[selectedIndex + 1]) = (orderedItems[selectedIndex + 1], orderedItems[selectedIndex]);
        return SheetPreviewOrderChange<T>.WithChange(orderedItems, selectedIndex + 1);
    }

    public SheetPreviewOrderChange<T> MoveToPosition<T>(IReadOnlyList<T> items, int selectedIndex, int targetPosition)
    {
        int targetIndex = targetPosition - 1;
        if (selectedIndex < 0 || selectedIndex >= items.Count || targetIndex < 0 || targetIndex >= items.Count || selectedIndex == targetIndex)
        {
            return SheetPreviewOrderChange<T>.NoChange(items);
        }

        List<T> orderedItems = items.ToList();
        T selectedItem = orderedItems[selectedIndex];
        orderedItems.RemoveAt(selectedIndex);
        orderedItems.Insert(targetIndex, selectedItem);
        return SheetPreviewOrderChange<T>.WithChange(orderedItems, targetIndex);
    }
}

public sealed record SheetPreviewOrderChange<T>(
    IReadOnlyList<T> Items,
    int SelectedIndex,
    bool Changed)
{
    public static SheetPreviewOrderChange<T> WithChange(IReadOnlyList<T> items, int selectedIndex)
    {
        return new SheetPreviewOrderChange<T>(items, selectedIndex, true);
    }

    public static SheetPreviewOrderChange<T> NoChange(IReadOnlyList<T> items)
    {
        return new SheetPreviewOrderChange<T>(items.ToList(), -1, false);
    }
}
