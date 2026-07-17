namespace TrueBIM.App.Modules.Print.Services;

public enum DwfExportTransactionMode
{
    UseExistingTransaction,
    StartTemporaryTransaction,
    RejectReadOnlyDocument
}

public static class DwfExportTransactionPolicy
{
    public static DwfExportTransactionMode Resolve(bool documentIsModifiable, bool documentIsReadOnly)
    {
        if (documentIsModifiable)
        {
            return DwfExportTransactionMode.UseExistingTransaction;
        }

        return documentIsReadOnly
            ? DwfExportTransactionMode.RejectReadOnlyDocument
            : DwfExportTransactionMode.StartTemporaryTransaction;
    }
}
