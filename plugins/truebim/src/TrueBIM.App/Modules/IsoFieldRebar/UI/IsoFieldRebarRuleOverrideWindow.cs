using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace TrueBIM.App.Modules.IsoFieldRebar.UI;

public sealed class IsoFieldRebarRuleOverrideWindow : TrueBimWindow
{
    private readonly RebarRulePreviewItem item;
    private readonly IsoFieldEngineeringSettings settings;
    private readonly IsoFieldRebarRuleOverrideService overrideService = new();
    private readonly CheckBox includedInput;
    private readonly WpfComboBox reinforcementInput;
    private readonly ContentControl statusHost = new();
    private readonly Button applyButton;

    public IsoFieldRebarRuleOverrideWindow(
        RebarRulePreviewItem item,
        IsoFieldEngineeringSettings settings,
        IReadOnlyList<string> reinforcementOptions,
        IsoFieldRebarRuleOverride? currentOverride)
    {
        this.item = item ?? throw new ArgumentNullException(nameof(item));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        includedInput = new CheckBox
        {
            Content = "Учитывать зону в раскладке",
            IsChecked = currentOverride?.IsIncluded ?? item.IsIncluded,
            Style = TrueBimStyles.CreateCheckBoxStyle(),
            ToolTip = "Если снять флажок, новые стержни зоны не создаются, а ранее созданные модулем попадут в удаление после сравнения."
        };
        reinforcementInput = new WpfComboBox
        {
            ItemsSource = reinforcementOptions
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
            IsEditable = true,
            IsTextSearchEnabled = true,
            Text = currentOverride?.ReinforcementLabel
                ?? item.Rule.ReinforcementLabel
                ?? string.Empty,
            MinWidth = 300,
            MinHeight = TrueBimTheme.ControlHeight32,
            Style = TrueBimStyles.CreateComboBoxStyle(),
            ToolTip = "Выберите распознанное сочетание или введите значение вида d12s200+d16s200."
        };
        applyButton = TrueBimUi.CreatePrimaryButton(
            "Применить настройку",
            TrueBimIcon.Apply,
            (_, _) => ApplyOverride(),
            minWidth: 184);
        applyButton.IsDefault = true;

        includedInput.Checked += (_, _) => RefreshValidation();
        includedInput.Unchecked += (_, _) => RefreshValidation();
        reinforcementInput.SelectionChanged += (_, _) => RefreshValidation();
        reinforcementInput.AddHandler(
            TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => RefreshValidation()));

