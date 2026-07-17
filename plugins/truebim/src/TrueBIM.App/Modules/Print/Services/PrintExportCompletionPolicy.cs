namespace TrueBIM.App.Modules.Print.Services;

public static class PrintExportCompletionPolicy
{
    public static bool ShouldOpenExportFolder(bool openAfterCompletion, int exportedFileCount)
    {
        return openAfterCompletion && exportedFileCount > 0;
    }
}
