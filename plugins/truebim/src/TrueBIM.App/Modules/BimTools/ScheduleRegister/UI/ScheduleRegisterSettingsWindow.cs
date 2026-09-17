using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;

public sealed class ScheduleRegisterSettingsWindow : TrueBimWindow
{
    private readonly ScheduleRegisterSettingsStorage storage;
    private readonly CheckBox filterEnabledInput;
    private readonly ComboBox parameterInput;
    private readonly TextBox excludedValueInput;
    private readonly TextBox templatePathInput;
    private readonly TextBlock statusText;

    public ScheduleRegisterSettingsWindow(
        ScheduleRegisterSettingsStorage storage,
        IReadOnlyList<string> parameterNames,
        ScheduleRegisterTemplateInspection templateInspection)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        Guard.NotNull(parameterNames, nameof(parameterNames));
        Guard.NotNull(templateInspection, nameof(templateInspection));

        Title = "Настройки ведомости спецификаций";
        Width = 760;
        Height = 590;
        MinWidth = 660;
        MinHeight = 520;
        Icon = IconFactory.CreateImage(TrueBimIcon.Settings, 32);

        ScheduleRegisterSettings settings = storage.Load();
        filterEnabledInput = new CheckBox
        {
            Content = "Фильтровать размещённые спецификации",
            IsChecked = settings.FilterEnabled,
            Style = TrueBimStyles.CreateCheckBoxStyle()
        };
        parameterInput = new ComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemsSource = parameterNames,
            Text = settings.FilterParameterName,
            MinWidth = 300,
            Style = TrueBimStyles.CreateComboBoxStyle()
        };
        excludedValueInput = new TextBox
        {
            Text = settings.ExcludedValue,
            MinWidth = 300,
            Style = TrueBimStyles.CreateTextBoxStyle()
        };
        templatePathInput = new TextBox
        {
            Text = settings.TemplateProjectPath,
            MinWidth = 420,
            Style = TrueBimStyles.CreateTextBoxStyle()
        };
        statusText = new TextBlock
        {
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        filterEnabledInput.Checked += (_, _) => UpdateFilterInputs();
        filterEnabledInput.Unchecked += (_, _) => UpdateFilterInputs();
        UpdateFilterInputs();

        Button saveButton = TrueBimUi.CreatePrimaryButton(
            "Сохранить",
            TrueBimIcon.Apply,
            Save,
            minWidth: 130);
        Button cancelButton = TrueBimUi.CreateSecondaryButton(
            "Отмена",
            TrueBimIcon.Close,
            (_, _) => Close());

        UIElement body = CreateBody(templateInspection);
        ApplyTrueBimShell(
            TrueBimUi.CreateHeader(
                "Ведомость спецификаций",
                "Настройте только исключающий фильтр и источник восстановления эталонной таблицы.",
                TrueBimIcon.ScheduleRegister),
            null,
            body,
            null,
            TrueBimUi.CreateFooter(statusText, cancelButton, saveButton));
    }

    private UIElement CreateBody(ScheduleRegisterTemplateInspection templateInspection)
    {
        StackPanel content = new();
        content.Children.Add(CreateFilterCard());

        Border templateCard = CreateTemplateCard(templateInspection);
        templateCard.Margin = new Thickness(0, TrueBimTheme.Spacing12, 0, 0);
        content.Children.Add(templateCard);

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content
        };
    }

    private Border CreateFilterCard()
    {
        StackPanel content = new();
        content.Children.Add(filterEnabledInput);

        Grid parameterRow = TrueBimUi.CreateSettingsRow(
            "Параметр спецификации",
            "Список собран из параметров видов-спецификаций текущего проекта. Можно ввести имя вручную.",
            parameterInput);
        parameterRow.Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, TrueBimTheme.Spacing12);
        content.Children.Add(parameterRow);
        content.Children.Add(TrueBimUi.CreateSettingsRow(
            "Исключающее значение",
            "Если значение параметра содержит этот текст без учёта регистра, спецификация будет пропущена.",
            excludedValueInput));
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            "Пустое значение не исключает спецификацию. Если выбранного параметра нет, спецификация останется в ведомости, а команда покажет предупреждение."));
        return TrueBimUi.CreateSectionCard("Фильтрация", content);
    }

    private Border CreateTemplateCard(ScheduleRegisterTemplateInspection inspection)
    {
        StackPanel content = new();
        string inspectionText = inspection.IsValid
            ? $"Шаблон «{ScheduleRegisterConstants.TemplateScheduleName}» найден и готов к работе."
            : inspection.CanRepairLocally
                ? "Шаблон имеет правильные заголовок и колонки, но его рабочие строки заполнены или он размещён на листе. "
                  + "Основная команда автоматически создаст чистый шаблон с тем же оформлением."
            : inspection.Exists
                ? "Структура шаблона повреждена: " + string.Join(" ", inspection.Validation.Issues)
                : $"Шаблон «{ScheduleRegisterConstants.TemplateScheduleName}» в проекте не найден.";
        content.Children.Add(TrueBimUi.CreateInfoBanner(
            inspectionText,
            inspection.IsValid ? TrueBimUiSeverity.Success : TrueBimUiSeverity.Warning));

        Grid pathRow = new()
        {
            Margin = new Thickness(0, TrueBimTheme.Spacing16, 0, 0)
        };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel pathInputPanel = new();
        pathInputPanel.Children.Add(TrueBimUi.CreateFieldLabel("Эталонный файл проекта или шаблона (.rvt/.rte)"));
        pathInputPanel.Children.Add(templatePathInput);
        pathRow.Children.Add(pathInputPanel);
        Button browseButton = TrueBimUi.CreateSecondaryButton(
            "Выбрать…",
            TrueBimIcon.Open,
            ChooseTemplate,
            minWidth: 120);
        browseButton.Margin = new Thickness(TrueBimTheme.Spacing8, 20, 0, 0);
        Grid.SetColumn(browseButton, 1);
        pathRow.Children.Add(browseButton);
        content.Children.Add(pathRow);
        content.Children.Add(new TextBlock
        {
            Text = "Путь используется только для восстановления отсутствующего или повреждённого шаблона. "
                   + "При обычном заполнении строк он не нужен: TrueBIM очистит шаблон автоматически. "
                   + "Файл должен быть совместим с запущенной версией Revit и содержать исправную таблицу с тем же именем.",
            Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, TrueBimTheme.Spacing8, 0, 0)
        });
        return TrueBimUi.CreateSectionCard("Контроль шаблона", content);
    }

    private void UpdateFilterInputs()
    {
        bool enabled = filterEnabledInput.IsChecked == true;
        parameterInput.IsEnabled = enabled;
        excludedValueInput.IsEnabled = enabled;
    }

    private void ChooseTemplate(object sender, RoutedEventArgs args)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Выберите эталонный проект или шаблон Revit",
            Filter = "Файлы Revit (*.rte;*.rvt)|*.rte;*.rvt|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            templatePathInput.Text = dialog.FileName;
        }
    }

    private void Save(object sender, RoutedEventArgs args)
    {
        ScheduleRegisterSettings settings = new()
        {
            FilterEnabled = filterEnabledInput.IsChecked == true,
            FilterParameterName = parameterInput.Text,
            ExcludedValue = excludedValueInput.Text,
            TemplateProjectPath = templatePathInput.Text
        };
        IReadOnlyList<string> issues = ScheduleRegisterSettingsStorage.Validate(settings);
        if (!string.IsNullOrWhiteSpace(settings.TemplateProjectPath)
            && !File.Exists(settings.TemplateProjectPath.Trim()))
        {
            issues = [.. issues, "Указанный эталонный файл не найден."];
        }
        else if (!string.IsNullOrWhiteSpace(settings.TemplateProjectPath)
                 && !IsRevitProjectFile(settings.TemplateProjectPath))
        {
            issues = [.. issues, "Эталонным файлом может быть только проект .rvt или шаблон .rte."];
        }

        if (issues.Count > 0)
        {
            statusText.Text = string.Join(" ", issues);
            statusText.Foreground = TrueBimBrushes.Danger;
            return;
        }

        storage.Save(settings);
        DialogResult = true;
        Close();
    }

    private static bool IsRevitProjectFile(string path)
    {
        string extension = Path.GetExtension(path.Trim());
        return string.Equals(extension, ".rte", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".rvt", StringComparison.OrdinalIgnoreCase);
    }
}
