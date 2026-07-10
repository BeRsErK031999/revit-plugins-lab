using TrueBIM.App.Modules.SheetNumbering.Rules;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Rules;

public sealed class NumberingRulesTests
{
    [Fact]
    public void FormatNumber_AppliesPrefixSuffixStartAndPadding()
    {
        NumberingRules rules = new("A-", "-X", 1, 1, 3);

        string result = rules.FormatNumber(0);

        Assert.Equal("A-001-X", result);
    }

    [Fact]
    public void FormatNumber_AppliesIncrementByIndex()
    {
        NumberingRules rules = new("S", string.Empty, 10, 5, 2);

        string result = rules.FormatNumber(2);

        Assert.Equal("S20", result);
    }

    [Fact]
    public void FormatNumber_AllowsNoPadding()
    {
        NumberingRules rules = new(string.Empty, "A", 7, 1, 0);

        string result = rules.FormatNumber(0);

        Assert.Equal("7A", result);
    }

    [Fact]
    public void FormatNumber_NoPaddingProducesPlainSequence()
    {
        NumberingRules rules = new(string.Empty, string.Empty, 1, 1, 0);

        string[] result = Enumerable.Range(0, 3)
            .Select(rules.FormatNumber)
            .ToArray();

        Assert.Equal(["1", "2", "3"], result);
    }

    [Fact]
    public void FormatNumber_RejectsNegativeIndex()
    {
        NumberingRules rules = new(string.Empty, string.Empty, 1, 1, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => rules.FormatNumber(-1));
    }

    [Fact]
    public void FormatNumber_RejectsZeroIncrement()
    {
        NumberingRules rules = new(string.Empty, string.Empty, 1, 0, 1);

        Assert.Throws<InvalidOperationException>(() => rules.FormatNumber(0));
    }

    [Fact]
    public void FormatNumber_RejectsNegativePadding()
    {
        NumberingRules rules = new(string.Empty, string.Empty, 1, 1, -1);

        Assert.Throws<InvalidOperationException>(() => rules.FormatNumber(0));
    }
}
