using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesComments()
    {
        ClashItem item = new(
            "C-01",
            "Pipe vs wall",
            101,
            202,
            1,
            2,
            3,
            ClashStatus.Ignored,
            "Checked; false positive",
            priority: ClashPriority.High,
            severityScore: 42.5,
            groupKey: "Model | Pipes x Walls",
            fingerprint: "CM-ABC123",
            approximateVolumeMm3: 9000000,
            assignedTo: "BIM Coordinator")
        {
            IsElement1Resolved = true,
            IsElement2Resolved = false,
            Element1Name = "Pipe",
            Message = "Найдено элементов: 1/2."
        };

        string csv = new ClashReportCsvService().Format([item]);

        Assert.Contains("Source;ClashId;Fingerprint;ClashName;Type;Priority;SeverityScore;Group;AssignedTo", csv);
        Assert.Contains("Проверка;C-01;CM-ABC123;Pipe vs wall;Hard;High;42.5;Model | Pipes x Walls;BIM Coordinator;;101;yes;Pipe;;202;no;;1;2;3;9000000;Ignored;\"Checked; false positive\";Найдено элементов: 1/2.", csv);
    }
}
