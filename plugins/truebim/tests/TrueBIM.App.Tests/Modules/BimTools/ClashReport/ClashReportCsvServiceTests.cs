using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesComments()
    {
        ClashItem item = new("C-01", "Pipe vs wall", 101, 202, 1, 2, 3, ClashStatus.Ignored, "Checked; false positive")
        {
            IsElement1Resolved = true,
            IsElement2Resolved = false,
            Element1Name = "Pipe",
            Message = "Найдено элементов: 1/2."
        };

        string csv = new ClashReportCsvService().Format([item]);

        Assert.Contains("Source;ClashId;ClashName;Element1Source;ElementId1;Element1Resolved;Element1Name", csv);
        Assert.Contains("Проверка;C-01;Pipe vs wall;;101;yes;Pipe;;202;no;;1;2;3;Ignored;\"Checked; false positive\";Найдено элементов: 1/2.", csv);
    }
}
