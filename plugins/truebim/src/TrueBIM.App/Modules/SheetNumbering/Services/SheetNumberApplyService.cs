using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetNumberApplyService
{
    public SheetNumberApplyResult Apply(Document document, IReadOnlyList<SheetNumberChange> changes)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(changes, nameof(changes));

        int unchangedCount = changes.Count(change => !change.IsChanged);
        List<SheetNumberChange> changedChanges = changes
            .Where(change => change.IsChanged)
            .ToList();

        if (changedChanges.Count == 0)
        {
            return new SheetNumberApplyResult(
                Succeeded: true,
                Message: "No changed sheet numbers to apply.",
                ChangedCount: 0,
                UnchangedCount: unchangedCount,
                SkippedCount: 0,
                FailedCount: 0);
        }

        SheetNumberApplyResult? validationFailure = ValidateChangedSheets(document, changedChanges, unchangedCount);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        Dictionary<long, ViewSheet> sheetsById = changedChanges
            .ToDictionary(
                change => change.ElementId,
                change => (ViewSheet)document.GetElement(RevitElementIds.Create(change.ElementId)));
        using Transaction transaction = new(document, "TrueBIM Sheet Numbering");
        transaction.Start();

        try
        {
            foreach (SheetNumberChange change in changedChanges)
            {
                sheetsById[change.ElementId].SheetNumber = change.NewNumber;
            }

            transaction.Commit();
        }
        catch (Exception exception) when (
            exception is Autodesk.Revit.Exceptions.ArgumentException
            or InvalidOperationException
            or Autodesk.Revit.Exceptions.ApplicationException)
        {
            if (transaction.HasStarted())
            {
                transaction.RollBack();
            }

            return new SheetNumberApplyResult(
                Succeeded: false,
                Message: "Apply failed during transaction. The transaction was rolled back.",
                ChangedCount: 0,
                UnchangedCount: unchangedCount,
                SkippedCount: 0,
                FailedCount: changedChanges.Count);
        }

        return new SheetNumberApplyResult(
            Succeeded: true,
            Message: "Apply committed successfully.",
            ChangedCount: changedChanges.Count,
            UnchangedCount: unchangedCount,
            SkippedCount: 0,
            FailedCount: 0);
    }

    private static SheetNumberApplyResult? ValidateChangedSheets(
        Document document,
        IReadOnlyList<SheetNumberChange> changedChanges,
        int unchangedCount)
    {
        foreach (SheetNumberChange change in changedChanges)
        {
            if (string.IsNullOrWhiteSpace(change.NewNumber))
            {
                return Failure(
                    unchangedCount: unchangedCount,
                    failedCount: 1,
                    message: "Apply validation failed: new sheet number is empty.");
            }

            Element? element = document.GetElement(RevitElementIds.Create(change.ElementId));
            if (element is null)
            {
                return Failure(
                    unchangedCount: unchangedCount,
                    skippedCount: 1,
                    message: "Apply validation failed: a target sheet was not found.");
            }

            if (element is not ViewSheet)
            {
                return Failure(
                    unchangedCount: unchangedCount,
                    failedCount: 1,
                    message: "Apply validation failed: a target element is not a sheet.");
            }
        }

        return null;
    }

    private static SheetNumberApplyResult Failure(
        int unchangedCount,
        int skippedCount = 0,
        int failedCount = 0,
        string message = "Apply validation failed.")
    {
        return new SheetNumberApplyResult(
            Succeeded: false,
            Message: message,
            ChangedCount: 0,
            UnchangedCount: unchangedCount,
            SkippedCount: skippedCount,
            FailedCount: failedCount);
    }
}