        Title = "Настройка зоны армирования";
        Icon = IconFactory.CreateImage(TrueBimIcon.IsoFieldRebar, 32);
        Width = 620;
        Height = 470;
        MinWidth = 540;
        MinHeight = 430;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ApplyTrueBimShell(
            header: TrueBimUi.CreateHeader(
                Title,
                "Изменение действует только на текущую рассчитанную раскладку и сбрасывает ранее выполненное сравнение с моделью.",
                TrueBimIcon.Settings),
            commandBar: null,
            body: CreateBody(),
            status: statusHost,
            footer: CreateFooter());
        RefreshValidation();
    }

    public IsoFieldRebarRuleOverride? Result { get; private set; }

    public bool ResetToCalculated { get; private set; }

    private UIElement CreateBody()
    {
        StackPanel content = new();
        content.Children.Add(CreateValueRow("Зона", item.ZoneName));
        content.Children.Add(CreateValueRow(
            "Слой",
            $"{item.Rule.LayerRole?.ToString() ?? "—"} · {item.Rule.PlacementDirection} · {FormatFace(item.Rule.Face)}"));
        content.Children.Add(CreateValueRow(
            "Требуется",
            item.Rule.RequiredAreaSquareCentimetersPerMeter.HasValue
                ? $"{FormatNumber(item.Rule.RequiredAreaSquareCentimetersPerMeter.Value)} см²/м"
                : "—"));
        content.Children.Add(CreateValueRow(
            "Расчётное правило",
            item.Rule.ReinforcementLabel ?? "—"));

        includedInput.Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, TrueBimTheme.Spacing12);
        content.Children.Add(includedInput);
        content.Children.Add(new TextBlock
        {
            Text = "Сочетание диаметр/шаг",
            Foreground = TrueBimBrushes.TextSecondary,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing4)
        });
        content.Children.Add(reinforcementInput);
        TextBlock hint = new()
        {
            Text = "Допустимый формат: d12s200 или d12s200+d16s200. Принятая площадь должна быть не меньше требуемой.",
            Foreground = TrueBimBrushes.TextMuted,
            FontSize = TrueBimTheme.CaptionFontSize,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, TrueBimTheme.Spacing4, 0, 0)
        };
        content.Children.Add(hint);
        return TrueBimUi.CreateSectionCard("Параметры выбранной зоны", content);
    }

    private UIElement CreateFooter()
    {
        Button resetButton = TrueBimUi.CreateSecondaryButton(
            "Вернуть расчётное",
            TrueBimIcon.Refresh,
            (_, _) => ResetOverride(),
            minWidth: 164);
        resetButton.ToolTip = "Удалить ручную настройку этой зоны и восстановить результат автоматического расчёта.";

        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close(),
            minWidth: 104);
        cancelButton.IsCancel = true;
        return TrueBimUi.CreateFooter(null, resetButton, cancelButton, applyButton);
    }

    private void RefreshValidation()
    {
        if (applyButton is null)
        {
            return;
        }

        bool isIncluded = includedInput.IsChecked == true;
        reinforcementInput.IsEnabled = isIncluded;
        IsoFieldRebarRuleOverrideValidation validation = overrideService.Validate(
            item,
            settings,
            isIncluded,
            reinforcementInput.Text ?? string.Empty);
        applyButton.IsEnabled = validation.IsValid;
        if (!isIncluded)
        {
            statusHost.Content = TrueBimUi.CreateInfoBanner(
                "Зона будет исключена. После «Сравнить с моделью» ранее созданная арматура этой зоны отобразится как удаляемая.",
                TrueBimUiSeverity.Warning);
            return;
        }

        if (!validation.IsValid)
        {
            statusHost.Content = TrueBimUi.CreateInfoBanner(
                string.Join(" ", validation.Diagnostics),
                TrueBimUiSeverity.Danger);
            return;
        }

        RebarRule rule = validation.Rule!;
        statusHost.Content = TrueBimUi.CreateInfoBanner(
            $"Принято {FormatNumber(rule.ProvidedAreaSquareCentimetersPerMeter!.Value)} см²/м · {rule.EffectiveComponents.Count} компонент(а).",
            TrueBimUiSeverity.Success);
    }

    private void ApplyOverride()
    {
        bool isIncluded = includedInput.IsChecked == true;
        IsoFieldRebarRuleOverrideValidation validation = overrideService.Validate(
            item,
            settings,
            isIncluded,
            reinforcementInput.Text ?? string.Empty);
        if (!validation.IsValid)
        {
            RefreshValidation();
            return;
        }

        Result = new IsoFieldRebarRuleOverride(
            item.ZoneId,
            isIncluded,
            validation.Rule?.ReinforcementLabel
                ?? item.Rule.ReinforcementLabel
                ?? string.Empty);
        DialogResult = true;
    }

    private void ResetOverride()
    {
        ResetToCalculated = true;
        DialogResult = true;
    }

    private static Grid CreateValueRow(string label, string value)
    {
        Grid row = new()
        {
            Margin = new Thickness(0, 0, 0, TrueBimTheme.Spacing8)
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = TrueBimBrushes.TextMuted
        });
        TextBlock valueText = new()
        {
            Text = value,
            Foreground = TrueBimBrushes.TextPrimary,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static string FormatFace(IsoFieldRebarFace? face)
    {
        return face == IsoFieldRebarFace.Bottom ? "низ" : "верх";
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    }
}
