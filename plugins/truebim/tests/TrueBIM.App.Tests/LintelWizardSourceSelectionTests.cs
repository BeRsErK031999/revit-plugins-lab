using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelWizardSourceSelectionTests
{
    [Fact]
    public void Constructor_PrefersCurrentSelectionWhenItExists()
    {
        LintelWizardSourceSelection selection = new(hasCurrentSelection: true);

        Assert.Equal(LintelWizardSourceMode.CurrentSelection, selection.SelectedMode);
        Assert.True(selection.CanContinue);
    }

    [Fact]
    public void Constructor_FallsBackToActiveViewWhenSelectionIsEmpty()
    {
        LintelWizardSourceSelection selection = new(hasCurrentSelection: false);
        LintelWizardSourceOption currentSelection = selection.Options.Single(
            option => option.Mode == LintelWizardSourceMode.CurrentSelection);

        Assert.Equal(LintelWizardSourceMode.ActiveView, selection.SelectedMode);
        Assert.False(currentSelection.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(currentSelection.UnavailableReason));
    }

    [Theory]
    [InlineData(LintelWizardSourceMode.EntireProject)]
    [InlineData(LintelWizardSourceMode.ExistingItems)]
    public void TrySelect_RejectsModesPlannedForLater(LintelWizardSourceMode mode)
    {
        LintelWizardSourceSelection selection = new(hasCurrentSelection: true);

        bool selected = selection.TrySelect(mode);

        Assert.False(selected);
        Assert.Equal(LintelWizardSourceMode.CurrentSelection, selection.SelectedMode);
        Assert.False(selection.Options.Single(option => option.Mode == mode).IsAvailable);
    }

    [Fact]
    public void TrySelect_AllowsActiveViewEvenWhenRevitHasASelection()
    {
        LintelWizardSourceSelection selection = new(hasCurrentSelection: true);

        bool selected = selection.TrySelect(LintelWizardSourceMode.ActiveView);

        Assert.True(selected);
        Assert.Equal(LintelWizardSourceMode.ActiveView, selection.SelectedMode);
    }
}
