using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelSourceModeWindow : TrueBimWindow
{
    private readonly LintelWizardSourceSelection selection;
    private readonly Button continueButton;
    private readonly TextBlock statusText = new();

    public LintelSourceModeWindow(bool hasCurrentSelection, bool hasExistingItems)
    {
        selection = new LintelWizardSourceSelection(hasCurrentSelection, hasExistingItems);
        continueButton = TrueBimUi.CreatePrimaryButton(
            "Далее: выбрать типоразмеры",
            TrueBimIcon.Apply,
            (_, _) => ConfirmSelection(),
            minWidth: 210);

        Title = "Перемычки — шаг 1 из 4";
        Icon = IconFactory.CreateImage(TrueBimIcon.Lintels, TrueBimTheme.IconSizeRibbon);
        Width = 780;
        Height = 650;
        MinWidth = 680;
        MinHeight = 560;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Откуда взять перемычки",
                "Шаг 1 из 4. Выберите, где искать исходные перемычки. Следующие действия показаны ниже.",
                TrueBimIcon.Lintels),
            commandBar: null,
            body: CreateBody(),
            status: null,
            footer: CreateFooter());

        UpdateState();
    }

    public LintelWizardSourceMode SelectedMode => selection.SelectedMode;

    private UIElement CreateBody()
    {
        StackPanel content = new();
        Border banner = TrueBimUi.CreateInfoBanner(
            "На этом шаге модель Revit не изменяется. После выбора источника откроется список найденных перемычек.");
        banner.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(banner);

        Border nextSteps = TrueBimUi.CreateInfoBanner(
            "Дальше: шаг 2 — отметьте один или несколько типоразмеров; шаг 3 — создайте для них сборки; шаг 4 — выберите файл семейства рамки .rfa и создайте оформленные виды 1:10.");
        nextSteps.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing16);
        content.Children.Add(nextSteps);

        StackPanel options = new();
        foreach (LintelWizardSourceOption option in selection.Options)
        {
            options.Children.Add(CreateOptionCard(option));
        }

        content.Children.Add(new TextBlock
        {
            Text = "Источник перемычек",
            FontSize = TrueBimTheme.SectionTitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = TrueBimBrushes.TextPrimary,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12)
        });
        content.Children.Add(options);
        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private UIElement CreateOptionCard(LintelWizardSourceOption option)
    {
        RadioButton radioButton = new()
        {
            GroupName = "LintelSourceMode",
            IsEnabled = option.IsAvailable,
            IsChecked = option.Mode == selection.SelectedMode,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Tag = option.Mode,
            ToolTip = option.UnavailableReason
        };
        ToolTipService.SetShowOnDisabled(radioButton, true);
        AutomationProperties.SetName(radioButton, option.Title);
        AutomationProperties.SetHelpText(radioButton, option.UnavailableReason ?? option.Description);
        radioButton.Checked += (_, _) =>
        {
            if (radioButton.Tag is LintelWizardSourceMode mode && selection.TrySelect(mode))
            {
                UpdateState();
            }
        };

        StackPanel text = new();
        text.Children.Add(new TextBlock
        {
            Text = option.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = TrueBimTheme.FontSize,
            Foreground = option.IsAvailable ? TrueBimBrushes.TextPrimary : TrueBimBrushes.TextMuted
        });
        text.Children.Add(new TextBlock
        {
            Text = option.UnavailableReason ?? option.Description,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = TrueBimBrushes.TextSecondary
        });
        radioButton.Content = text;

        Grid row = new();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(radioButton);

        Border badge = TrueBimUi.CreateStatusBadge(
            option.StatusText,
            option.IsAvailable ? TrueBimUiSeverity.Success : TrueBimUiSeverity.Neutral);
        badge.Margin = new Thickness(TrueBimTheme.Spacing12, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(badge, 1);
        row.Children.Add(badge);

        Border card = new()
        {
            Background = option.IsAvailable ? TrueBimBrushes.Surface : TrueBimBrushes.SurfaceAlt,
            BorderBrush = TrueBimBrushes.Border,
            BorderThickness = new Thickness(TrueBimTheme.BorderWidth),
            CornerRadius = new CornerRadius(TrueBimTheme.Radius8),
            Padding = new Thickness(TrueBimTheme.Spacing12),
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8),
            ToolTip = option.UnavailableReason,
            Child = row
        };
        return card;
    }

    private UIElement CreateFooter()
    {
        statusText.Foreground = TrueBimBrushes.TextSecondary;
        statusText.TextWrapping = TextWrapping.Wrap;
        statusText.VerticalAlignment = VerticalAlignment.Center;

        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        cancelButton.IsCancel = true;
        cancelButton.ToolTip = "Закрыть мастер без изменений модели.";

        return TrueBimUi.CreateFooter(statusText, continueButton, cancelButton);
    }

    private void UpdateState()
    {
        continueButton.IsEnabled = selection.CanContinue;
        continueButton.ToolTip = selection.CanContinue
            ? $"Продолжить. Источник: {selection.SelectedOption.Title}."
            : selection.SelectedOption.UnavailableReason;
        ToolTipService.SetShowOnDisabled(continueButton, true);
        statusText.Text = $"Выбран источник: {selection.SelectedOption.Title}.";
    }

    private void ConfirmSelection()
    {
        if (!selection.CanContinue)
        {
            return;
        }

        DialogResult = true;
    }
}
