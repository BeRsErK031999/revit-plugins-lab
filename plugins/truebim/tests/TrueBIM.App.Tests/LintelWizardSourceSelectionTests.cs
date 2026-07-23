using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelWizardSourceSelectionTests
{
    [Fact]
    public void Constructor_PrefersCurrentSelectionWhenItExists()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: true,
            hasExistingItems: false);

        Assert.Equal(LintelWizardSourceMode.CurrentSelection, selection.SelectedMode);
        Assert.True(selection.CanContinue);
    }

    [Fact]
    public void Constructor_FallsBackToActiveViewWhenSelectionIsEmpty()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: false,
            hasExistingItems: false);
        LintelWizardSourceOption currentSelection = selection.Options.Single(
            option => option.Mode == LintelWizardSourceMode.CurrentSelection);

        Assert.Equal(LintelWizardSourceMode.ActiveView, selection.SelectedMode);
        Assert.False(currentSelection.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(currentSelection.UnavailableReason));
    }

    [Fact]
    public void TrySelect_RejectsEntireProjectModePlannedForLater()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: true,
            hasExistingItems: true);

        bool selected = selection.TrySelect(LintelWizardSourceMode.EntireProject);

        Assert.False(selected);
        Assert.Equal(LintelWizardSourceMode.CurrentSelection, selection.SelectedMode);
        Assert.False(selection.Options.Single(
            option => option.Mode == LintelWizardSourceMode.EntireProject).IsAvailable);
    }

    [Fact]
    public void TrySelect_AllowsActiveViewEvenWhenRevitHasASelection()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: true,
            hasExistingItems: false);

        bool selected = selection.TrySelect(LintelWizardSourceMode.ActiveView);

        Assert.True(selected);
        Assert.Equal(LintelWizardSourceMode.ActiveView, selection.SelectedMode);
    }

    [Fact]
    public void TrySelect_AllowsExistingTrueBimResultsWhenProjectHasThem()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: false,
            hasExistingItems: true);

        bool selected = selection.TrySelect(LintelWizardSourceMode.ExistingItems);

        Assert.True(selected);
        Assert.Equal(LintelWizardSourceMode.ExistingItems, selection.SelectedMode);
    }

    [Fact]
    public void ExistingTrueBimResults_ExplainEmptyProjectState()
    {
        LintelWizardSourceSelection selection = new(
            hasCurrentSelection: false,
            hasExistingItems: false);
        LintelWizardSourceOption option = selection.Options.Single(
            candidate => candidate.Mode == LintelWizardSourceMode.ExistingItems);

        Assert.False(option.IsAvailable);
        Assert.Equal("Пока пусто", option.StatusText);
        Assert.Contains("после создания первой сборки", option.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }
}
