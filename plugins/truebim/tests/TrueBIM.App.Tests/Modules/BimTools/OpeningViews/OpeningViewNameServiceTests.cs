using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewNameServiceTests
{
    [Fact]
    public void Build_ReplacesTokensAndSanitizesInvalidCharacters()
    {
        OpeningViewNameContext context = new(
            123,
            OpeningViewCategoryKeys.Door,
            "Дверь",
            "Family/A",
            "Type:1",
            "Level 1");

        string name = OpeningViewNameService.Build("BIM_{CategoryKey}_{ElementId}_{Family}_{Type}_{Level}", context);

        Assert.Equal("BIM_Door_123_Family_A_Type_1_Level 1", name);
    }

    [Fact]
    public void Sanitize_TrimsAndLimitsLength()
    {
        string longName = new('A', 140);

        string sanitized = OpeningViewNameService.Sanitize($"  {longName}  ");

        Assert.Equal(120, sanitized.Length);
        Assert.DoesNotContain("/", sanitized);
    }
}
