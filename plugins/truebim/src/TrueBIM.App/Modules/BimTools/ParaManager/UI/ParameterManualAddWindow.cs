using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.UI;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.App.Modules.BimTools.ParaManager.UI;

public sealed class ParameterManualAddWindow : TrueBimWindow
{
    private static readonly string[] BindingTypes = ["Instance", "Type"];
    private static readonly string[] GroupOptions = ["Identity Data", "Text", "Dimensions", "Data", "General", "Other"];
    private static readonly string[] DataTypeOptions = ["Text", "Integer", "Number", "YesNo", "Length", "Area", "Volume"];

    private readonly IReadOnlyList<string> categoryNames;
    private readonly WpfTextBox parameterNameInput = new();
    private readonly WpfTextBox sharedGroupInput = new();
    private readonly ComboBox bindingTypeInput = new();
    private readonly WpfTextBox categoriesInput = new();
    private readonly ComboBox groupUnderInput = new();
    private readonly ComboBox dataTypeInput = new();
    private readonly WpfTextBox descriptionInput = new();
    private readonly TextBlock validationText = new();
    private List<string> selectedCategoryNames;

    public ParameterManualAddWindow(IReadOnlyList<string> categoryNames, IReadOnlyList<string> selectedCategoryNames)
    {
        this.categoryNames = categoryNames ?? throw new ArgumentNullException(nameof(categoryNames));
        this.selectedCategoryNames = selectedCategoryNames?
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToList() ?? [];

        Title = "Добавить параметр проекта";
        Icon = IconFactory.CreateImage(TrueBimIcon.Parameters, 32);
        Width = 580;
        Height = 560;
        MinWidth = 520;
        MinHeight = 520;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();
        UpdateCategoryText();
    }

    public ParameterImportRow? CreatedRow { get; private set; }

    private UIElement CreateContent()
    {
        WpfGrid root = new()
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel header = new();
        header.Children.Add(new TextBlock
        {
            Text = "Новый project/shared parameter",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Параметр будет добавлен в список проверки ParaManager. Запись в модель произойдёт только после кнопки «Применить импорт».",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12)
        });
        root.Children.Add(header);

        StackPanel form = new();
        WpfGrid.SetRow(form, 1);
        root.Children.Add(form);

        parameterNameInput.ToolTip = "Имя параметра, которое появится в Revit. Например: BIM_Раздел.";
        form.Children.Add(CreateLabeledControl("Имя параметра", parameterNameInput));

        sharedGroupInput.Text = "BIM";
        sharedGroupInput.ToolTip = "Группа внутри shared parameter .txt. Если группы нет, ParaManager создаст её.";
        form.Children.Add(CreateLabeledControl("Группа shared parameters", sharedGroupInput));

        bindingTypeInput.ItemsSource = BindingTypes;
        bindingTypeInput.SelectedItem = "Instance";
        bindingTypeInput.ToolTip = "Instance добавляет параметр экземплярам элементов, Type добавляет параметр типам.";
        form.Children.Add(CreateLabeledControl("Привязка", bindingTypeInput));

        DockPanel categoriesPanel = new()
        {
            LastChildFill = true
        };
        Button categoriesButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Open, "Категории"),
            Height = 30,
            MinWidth = 125,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Выбрать категории Revit, к которым будет привязан параметр."
        };
        categoriesButton.Click += (_, _) => ChooseCategories();
        DockPanel.SetDock(categoriesButton, Dock.Right);
        categoriesPanel.Children.Add(categoriesButton);

        categoriesInput.IsReadOnly = true;
        categoriesInput.Height = 30;
        categoriesInput.VerticalContentAlignment = VerticalAlignment.Center;
        categoriesInput.ToolTip = "Категории, которые получат этот параметр в проекте.";
        categoriesPanel.Children.Add(categoriesInput);
        form.Children.Add(CreateLabeledControl("Категории", categoriesPanel));

        groupUnderInput.ItemsSource = GroupOptions;
        groupUnderInput.SelectedItem = "Identity Data";
        groupUnderInput.IsEditable = true;
        groupUnderInput.ToolTip = "Раздел палитры свойств Revit, где будет показан параметр.";
        form.Children.Add(CreateLabeledControl("Раздел свойств", groupUnderInput));

        dataTypeInput.ItemsSource = DataTypeOptions;
        dataTypeInput.SelectedItem = "Text";
        dataTypeInput.IsEditable = true;
        dataTypeInput.ToolTip = "Тип данных параметра: Text, YesNo, Number, Length и другие поддержанные типы.";
        form.Children.Add(CreateLabeledControl("Тип данных", dataTypeInput));

        descriptionInput.AcceptsReturn = true;
        descriptionInput.Height = 70;
        descriptionInput.TextWrapping = TextWrapping.Wrap;
        descriptionInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        descriptionInput.ToolTip = "Описание попадёт в definition shared parameter и поможет отличать параметры в стандарте.";
        form.Children.Add(CreateLabeledControl("Описание", descriptionInput));

        validationText.Foreground = Brushes.DarkRed;
        validationText.TextWrapping = TextWrapping.Wrap;
        validationText.Margin = new Thickness(0, 8, 0, 8);
        WpfGrid.SetRow(validationText, 2);
        root.Children.Add(validationText);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Button addButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Apply, "Добавить"),
            MinWidth = 120,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Добавить параметр в список импорта без записи в модель."
        };
        addButton.Click += (_, _) => AddParameter();
        footer.Children.Add(addButton);

        Button cancelButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Отмена"),
            MinWidth = 110,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть окно без добавления параметра."
        };
        cancelButton.Click += (_, _) => DialogResult = false;
        footer.Children.Add(cancelButton);
        WpfGrid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private void ChooseCategories()
    {
        ParameterCategorySelectionWindow window = new(categoryNames, selectedCategoryNames)
        {
            Owner = this
        };
        if (window.ShowDialog() != true)
        {
            return;
        }

        selectedCategoryNames = window.SelectedCategoryNames.ToList();
        UpdateCategoryText();
    }

    private void AddParameter()
    {
        validationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(parameterNameInput.Text))
        {
            validationText.Text = "Укажите имя параметра.";
            parameterNameInput.Focus();
            return;
        }

        if (selectedCategoryNames.Count == 0)
        {
            validationText.Text = "Выберите хотя бы одну категорию.";
            return;
        }

        CreatedRow = new ParameterImportRow(
            0,
            parameterNameInput.Text,
            sharedGroupInput.Text,
            GetComboText(bindingTypeInput),
            string.Join(",", selectedCategoryNames),
            GetComboText(groupUnderInput),
            GetComboText(dataTypeInput),
            "true",
            "true",
            descriptionInput.Text);
        DialogResult = true;
    }

    private void UpdateCategoryText()
    {
        categoriesInput.Text = selectedCategoryNames.Count == 0
            ? "Категории не выбраны."
            : string.Join(", ", selectedCategoryNames);
    }

    private static string GetComboText(ComboBox comboBox)
    {
        return string.IsNullOrWhiteSpace(comboBox.Text)
            ? comboBox.SelectedItem?.ToString() ?? string.Empty
            : comboBox.Text;
    }

    private static UIElement CreateLabeledControl(string label, FrameworkElement control)
    {
        StackPanel panel = new()
        {
            Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        if (control is Control input)
        {
            input.Height = input.Height > 0 ? input.Height : 30;
            input.VerticalContentAlignment = VerticalAlignment.Center;
        }

        panel.Children.Add(control);
        return panel;
    }
}
