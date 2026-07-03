using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetSelectionState
{
    private readonly Dictionary<PrintSheetSelectionKey, bool> selections = new();

    public PrintSheetSelectionState(IEnumerable<PrintSheetInfo> sheets)
    {
        if (sheets is null)
        {
            throw new ArgumentNullException(nameof(sheets));
        }

        foreach (PrintSheetInfo sheet in sheets)
        {
            selections[CreateKey(sheet)] = sheet.CanBePrinted;
        }
    }

    public bool Get(PrintSheetInfo sheet)
    {
        if (sheet is null)
        {
            throw new ArgumentNullException(nameof(sheet));
        }

        if (!sheet.CanBePrinted)
        {
            return false;
        }

        PrintSheetSelectionKey key = CreateKey(sheet);
        if (selections.TryGetValue(key, out bool isSelected))
        {
            return isSelected;
        }

        selections[key] = true;
        return true;
    }

    public void Set(PrintSheetInfo sheet, bool isSelected)
    {
        if (sheet is null)
        {
            throw new ArgumentNullException(nameof(sheet));
        }

        selections[CreateKey(sheet)] = sheet.CanBePrinted && isSelected;
    }

    public int CountSelected(IEnumerable<PrintSheetInfo> sheets)
    {
        if (sheets is null)
        {
            throw new ArgumentNullException(nameof(sheets));
        }

        return sheets.Count(sheet => sheet.CanBePrinted && Get(sheet));
    }

    private static PrintSheetSelectionKey CreateKey(PrintSheetInfo sheet)
    {
        return new PrintSheetSelectionKey(sheet.SourceId, sheet.ElementId);
    }
}

public sealed record PrintSheetSelectionKey(string SourceId, long ElementId);
