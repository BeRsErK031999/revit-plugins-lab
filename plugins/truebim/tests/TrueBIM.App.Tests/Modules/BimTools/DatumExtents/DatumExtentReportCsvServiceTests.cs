using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Modules.BimTools.DatumExtents.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.DatumExtents;

public sealed class DatumExtentReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesValues()
    {
        DatumExtentReportCsvService service = new();

        string csv = service.Format(
        [
            new DatumExtentReportRow(
                "Предпросмотр",
                "Level 1",
                42,
                "Ось",
                "А;1",
                "2D на активном виде",
                "3D модельные экстенты",
                "2D на активном виде",
                DatumExtentStatuses.Ready,
                "Готово; можно переключать")
        ]);

        Assert.Contains("Phase;ViewName;ElementId;Kind;Name;TargetExtentType;End0Type;End1Type;Status;Message", csv);
        Assert.Contains("Предпросмотр;Level 1;42;Ось;\"А;1\";2D на активном виде;3D модельные экстенты;2D на активном виде;Готово;\"Готово; можно переключать\"", csv);
    }
}
