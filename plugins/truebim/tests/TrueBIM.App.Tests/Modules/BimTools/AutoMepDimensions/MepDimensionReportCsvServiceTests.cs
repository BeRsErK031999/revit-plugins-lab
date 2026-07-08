using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoMepDimensions;

public sealed class MepDimensionReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesValues()
    {
        MepDimensionReportCsvService service = new();

        string csv = service.Format(
        [
            new MepDimensionReportRow(
                "Предпросмотр",
                "Level 1",
                "Трубы-H",
                "Трубы",
                "Горизонтальные трассы",
                3,
                2,
                1,
                MepDimensionStatuses.Ready,
                "Готово; без Reference: 1")
        ]);

        Assert.Contains("Phase;ViewName;CandidateId;CategoryName;DirectionName;ElementCount;ReadyReferenceCount;MissingReferenceCount;Status;Message", csv);
        Assert.Contains("Предпросмотр;Level 1;Трубы-H;Трубы;Горизонтальные трассы;3;2;1;Готово;\"Готово; без Reference: 1\"", csv);
    }
}
