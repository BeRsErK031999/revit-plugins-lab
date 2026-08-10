using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ParameterAudit;

public sealed class ParameterAuditRuleEvaluatorTests
{
    private readonly ParameterAuditRuleEvaluator evaluator = new();

    [Fact]
    public void Evaluate_RejectsWhitespaceButAcceptsZero()
    {
        ParameterAuditRule rule = Rule(required: true);
        ParameterAuditElementSnapshot element = Element();

        ParameterAuditResultRow? whitespace = evaluator.Evaluate(
            rule,
            element,
            new ParameterAuditValueSnapshot(true, false, true, "   ", "   ", null));
        ParameterAuditResultRow? zero = evaluator.Evaluate(
            rule,
            element,
            new ParameterAuditValueSnapshot(true, false, true, "0", "0", 0));

        Assert.Equal(ParameterAuditIssueCode.EmptyValue, whitespace?.IssueCode);
        Assert.Null(zero);
    }

    [Fact]
    public void Evaluate_ReportsMissingRequiredParameter()
    {
        ParameterAuditResultRow? result = evaluator.Evaluate(
            Rule(required: true),
            Element(),
            ParameterAuditValueSnapshot.Missing);

        Assert.Equal(ParameterAuditIssueCode.MissingParameter, result?.IssueCode);
    }

    [Fact]
    public void Evaluate_AcceptsExpectedOrAllowedValueIgnoringCase()
    {
        ParameterAuditRule expectedRule = Rule(expected: "АР");
        ParameterAuditRule allowedRule = Rule(allowed: ["EI30", "EI60"]);
        ParameterAuditElementSnapshot element = Element();

        Assert.Null(evaluator.Evaluate(expectedRule, element, Value("ар")));
        Assert.Null(evaluator.Evaluate(allowedRule, element, Value("ei60")));
    }

    [Fact]
    public void Evaluate_ValidatesRegexAndNumericRange()
    {
        ParameterAuditRule regexRule = Rule(pattern: "^[А-Я]{2}-[0-9]+$");
        ParameterAuditRule rangeRule = Rule(minimum: 10, maximum: 20);
        ParameterAuditElementSnapshot element = Element();

        ParameterAuditResultRow? regexResult = evaluator.Evaluate(regexRule, element, Value("wrong"));
        ParameterAuditResultRow? rangeResult = evaluator.Evaluate(
            rangeRule,
            element,
            new ParameterAuditValueSnapshot(true, false, true, "25", "25", 25));

        Assert.Equal(ParameterAuditIssueCode.PatternMismatch, regexResult?.IssueCode);
        Assert.Equal(ParameterAuditIssueCode.AboveMaximum, rangeResult?.IssueCode);
    }

    [Fact]
    public void MatchesTarget_SupportsCategoryFamilyAndTypeWildcards()
    {
        ParameterAuditRule rule = Rule(category: "Двер*", family: "Door-*", type: "EI??");
        ParameterAuditElementSnapshot element = Element(
            category: "Двери",
            family: "Door-Metal",
            type: "EI60");

        Assert.True(evaluator.MatchesTarget(rule, element));
    }

    [Fact]
    public void Evaluate_ReportsAmbiguousNameLookup()
    {
        ParameterAuditResultRow? result = evaluator.Evaluate(
            Rule(),
            Element(),
            new ParameterAuditValueSnapshot(true, true, false, string.Empty, string.Empty, null));

        Assert.Equal(ParameterAuditIssueCode.AmbiguousParameter, result?.IssueCode);
    }

    [Fact]
    public void Evaluate_PreservesElementIdentityForRevitNavigation()
    {
        ParameterAuditElementSnapshot element = new(
            "Linked model",
            true,
            42,
            "linked-unique-id",
            "Стены",
            "Basic Wall",
            "200 мм",
            ParameterAuditScope.Instance,
            700);

        ParameterAuditResultRow? result = evaluator.Evaluate(
            Rule(required: true),
            element,
            ParameterAuditValueSnapshot.Missing);

        Assert.NotNull(result);
        Assert.Equal("linked-unique-id", result.UniqueId);
        Assert.Equal(700, result.HostLinkInstanceId);
        Assert.True(result.CanNavigateInRevit);
        Assert.Equal("Показать связь", result.NavigationActionDisplay);
    }

    [Fact]
    public void MatchesSelection_UsesParameterValueInsteadOfCategory()
    {
        ParameterAuditRule rule = Rule() with
        {
            CategoryPattern = "*",
            SelectionParameterName = "Описание",
            SelectionExpectedValue = "Стены, перегородки"
        };

        Assert.True(evaluator.MatchesSelection(rule, Value("стены, перегородки")));
        Assert.False(evaluator.MatchesSelection(rule, Value("Двери")));
        Assert.False(evaluator.MatchesSelection(rule, ParameterAuditValueSnapshot.Missing));
    }

    private static ParameterAuditRule Rule(
        bool required = true,
        string expected = "",
        IReadOnlyList<string>? allowed = null,
        string pattern = "",
        double? minimum = null,
        double? maximum = null,
        string category = "Стены",
        string family = "*",
        string type = "*")
    {
        return new ParameterAuditRule(
            2,
            "RULE_1",
            true,
            category,
            family,
            type,
            "BIM_Раздел",
            null,
            string.Empty,
            ParameterAuditScope.Instance,
            required,
            expected,
            allowed ?? [],
            pattern,
            minimum,
            maximum,
            false,
            ParameterAuditSeverity.Error,
            string.Empty);
    }

    private static ParameterAuditElementSnapshot Element(
        string category = "Стены",
        string family = "Basic Wall",
        string type = "200 мм")
    {
        return new ParameterAuditElementSnapshot(
            "Model",
            false,
            42,
            "uid",
            category,
            family,
            type,
            ParameterAuditScope.Instance);
    }

    private static ParameterAuditValueSnapshot Value(string value)
    {
        return new ParameterAuditValueSnapshot(true, false, true, value, value, null);
    }
}
