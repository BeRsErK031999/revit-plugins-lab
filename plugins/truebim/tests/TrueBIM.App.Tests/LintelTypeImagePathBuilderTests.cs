using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelTypeImagePathBuilderTests
{
    [Fact]
    public void Build_CreatesProjectScopedPngPath()
    {
        string path = LintelTypeImagePathBuilder.Build(
            @"C:\Local",
            "Корпус 1",
            "TB_Перемычка_ПР-1_101.png");

        Assert.Equal(
            Path.Combine(
                @"C:\Local",
                "TrueBIM",
                "Lintels",
                "Корпус 1",
                "TB_Перемычка_ПР-1_101.png"),
            path);
    }

    [Fact]
    public void Build_SanitizesProjectAndFileTokens()
    {
        string path = LintelTypeImagePathBuilder.Build(
            @"C:\Local",
            "Корпус:1",
            "Перемычка:ПР-1.jpg");

        Assert.EndsWith(
            Path.Combine("Корпус_1", "Перемычка_ПР-1.png"),
            path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Build_UsesFallbacksForEmptyNames()
    {
        string path = LintelTypeImagePathBuilder.Build(@"C:\Local", null, null);

        Assert.EndsWith(
            Path.Combine("Несохраненный проект", "Перемычка.png"),
            path,
            StringComparison.Ordinal);
    }
}
