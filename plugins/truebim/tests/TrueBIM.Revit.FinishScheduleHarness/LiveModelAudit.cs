using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;

namespace TrueBIM.Revit.FinishScheduleHarness;

public static class LiveModelAudit
{
    public static string Run(Document document, string reportPath)
    {
        if (!document.Title.StartsWith("D085_П_АР_ALL_rvt23_тест_Отделки", StringComparison.Ordinal))
            throw new InvalidOperationException("The active document is not the requested finish test model.");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        string? error = null;
        using TransactionGroup rollback = new(document, "TrueBIM: временная проверка отделки с откатом");
        rollback.Start();
        try
        {
            new FinishModelAudit(message => File.AppendAllText(reportPath + ".progress.txt", message + Environment.NewLine))
                .Run(document, reportPath);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            rollback.RollBack();
        }
        string result = new JavaScriptSerializer().Serialize(new
        {
            FatalError = error,
            Document = document.Title,
            RolledBack = true,
            CompletedUtc = DateTime.UtcNow.ToString("o")
        });
        File.WriteAllText(reportPath, result);
        return result;
    }
}
