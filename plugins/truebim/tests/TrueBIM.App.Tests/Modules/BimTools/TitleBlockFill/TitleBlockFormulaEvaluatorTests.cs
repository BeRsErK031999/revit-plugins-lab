using TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.TitleBlockFill;

public sealed class TitleBlockFormulaEvaluatorTests
{
    [Fact]
    public void Evaluate_ReplacesKnownTokensAndKeepsPlainText()
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SheetNumber"] = "A-101",
            ["SheetName"] = "Plan",
            ["Date:yyyy-MM-dd"] = "2026-07-08"
        };

        string result = TitleBlockFormulaEvaluator.Evaluate(
            "{SheetNumber}_{SheetName}_{Date:yyyy-MM-dd}",
            token => values.TryGetValue(token, out string? value) ? value : string.Empty);

        Assert.Equal("A-101_Plan_2026-07-08", result);
    }

    [Fact]
    public void Evaluate_LeavesUnclosedTokenAsLiteralText()
    {
        string result = TitleBlockFormulaEvaluator.Evaluate(
            "Prefix {SheetNumber",
            token => token);

        Assert.Equal("Prefix {SheetNumber", result);
    }

    [Fact]
    public void Evaluate_UsesEmptyStringForBlankTokens()
    {
        string result = TitleBlockFormulaEvaluator.Evaluate(
            "A{}B{   }C",
            token => token);

        Assert.Equal("ABC", result);
    }
}
