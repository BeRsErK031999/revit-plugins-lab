using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetNumberApplyService
{
    public SheetNumberApplyResult Apply(Document document, IReadOnlyList<SheetNumberChange> changes)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(changes);

        int changedCount = 0;
        int unchangedCount = 0;
        int skippedCount = 0;
        int failedCount = 0;

        using Transaction transaction = new(document, "TrueBIM Sheet Numbering");
        transaction.Start();

        try
        {
            foreach (SheetNumberChange change in changes)
            {
                if (!change.IsChanged)
                {
                    unchangedCount++;
                    continue;
                }

                if (document.GetElement(new ElementId(change.ElementId)) is not ViewSheet sheet)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    sheet.SheetNumber = change.NewNumber;
                    changedCount++;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    failedCount++;
                }
                catch (InvalidOperationException)
                {
                    failedCount++;
                }
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.HasStarted())
            {
                transaction.RollBack();
            }

            throw;
        }

        return new SheetNumberApplyResult(changedCount, unchangedCount, skippedCount, failedCount);
    }
}
