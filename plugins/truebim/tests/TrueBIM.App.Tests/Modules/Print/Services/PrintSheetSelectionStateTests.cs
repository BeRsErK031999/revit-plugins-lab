using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintSheetSelectionStateTests
{
    [Fact]
    public void Get_SelectsPrintableSheetsByDefault()
    {
        PrintSheetInfo printableSheet = Sheet(10, "model", canBePrinted: true);
        PrintSheetInfo notPrintableSheet = Sheet(11, "model", canBePrinted: false);
        PrintSheetSelectionState state = new([printableSheet, notPrintableSheet]);

        Assert.True(state.Get(printableSheet));
        Assert.False(state.Get(notPrintableSheet));
        Assert.Equal(1, state.CountSelected([printableSheet, notPrintableSheet]));
    }

    [Fact]
    public void Get_PreservesSelectionForEquivalentSheetIdentity()
    {
        PrintSheetInfo sheet = Sheet(10, "model", canBePrinted: true);
        PrintSheetSelectionState state = new([sheet]);

        state.Set(sheet, isSelected: false);

        PrintSheetInfo reloadedSheet = Sheet(10, "model", canBePrinted: true);
        Assert.False(state.Get(reloadedSheet));
    }

    [Fact]
    public void Get_KeepsSourcesSeparateForSameElementId()
    {
        PrintSheetInfo firstSourceSheet = Sheet(10, "first", canBePrinted: true);
        PrintSheetInfo secondSourceSheet = Sheet(10, "second", canBePrinted: true);
        PrintSheetSelectionState state = new([firstSourceSheet, secondSourceSheet]);

        state.Set(firstSourceSheet, isSelected: false);

        Assert.False(state.Get(firstSourceSheet));
        Assert.True(state.Get(secondSourceSheet));
    }

    [Fact]
    public void Set_DoesNotSelectNonPrintableSheets()
    {
        PrintSheetInfo sheet = Sheet(10, "model", canBePrinted: false);
        PrintSheetSelectionState state = new([sheet]);

        state.Set(sheet, isSelected: true);

        Assert.False(state.Get(sheet));
        Assert.Equal(0, state.CountSelected([sheet]));
    }

    private static PrintSheetInfo Sheet(long elementId, string sourceId, bool canBePrinted)
    {
        return new PrintSheetInfo(
            elementId,
            sourceId,
            sourceId,
            SourceIsLinked: false,
            GroupName: "Без группы",
            $"A-{elementId}",
            "Plan",
            "A1",
            IsPlaceholder: !canBePrinted,
            CanBePrinted: canBePrinted,
            new Dictionary<string, string>());
    }
}
