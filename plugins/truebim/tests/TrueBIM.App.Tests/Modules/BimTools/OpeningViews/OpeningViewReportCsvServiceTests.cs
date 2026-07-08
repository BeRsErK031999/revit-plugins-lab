using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesValues()
    {
        OpeningViewReportCsvService service = new();

        string csv = service.Format(
        [
            new OpeningViewReportRow(
                "Предпросмотр",
                "Level 1",
                42,
                "Дверь",
                "Family;A",
                "Type 1",
                "Level 1",
                "BIM_Opening_Door_42",
                OpeningViewStatuses.Ready,
                "Готово; можно создавать")
        ]);

        Assert.Contains("Phase;SourceViewName;ElementId;CategoryName;FamilyName;TypeName;LevelName;ViewName;Status;Message", csv);
        Assert.Contains("Предпросмотр;Level 1;42;Дверь;\"Family;A\";Type 1;Level 1;BIM_Opening_Door_42;Готово;\"Готово; можно создавать\"", csv);
    }
}
