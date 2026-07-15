using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelDiagnosticReportBuilderTests
{
    private readonly LintelDiagnosticReportBuilder builder = new();

    [Fact]
    public void Build_GroupsByTypeAndSelectsReadyRepresentative()
    {
        LintelInstanceSnapshot notReady = CreateInstance(20, 100, false, []);
        LintelInstanceSnapshot ready = CreateInstance(
            10,
            100,
            false,
            [new LintelNestedComponentSnapshot(11, "Обобщенные модели", "Уголок", "L75", true)]);

        LintelDiagnosticResult result = builder.Build(
            LintelDiagnosticSource.Selection,
            [notReady, ready]);

        LintelTypeDiagnostic type = Assert.Single(result.Types);
        Assert.Equal(2, type.InstanceCount);
        Assert.Equal(1, type.ReadyInstanceCount);
        Assert.Equal(10, type.RepresentativeElementId);
        Assert.Equal(1, type.RepresentativeGeometryNestedComponentCount);
        Assert.Equal([11], type.RepresentativeAssemblyMemberIds);
        Assert.Contains("часть экземпляров", Assert.Single(type.Diagnostics), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void Build_ReportsMissingSharedComponents()
    {
        LintelDiagnosticResult result = builder.Build(
            LintelDiagnosticSource.ActiveView,
            [CreateInstance(30, 200, false, [])]);

        LintelTypeDiagnostic type = Assert.Single(result.Types);
        Assert.False(type.IsAssemblyReady);
        Assert.Contains("общими", Assert.Single(type.Diagnostics), StringComparison.CurrentCultureIgnoreCase);
        Assert.Contains("Модель Revit не изменялась", result.BuildSummary(), StringComparison.CurrentCulture);
    }

    [Fact]
    public void Build_DoesNotTreatParentOnlyGeometryAsAssemblyReady()
    {
        LintelDiagnosticResult result = builder.Build(
            LintelDiagnosticSource.Selection,
            [CreateInstance(40, 300, true, [])]);

        LintelTypeDiagnostic type = Assert.Single(result.Types);
        Assert.False(type.IsAssemblyReady);
        Assert.Contains("родительском", Assert.Single(type.Diagnostics), StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void Build_IncludesExclusionReasonsInDetails()
    {
        LintelDiagnosticResult result = builder.Build(
            LintelDiagnosticSource.Selection,
            Array.Empty<LintelInstanceSnapshot>(),
            [new LintelExcludedElement(55, "Стена", "элемент не является экземпляром семейства")]);

        Assert.False(result.HasCandidates);
        Assert.Contains("ID 55", result.BuildDetails(), StringComparison.Ordinal);
        Assert.Contains("не является", result.BuildDetails(), StringComparison.CurrentCultureIgnoreCase);
    }

    private static LintelInstanceSnapshot CreateInstance(
        long elementId,
        long typeId,
        bool hasOwnGeometry,
        IReadOnlyList<LintelNestedComponentSnapshot> components)
    {
        return new LintelInstanceSnapshot(
            elementId,
            typeId,
            "Перемычки",
            "ПР-1",
            hasOwnGeometry,
            components);
    }
}
