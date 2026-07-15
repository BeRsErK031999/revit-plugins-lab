using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelAssemblyCreationResultTests
{
    [Theory]
    [InlineData(LintelAssemblyCreationStatus.Created, true, "Создано")]
    [InlineData(LintelAssemblyCreationStatus.AlreadyExists, false, "Уже существует")]
    [InlineData(LintelAssemblyCreationStatus.Blocked, false, "Заблокировано")]
    [InlineData(LintelAssemblyCreationStatus.Failed, false, "Ошибка")]
    public void Result_MapsStatusAndMutationFlag(
        LintelAssemblyCreationStatus status,
        bool expectedModelChanged,
        string expectedDisplay)
    {
        LintelAssemblyCreationResult result = new(status, "TB_Перемычка_ПР-1_100", 501, "Сообщение");

        Assert.Equal(expectedModelChanged, result.ModelChanged);
        Assert.Equal(expectedDisplay, result.StatusDisplay);
    }

    [Fact]
    public void BuildSummary_IncludesAssemblyIdentityAndMessage()
    {
        LintelAssemblyCreationResult result = new(
            LintelAssemblyCreationStatus.Created,
            "TB_Перемычка_ПР-1_100",
            501,
            "Вид ещё не создавался.");

        string summary = result.BuildSummary();

        Assert.Contains("TB_Перемычка_ПР-1_100", summary, StringComparison.Ordinal);
        Assert.Contains("ElementId: 501", summary, StringComparison.Ordinal);
        Assert.Contains("Вид ещё не создавался", summary, StringComparison.CurrentCulture);
    }
}
