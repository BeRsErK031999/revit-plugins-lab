using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class FamilyFormulaDependencyParserTests
{
    private readonly FamilyFormulaDependencyParser parser = new();

    [Theory]
    [InlineData("Ширина * 2", "Ширина")]
    [InlineData("[Высота чистая] + Отступ", "Высота чистая")]
    [InlineData("if(Марка_типа = 1, 2, 3)", "Марка_типа")]
    public void ClassifyReference_FindsExactParameterTokens(string formula, string parameterName)
    {
        Assert.Equal(DetectionConfidence.Exact, parser.ClassifyReference(formula, parameterName));
    }

    [Fact]
    public void ClassifyReference_DoesNotTreatSimilarNameAsExact()
    {
        DetectionConfidence confidence = parser.ClassifyReference("Ширина_проема * 2", "Ширина");

        Assert.Equal(DetectionConfidence.Probable, confidence);
    }

    [Fact]
    public void ExtractParameterReferences_ReturnsOnlyKnownExactTokens()
    {
        IReadOnlyList<string> references = parser.ExtractParameterReferences(
            "[Высота чистая] * Ширина + 10",
            ["Высота", "Высота чистая", "Ширина", "Длина"]);

        Assert.Equal(["Высота чистая", "Ширина"], references);
    }
}
