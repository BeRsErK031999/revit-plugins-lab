using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintSheetNumberComparerTests
{
    [Fact]
    public void Compare_SortsPlainNumbersByNumericValue()
    {
        string[] values = ["11", "2", "1", "10"];

        string[] result = values.OrderBy(value => value, PrintSheetNumberComparer.Instance).ToArray();

        Assert.Equal(["1", "2", "10", "11"], result);
    }

    [Fact]
    public void Compare_SortsCompositeSheetNumbersByEveryNumericChunk()
    {
        string[] values = ["АР-10", "АР-2.10", "АР-2", "АР-2.2", "АР-1"];

        string[] result = values.OrderBy(value => value, PrintSheetNumberComparer.Instance).ToArray();

        Assert.Equal(["АР-1", "АР-2", "АР-2.2", "АР-2.10", "АР-10"], result);
    }

    [Fact]
    public void Compare_HandlesLeadingZerosAndNumbersLargerThanInt64()
    {
        string[] values = ["100000000000000000000", "002", "02", "10", "2"];

        string[] result = values.OrderBy(value => value, PrintSheetNumberComparer.Instance).ToArray();

        Assert.Equal(["2", "02", "002", "10", "100000000000000000000"], result);
    }

    [Fact]
    public void SheetComparer_StabilizesDuplicateNumbersByNameThenElementId()
    {
        PrintSheetInfo[] sheets =
        [
            Sheet(30, "A-2", "Разрез"),
            Sheet(20, "A-2", "План"),
            Sheet(10, "A-2", "План"),
            Sheet(40, "A-10", "План")
        ];

        PrintSheetInfo[] result = sheets.OrderBy(sheet => sheet, PrintSheetComparer.Ascending).ToArray();

        Assert.Equal([10L, 20L, 30L, 40L], result.Select(sheet => sheet.ElementId));
    }

    [Fact]
    public void SheetComparer_DescendingReversesOnlyNaturalNumberOrder()
    {
        PrintSheetInfo[] sheets =
        [
            Sheet(20, "A-2", "План"),
            Sheet(100, "A-10", "План")
        ];

        PrintSheetInfo[] result = sheets.OrderBy(sheet => sheet, PrintSheetComparer.Descending).ToArray();

        Assert.Equal(["A-10", "A-2"], result.Select(sheet => sheet.SheetNumber));
    }

    [Fact]
    public void HierarchyComparer_SortsBySourceOrderThenGroupAndNaturalNumber()
    {
        IReadOnlyDictionary<string, int> sourceOrder = new Dictionary<string, int>
        {
            ["active"] = 0,
            ["secondary"] = 1
        };
        PrintSheetInfo[] sheets =
        [
            Sheet(50, "A-1", "План", "secondary", "Secondary", "Том 1"),
            Sheet(40, "A-10", "План", "active", "Active", "Том 2"),
            Sheet(30, "A-2", "План", "active", "Active", "Том 2"),
            Sheet(20, "A-5", "План", "active", "Active", "Том 1")
        ];
        PrintSheetHierarchyComparer comparer = new(sourceOrder);

        PrintSheetInfo[] result = sheets.OrderBy(sheet => sheet, comparer).ToArray();

        Assert.Equal([20L, 30L, 40L, 50L], result.Select(sheet => sheet.ElementId));
    }

    private static PrintSheetInfo Sheet(
        long elementId,
        string number,
        string name,
        string sourceId = "model",
        string sourceName = "Model",
        string groupName = "Без группы")
    {
        return new PrintSheetInfo(
            elementId,
            sourceId,
            sourceName,
            SourceIsLinked: false,
            GroupName: groupName,
            number,
            name,
            "A1",
            IsPlaceholder: false,
            CanBePrinted: true,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
    }
}
