using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.FinishSchedule.UI;

public sealed class FinishScheduleWindow : TrueBimWindow
{
    private readonly FinishScheduleModuleStatus status;

    public FinishScheduleWindow(FinishScheduleModuleStatus status)
    {
        this.status = status ?? throw new ArgumentNullException(nameof(status));

        Title = "Ведомость отделки";
        Icon = IconFactory.CreateImage(TrueBimIcon.FinishSchedule, TrueBimTheme.IconSizeRibbon);
        Width = 780;
        Height = 640;
        MinWidth = 680;
        MinHeight = 540;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Ведомость отделки",
                "Расчёт и формирование ведомости отделки помещений",
                TrueBimIcon.FinishSchedule),
            commandBar: null,
            body: CreateBody(),
            status: null,
            footer: CreateFooter());
    }

    private UIElement CreateBody()
    {
        StackPanel content = new();

        Border safetyBanner = TrueBimUi.CreateInfoBanner(
            "Первый интеграционный этап. Окно уже подключено к TrueBIM, но расчёт и запись параметров пока отключены. Модель Revit не изменяется.");
        safetyBanner.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(safetyBanner);

        StackPanel documentDetails = new();
        documentDetails.Children.Add(CreateValueRow("Документ", status.DocumentName));
        documentDetails.Children.Add(CreateValueRow(
            "Состояние",
            status.HasActiveDocument
                ? "Можно переходить к обнаружению параметров"
                : "Для следующих этапов потребуется открыть проект Revit"));

        Border documentCard = TrueBimUi.CreateSectionCard("Контекст запуска", documentDetails);
        documentCard.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(documentCard);

        Border readyCard = TrueBimUi.CreateSectionCard(
            "Готово в первом этапе",
            CreateBulletList(status.ReadyCapabilities));
        readyCard.Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing12);
        content.Children.Add(readyCard);

        content.Children.Add(TrueBimUi.CreateSectionCard(
            "Следующие этапы",
            CreateBulletList(status.PendingCapabilities)));

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = content
        };
    }

    private UIElement CreateFooter()
    {
        TextBlock footerStatus = new()
        {
            Text = "Без транзакций и изменений модели",
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        Button generateButton = TrueBimUi.CreatePrimaryButton(
            "Сформировать",
            TrueBimIcon.Apply,
            isEnabled: false,
            minWidth: 140);
        generateButton.ToolTip = "Будет доступно после реализации выбора и проверки совместимых параметров.";
        ToolTipService.SetShowOnDisabled(generateButton, true);

        Button closeButton = TrueBimUi.CreateSecondaryButton(
            "Закрыть",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 110);
        closeButton.IsCancel = true;
        closeButton.ToolTip = "Закрыть окно без изменений модели.";

        return TrueBimUi.CreateFooter(footerStatus, generateButton, closeButton);
    }

    private static UIElement CreateValueRow(string label, string value)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        TextBlock labelText = new()
        {
            Text = label,
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold
        };
        row.Children.Add(labelText);

        TextBlock valueText = new()
        {
            Text = value,
            Foreground = TrueBimBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static UIElement CreateBulletList(IEnumerable<string> items)
    {
        StackPanel list = new();
        foreach (string item in items)
        {
            list.Children.Add(new TextBlock
            {
                Text = $"• {item}",
                Foreground = TrueBimBrushes.TextPrimary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
            });
        }

        return list;
    }
}
