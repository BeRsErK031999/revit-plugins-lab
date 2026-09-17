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

    [Fact]
    public void Build_ShortensLongNamesAndKeepsPathBelowWindowsLimit()
    {
        string projectName = new('П', 120);
        string imageFileName = $"{new string('И', 180)}.png";

        string path = LintelTypeImagePathBuilder.Build(
            @"C:\Users\User\AppData\Local",
            projectName,
            imageFileName);

        Assert.InRange(path.Length, 1, 240);
        Assert.InRange(Path.GetFileName(Path.GetDirectoryName(path)!).Length, 1, 64);
        Assert.InRange(Path.GetFileNameWithoutExtension(path).Length, 1, 96);
    }

    [Fact]
    public void Build_UsesHashToDistinguishLongNamesWithSamePrefix()
    {
        string sharedPrefix = new('И', 180);

        string first = LintelTypeImagePathBuilder.Build(
            @"C:\Local",
            "Проект",
            $"{sharedPrefix}A.png");
        string second = LintelTypeImagePathBuilder.Build(
            @"C:\Local",
            "Проект",
            $"{sharedPrefix}B.png");

        Assert.NotEqual(first, second);
    }
}
