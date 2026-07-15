using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelDiagnosticsWindow : TrueBimWindow
{
    public LintelDiagnosticsWindow(LintelDiagnosticResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        Title = "Диагностика перемычек";
        Icon = IconFactory.CreateImage(TrueBimIcon.Info, TrueBimTheme.IconSizeRibbon);
        Width = 760;
        Height = 560;
        MinWidth = 620;
        MinHeight = 440;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        TextBox details = new()
        {
            Text = result.BuildDetails(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Style = TrueBimStyles.CreateTextBoxStyle(),
            Padding = new Thickness(TrueBimTheme.Spacing12)
        };

        Border summary = TrueBimUi.CreateInfoBanner(
            result.BuildSummary(),
            !result.HasCandidates || result.ReadyTypeCount == 0
                ? TrueBimUiSeverity.Warning
                : TrueBimUiSeverity.Info);
        summary.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsCancel = true;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Диагностика перемычек",
                "Подробности источника, типоразмеров и исключённых элементов. Текст можно выделить и скопировать.",
                TrueBimIcon.Info),
            summary,
            details,
            footer: TrueBimUi.CreateFooter(null, closeButton));
    }
}
