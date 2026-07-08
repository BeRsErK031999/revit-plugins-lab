using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.TitleBlockFill;

public sealed class TitleBlockFillReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesValues()
    {
        TitleBlockFillReportCsvService service = new();

        string csv = service.Format(
        [
            new TitleBlockPreviewRow(
                0,
                42,
                "A-101",
                "Plan; Main",
                TitleBlockRuleTargets.TitleBlock,
                "Дата",
                "01.01.2026",
                "08.07.2026",
                "Готово",
                "Записано; проверено",
                CanApply: true)
        ]);

        Assert.Contains("SheetNumber;SheetName;Target;ParameterName;CurrentValue;NewValue;Status;Message", csv);
        Assert.Contains("A-101;\"Plan; Main\";Штамп;Дата;01.01.2026;08.07.2026;Готово;\"Записано; проверено\"", csv);
    }
}
