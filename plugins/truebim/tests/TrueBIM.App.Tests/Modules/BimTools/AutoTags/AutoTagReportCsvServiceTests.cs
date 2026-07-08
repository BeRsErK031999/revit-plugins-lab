using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Modules.BimTools.AutoTags.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoTags;

public sealed class AutoTagReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesValues()
    {
        AutoTagReportCsvService service = new();

        string csv = service.Format(
        [
            new AutoTagReportRow(
                "Предпросмотр",
                "Level 1",
                42,
                "Walls",
                "Wall; Basic",
                "Авто (по категории)",
                AutoTagStatuses.Ready,
                "Готово; можно ставить")
        ]);

        Assert.Contains("Phase;ViewName;ElementId;CategoryName;ElementName;TagType;Status;Message", csv);
        Assert.Contains("Предпросмотр;Level 1;42;Walls;\"Wall; Basic\";Авто (по категории);Готово;\"Готово; можно ставить\"", csv);
    }
}
