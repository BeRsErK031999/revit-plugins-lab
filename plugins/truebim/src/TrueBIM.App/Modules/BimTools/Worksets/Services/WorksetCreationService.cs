using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.Worksets.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.Worksets.Services;

public sealed class WorksetCreationService
{
    private readonly ITrueBimLogger logger;

    public WorksetCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ISet<string> CollectExistingWorksetNames(Document document)
    {
        if (!document.IsWorkshared)
        {
            return new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        }

        return new HashSet<string>(
            new FilteredWorksetCollector(document)
            .OfKind(WorksetKind.UserWorkset)
            .ToWorksets()
            .Select(workset => workset.Name),
            StringComparer.CurrentCultureIgnoreCase);
    }

    public WorksetCreateResult Create(Document document, IReadOnlyList<WorksetImportRow> rows)
    {
        List<WorksetImportRow> resultRows = rows
            .Select(CloneRow)
            .ToList();
        IReadOnlyList<WorksetImportRow> rowsToCreate = resultRows
            .Where(row => row.Status == WorksetImportStatus.WillCreate)
            .ToList();
        if (rowsToCreate.Count == 0)
        {
            return new WorksetCreateResult(resultRows);
        }

        using Transaction transaction = new(document, "TrueBIM: создать рабочие наборы");
        transaction.Start();

        try
        {
            foreach (WorksetImportRow row in rowsToCreate)
            {
                CreateOne(document, row);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.RollBack();
            throw;
        }

        logger.Info(
            $"Created worksets. Created: {resultRows.Count(row => row.Status == WorksetImportStatus.Created)}; failed: {resultRows.Count(row => row.Status == WorksetImportStatus.Failed)}; skipped: {resultRows.Count(row => row.Status is WorksetImportStatus.Empty or WorksetImportStatus.Invalid or WorksetImportStatus.DuplicateInFile or WorksetImportStatus.Existing)}.");

        return new WorksetCreateResult(resultRows);
    }

    private static void CreateOne(Document document, WorksetImportRow row)
    {
        try
        {
            if (!WorksetTable.IsWorksetNameUnique(document, row.WorksetName))
            {
                row.Status = WorksetImportStatus.Existing;
                row.Message = "Рабочий набор уже существует в модели.";
                return;
            }

            Workset.Create(document, row.WorksetName);
            row.Status = WorksetImportStatus.Created;
            row.Message = "Рабочий набор создан.";
        }
        catch (Exception exception)
        {
            row.Status = WorksetImportStatus.Failed;
            row.Message = string.IsNullOrWhiteSpace(exception.Message)
                ? "Ошибка создания рабочего набора."
                : exception.Message;
        }
    }

    private static WorksetImportRow CloneRow(WorksetImportRow row)
    {
        return new WorksetImportRow(row.LineNumber, row.SourceValue, row.WorksetName)
        {
            Status = row.Status,
            Message = row.Message
        };
    }
}
