using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintExportCompletionPolicyTests
{
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, -1, false)]
    [InlineData(true, 1, true)]
    [InlineData(true, 5, true)]
    public void ShouldOpenExportFolder_RequiresEnabledOptionAndExportedFile(
        bool openAfterCompletion,
        int exportedFileCount,
        bool expected)
    {
        bool result = PrintExportCompletionPolicy.ShouldOpenExportFolder(
            openAfterCompletion,
            exportedFileCount);

        Assert.Equal(expected, result);
    }
}
