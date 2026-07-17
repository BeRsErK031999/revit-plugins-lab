using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal static class FinishTransactionStatus
{
    public static void EnsureStarted(Transaction transaction)
    {
        TransactionStatus status = transaction.Start();
        if (status != TransactionStatus.Started)
        {
            throw new InvalidOperationException($"Revit не начал транзакцию «{transaction.GetName()}». Status={status}.");
        }
    }

    public static void EnsureCommitted(Transaction transaction)
    {
        TransactionStatus status = transaction.Commit();
        if (status != TransactionStatus.Committed)
        {
            throw new InvalidOperationException($"Revit не зафиксировал транзакцию «{transaction.GetName()}». Status={status}.");
        }
    }

    public static void RollBackIfStarted(Transaction transaction)
    {
        if (transaction.GetStatus() == TransactionStatus.Started)
        {
            transaction.RollBack();
        }
    }

    public static void EnsureStarted(TransactionGroup group)
    {
        TransactionStatus status = group.Start();
        if (status != TransactionStatus.Started)
        {
            throw new InvalidOperationException($"Revit не начал группу транзакций «{group.GetName()}». Status={status}.");
        }
    }

    public static void EnsureAssimilated(TransactionGroup group)
    {
        TransactionStatus status = group.Assimilate();
        if (status != TransactionStatus.Committed)
        {
            throw new InvalidOperationException($"Revit не объединил группу транзакций «{group.GetName()}». Status={status}.");
        }
    }

    public static void RollBackIfStarted(TransactionGroup group)
    {
        if (group.GetStatus() == TransactionStatus.Started)
        {
            group.RollBack();
        }
    }
}
