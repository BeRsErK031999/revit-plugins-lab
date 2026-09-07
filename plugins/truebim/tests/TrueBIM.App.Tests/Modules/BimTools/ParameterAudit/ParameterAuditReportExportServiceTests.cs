using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditReportExportServiceTests
{
    [Fact]
    public void ExportCsv_IncludesElementIdentityUsedForNavigation()
    {
        string path = Path.Combine(Path.GetTempPath(), $"truebim-parameter-audit-{Guid.NewGuid():N}.csv");
        try
        {
            ParameterAuditResultRow row = new(
                "RULE_1",
                ParameterAuditSeverity.Error,
                ParameterAuditIssueCode.EmptyValue,
                "Linked model",
                true,
                42,
                "Стены",
                "Basic Wall",
                "200 мм",
                "ADSK_Этаж",
                string.Empty,
                "не пусто",
                "Параметр не заполнен.",
                "linked-unique-id",
                700);
            ParameterAuditReport report = new(
                DateTimeOffset.Now,
                1,
                1,
                0,
                [row]);

            new ParameterAuditReportExportService().ExportCsv(path, report);

            string csv = File.ReadAllText(path);
            Assert.Contains("ElementId;UniqueId;HostLinkInstanceId", csv, StringComparison.Ordinal);
            Assert.Contains(";42;linked-unique-id;700;", csv, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
