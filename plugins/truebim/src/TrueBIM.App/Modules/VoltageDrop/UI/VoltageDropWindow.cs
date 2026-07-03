using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrueBIM.App.Modules.VoltageDrop.Models;
using TrueBIM.App.Modules.VoltageDrop.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.VoltageDrop.UI;

public sealed class VoltageDropWindow : Window
{
    private readonly VoltageDropCalculationService calculationService;
    private readonly ITrueBimLogger logger;
    private readonly Dictionary<string, TextBox> inputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> outputs = new(StringComparer.Ordinal);
    private readonly ComboBox conductorMaterialInput = CreateReferenceComboBox("Материал жилы кабеля.");
    private readonly ComboBox voltageInput = CreateReferenceComboBox("Напряжение сети для расчета выбранного варианта.");
    private readonly TabControl tableTabs = new();
    private readonly TextBlock statusText = new();
    private readonly StackPanel threePhaseCurrentRows = new();
    private readonly StackPanel singlePhaseCurrentRows = new();
    private readonly Brush inputBackground = new SolidColorBrush(Color.FromRgb(232, 246, 231));
    private readonly Brush inputBorder = new SolidColorBrush(Color.FromRgb(80, 150, 80));
    private readonly Brush invalidInputBackground = new SolidColorBrush(Color.FromRgb(255, 235, 235));
    private readonly Brush invalidInputBorder = new SolidColorBrush(Color.FromRgb(180, 40, 40));
    private bool isLoadingDefaults;

    public VoltageDropWindow(ITrueBimLogger logger)
        : this(new VoltageDropCalculationService(), logger)
    {
    }

    public VoltageDropWindow(VoltageDropCalculationService calculationService, ITrueBimLogger logger)
    {
        this.calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "Расчет потери напряжения";
        Icon = IconFactory.CreateImage(TrueBimIcon.VoltageDrop, 32);
        Width = 1220;
        Height = 820;
        MinWidth = 1080;
        MinHeight = 680;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        isLoadingDefaults = true;
        LoadReferenceSelectors();
        LoadDefaults();
        isLoadingDefaults = false;
        UpdateResults();
        logger.Info("Voltage drop calculation window opened.");
    }

    private UIElement CreateContent()
    {
        DockPanel root = new()
        {
            Margin = new Thickness(12)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        statusText.Margin = new Thickness(0, 0, 0, 8);
        statusText.Foreground = Brushes.DimGray;
        DockPanel.SetDock(statusText, Dock.Top);
        root.Children.Add(statusText);

        tableTabs.Margin = new Thickness(0, 0, 0, 4);
        tableTabs.SelectionChanged += TableTabsSelectionChanged;
        tableTabs.Items.Add(CreateTableTab("Таблица 1: Потеря напряжения", CreateVoltageDropTable(), new TableViewport(1220, 820)));
        tableTabs.Items.Add(CreateTableTab("Таблица 2: Квартиры и лифты", CreateApartmentDemandTable(), new TableViewport(1240, 860)));
        tableTabs.Items.Add(CreateTableTab("Таблица 3: Повышенная комфортность", CreateHighComfortDemandTable(), new TableViewport(1320, 940)));
        tableTabs.Items.Add(CreateTableTab("Таблица 4: Дополнительные формулы", CreateSupplementaryTable(), new TableViewport(1320, 940)));
        tableTabs.SelectedIndex = 0;
        root.Children.Add(tableTabs);

        return root;
    }

    private static TabItem CreateTableTab(string header, UIElement content, TableViewport viewport)
    {
        return new TabItem
        {
            Header = header,
            Tag = viewport,
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content
            }
        };
    }

    private void TableTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, tableTabs))
        {
            return;
        }

        ResizeForSelectedTable();
    }

    private void ResizeForSelectedTable()
    {
        if (tableTabs.SelectedItem is not TabItem { Tag: TableViewport viewport })
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        Width = Math.Min(workArea.Width - 40, Math.Max(MinWidth, viewport.Width));
        Height = Math.Min(workArea.Height - 40, Math.Max(MinHeight, viewport.Height));

        if (!IsLoaded)
        {
            return;
        }

        if (Left + Width > workArea.Right)
        {
            Left = Math.Max(workArea.Left, workArea.Right - Width);
        }

        if (Top + Height > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
        }
    }

    private UIElement CreateFooter()
    {
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        Button referencesButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Preview, "Справочники"),
            MinWidth = 140,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Открыть оцифрованные справочные таблицы и формулы."
        };
        referencesButton.Click += (_, _) => OpenReferences();
        footer.Children.Add(referencesButton);

        Button closeButton = new()
        {
            Content = IconFactory.CreateButtonContent(TrueBimIcon.Close, "Закрыть"),
            MinWidth = 120,
            Height = 32,
            IsCancel = true,
            ToolTip = "Закрыть окно расчета."
        };
        closeButton.Click += (_, _) => Close();
        footer.Children.Add(closeButton);

        return footer;
    }

    private void OpenReferences()
    {
        VoltageDropReferenceWindow window = new(calculationService.ReferenceCatalog)
        {
            Owner = this
        };
        logger.Info("Voltage drop reference window opened.");
        window.ShowDialog();
    }

    private UIElement CreateVoltageDropTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Исходные данные и расчет потери напряжения", 4);
        AddComboRow(grid, 1, "Материал кабеля", conductorMaterialInput, "Выбор материала для коэффициента C");
        AddComboRow(grid, 2, "Напряжение сети", voltageInput, "Выбор напряжения для коэффициента C");
        AddOutputRow(grid, 3, "C выбранного варианта", "selectedCoefficient", "Материал + напряжение");
        AddOutputRow(grid, 4, "ΔU% выбранного варианта", "selectedVoltageDrop", "Момент нагрузки / (C * сечение)");
        AddInputRow(grid, 5, "Коэффициент C для Al, 400 В", "al400");
        AddInputRow(grid, 6, "Коэффициент C для Cu, 400 В", "cu400");
        AddInputRow(grid, 7, "Коэффициент C для Cu, 230 В", "cu230");
        AddInputRow(grid, 8, "Коэффициент C для Al, 230 В", "al230");
        AddInputRow(grid, 9, "Длина линии L, м", "length");
        AddInputRow(grid, 10, "Сечение кабеля F, мм2", "section");
        AddInputRow(grid, 11, "Мощность Pp, кВт", "power");
        AddOutputRow(grid, 12, "Момент нагрузки M", "loadMoment", "Длина линии * мощность");
        AddOutputRow(grid, 13, "ΔU%, Al 400 В", "al400Drop", "Момент нагрузки / (C Al 400 В * сечение)");
        AddOutputRow(grid, 14, "ΔU%, Cu 400 В", "cu400Drop", "Момент нагрузки / (C Cu 400 В * сечение)");
        AddOutputRow(grid, 15, "ΔU%, Cu 230 В", "cu230Drop", "Момент нагрузки / (C Cu 230 В * сечение)");
        AddOutputRow(grid, 16, "ΔU%, Al 230 В", "al230Drop", "Момент нагрузки / (C Al 230 В * сечение)");

        TextBlock threePhaseHeader = CreateSubHeader("Расчет 3Ф тока");
        Grid.SetRow(threePhaseHeader, 17);
        Grid.SetColumnSpan(threePhaseHeader, grid.ColumnDefinitions.Count);
        grid.Children.Add(threePhaseHeader);
        Grid.SetRow(threePhaseCurrentRows, 18);
        Grid.SetColumnSpan(threePhaseCurrentRows, grid.ColumnDefinitions.Count);
        grid.Children.Add(threePhaseCurrentRows);

        TextBlock singlePhaseHeader = CreateSubHeader("Расчет 1Ф тока");
        Grid.SetRow(singlePhaseHeader, 19);
        Grid.SetColumnSpan(singlePhaseHeader, grid.ColumnDefinitions.Count);
        grid.Children.Add(singlePhaseHeader);
        Grid.SetRow(singlePhaseCurrentRows, 20);
        Grid.SetColumnSpan(singlePhaseCurrentRows, grid.ColumnDefinitions.Count);
        grid.Children.Add(singlePhaseCurrentRows);

        return CreateSection("Таблица 1", grid);
    }

    private UIElement CreateApartmentDemandTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Расчет мощности квартир с электроплитами и лифтов от количества этажей", 4);
        AddInputRow(grid, 1, "Кол-во этажей", "stdFloors");
        AddInputRow(grid, 2, "Кол-во квартир", "stdApartmentCount");
        AddInputRow(grid, 3, "Ру., кВт на квартиру", "stdApartmentUnitPower");
        AddOutputRow(grid, 4, "Ко/Руд, кВт", "stdApartmentSpecificDemand", "По количеству квартир");
        AddOutputRow(grid, 5, "Ру.общ., кВт", "stdApartmentInstalledPower", "Кол-во квартир * Ру на квартиру");
        AddInputRow(grid, 6, "Кс квартир", "stdApartmentUsageFactor");
        AddInputRow(grid, 7, "Кс.об квартир", "stdApartmentCoincidenceFactor");
        AddOutputRow(grid, 8, "Рр квартир, кВт", "stdApartmentActivePower", "Кол-во квартир * Ко/Руд * Кс * Кс.об");
        AddInputRow(grid, 9, "cos(ϕ) квартир", "stdApartmentCosPhi");
        AddOutputRow(grid, 10, "Qр квартир, кВАр", "stdApartmentReactivePower", "√(Sр² - Pр²)");
        AddOutputRow(grid, 11, "Sр квартир, кВА", "stdApartmentApparentPower", "Pр / cos(ϕ)");
        AddOutputRow(grid, 12, "Ip квартир, A", "stdApartmentCurrent", "Pр / (0,38 * √3 * cos(ϕ))");
        AddInputRow(grid, 13, "Кол-во лифтов", "stdElevatorCount");
        AddInputRow(grid, 14, "Ру.общ. лифтов, кВт", "stdLiftInstalledPower");
        AddOutputRow(grid, 15, "Кс лифтов", "stdLiftDemandFactor", "По этажам и количеству лифтов");
        AddInputRow(grid, 16, "Кс.об лифтов", "stdLiftCoincidenceFactor");
        AddOutputRow(grid, 17, "Рр лифтов, кВт", "stdLiftActivePower", "Ру.общ. лифтов * Кс * Кс.об");
        AddInputRow(grid, 18, "cos(ϕ) лифтов", "stdLiftCosPhi");
        AddOutputRow(grid, 19, "Qр лифтов, кВАр", "stdLiftReactivePower", "√(Sр² - Pр²)");
        AddOutputRow(grid, 20, "Sр лифтов, кВА", "stdLiftApparentPower", "Pр / cos(ϕ)");
        AddOutputRow(grid, 21, "Ip лифтов, A", "stdLiftCurrent", "Pр / (0,38 * √3 * cos(ϕ))");

        return CreateSection("Таблица 2", grid);
    }

    private UIElement CreateHighComfortDemandTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Расчет мощности квартир повышенной комфортности с электроплитами и лифтов", 4);
        AddInputRow(grid, 1, "Кол-во этажей", "comfortFloors");
        AddInputRow(grid, 2, "Кол-во квартир", "comfortApartmentCount");
        AddInputRow(grid, 3, "Ру., кВт на квартиру", "comfortApartmentUnitPower");
        AddOutputRow(grid, 4, "Кс/Ко по Ру.", "comfortUnitPowerDemandFactor", "По Ру на квартиру");
        AddInputRow(grid, 5, "Кол-во квартир для Кс/Ко", "comfortSpecificDemandApartmentCount");
        AddOutputRow(grid, 6, "Кс/Ко по количеству", "comfortApartmentCountDemandFactor", "По количеству квартир");
        AddOutputRow(grid, 7, "Ру.общ., кВт", "comfortApartmentInstalledPower", "Кол-во квартир * Ру на квартиру");
        AddInputRow(grid, 8, "Кс квартир", "comfortApartmentUsageFactor");
        AddInputRow(grid, 9, "Кс.об квартир", "comfortApartmentCoincidenceFactor");
        AddOutputRow(grid, 10, "Рр квартир, кВт", "comfortApartmentActivePower", "Кол-во квартир * Ру * Кс/Ко по Ру * Кс/Ко по количеству * Кс * Кс.об");
        AddInputRow(grid, 11, "cos(ϕ) квартир", "comfortApartmentCosPhi");
        AddOutputRow(grid, 12, "Qр квартир, кВАр", "comfortApartmentReactivePower", "√(Sр² - Pр²)");
        AddOutputRow(grid, 13, "Sр квартир, кВА", "comfortApartmentApparentPower", "Pр / cos(ϕ)");
        AddOutputRow(grid, 14, "Ip квартир, A", "comfortApartmentCurrent", "Pр / (0,38 * √3 * cos(ϕ))");
        AddInputRow(grid, 15, "Кол-во лифтов", "comfortElevatorCount");
        AddInputRow(grid, 16, "Ру.общ. лифтов, кВт", "comfortLiftInstalledPower");
        AddOutputRow(grid, 17, "Кс лифтов", "comfortLiftDemandFactor", "По этажам и количеству лифтов");
        AddInputRow(grid, 18, "Кс.об лифтов", "comfortLiftCoincidenceFactor");
        AddOutputRow(grid, 19, "Рр лифтов, кВт", "comfortLiftActivePower", "Ру.общ. лифтов * Кс * Кс.об");
        AddInputRow(grid, 20, "cos(ϕ) лифтов", "comfortLiftCosPhi");
        AddOutputRow(grid, 21, "Qр лифтов, кВАр", "comfortLiftReactivePower", "√(Sр² - Pр²)");
        AddOutputRow(grid, 22, "Sр лифтов, кВА", "comfortLiftApparentPower", "Pр / cos(ϕ)");
        AddOutputRow(grid, 23, "Ip лифтов, A", "comfortLiftCurrent", "Pр / (0,38 * √3 * cos(ϕ))");
        AddInputRow(grid, 24, "cos(ϕ) общего по квартирам", "comfortCombinedCosPhi");
        AddOutputRow(grid, 25, "Общее по квартирам Рр, кВт", "comfortCombinedActivePower", "Pр обычных квартир + Pр квартир повышенной комфортности");
        AddOutputRow(grid, 26, "Общее по квартирам Qр, кВАр", "comfortCombinedReactivePower", "√(Sр² - Pр²)");
        AddOutputRow(grid, 27, "Общее по квартирам Sр, кВА", "comfortCombinedApparentPower", "Pр / cos(ϕ)");
        AddOutputRow(grid, 28, "Общее по квартирам Ip, A", "comfortCombinedCurrent", "Pр / (0,38 * √3 * cos(ϕ))");

        return CreateSection("Таблица 3", grid);
    }

    private UIElement CreateSupplementaryTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Дополнительные расчетные блоки", 4);
        AddInputRow(grid, 1, "P 3Ф, кВт", "suppThreePhasePower");
        AddInputRow(grid, 2, "I 3Ф, A", "suppThreePhaseCurrent");
        AddOutputRow(grid, 3, "0,38 * 1,73", "suppThreePhaseVoltageFactor", "Напряжение 3Ф * √3");
        AddOutputRow(grid, 4, "cos(φ) 3Ф", "suppThreePhaseCosPhi", "P 3Ф / (I 3Ф * напряжение 3Ф * √3)");
        AddInputRow(grid, 5, "Pнов 3Ф, кВт", "suppNewThreePhasePower");
        AddOutputRow(grid, 6, "Iнов 3Ф, A", "suppNewThreePhaseCurrent", "Pнов 3Ф / (напряжение 3Ф * √3 * cos(φ))");
        AddInputRow(grid, 7, "P 1Ф, кВт", "suppSinglePhasePower");
        AddInputRow(grid, 8, "I 1Ф, A", "suppSinglePhaseCurrent");
        AddOutputRow(grid, 9, "U 1Ф, кВ", "suppSinglePhaseVoltageFactor", "Напряжение однофазной сети");
        AddOutputRow(grid, 10, "cos(φ) 1Ф", "suppSinglePhaseCosPhi", "P 1Ф / (I 1Ф * U 1Ф)");

        TextBlock legacyHeader = CreateSubHeader("Формулы потери напряжения");
        Grid.SetRow(legacyHeader, 11);
        Grid.SetColumnSpan(legacyHeader, grid.ColumnDefinitions.Count);
        grid.Children.Add(legacyHeader);
        AddInputRow(grid, 12, "ρ1", "suppLegacyResistivity");
        AddInputRow(grid, 13, "l, м", "suppLegacyLength");
        AddInputRow(grid, 14, "s, мм2", "suppLegacySection");
        AddInputRow(grid, 15, "cos(φ)", "suppLegacyCosPhi");
        AddInputRow(grid, 16, "λ", "suppLegacyReactance");
        AddInputRow(grid, 17, "Ip, A", "suppLegacyCurrent");
        AddOutputRow(grid, 18, "sin(φ)", "suppLegacySinPhi", "√(1 - cos²(φ))");
        AddOutputRow(grid, 19, "ΔU%, упрощенная", "suppLegacyDropPercent", "((ρ1 * 10 / s * 0,8 + λ * 10 * 0,6) * Ip / 380) * 100");
        AddOutputRow(grid, 20, "U, В", "suppLegacyVoltageDrop", "(ρ1 * (l / s) * cos(φ) + λ * l * sin(φ)) * Ip");
        AddOutputRow(grid, 21, "U, %", "suppLegacyVoltageDropPercent", "100 * (U / 380)");

        TextBlock lineHeader = CreateSubHeader("Линейная формула");
        Grid.SetRow(lineHeader, 22);
        Grid.SetColumnSpan(lineHeader, grid.ColumnDefinitions.Count);
        grid.Children.Add(lineHeader);
        AddInputRow(grid, 23, "Ip, A", "suppLineCurrent");
        AddInputRow(grid, 24, "L, км", "suppLineLength");
        AddInputRow(grid, 25, "ro", "suppLineResistance");
        AddInputRow(grid, 26, "xo", "suppLineReactance");
        AddInputRow(grid, 27, "cos(φ)", "suppLineCosPhi");
        AddInputRow(grid, 28, "sin(φ)", "suppLineSinPhi");
        AddOutputRow(grid, 29, "ΔU, В", "suppLineVoltageDrop", "1,73 * Ip * L * (ro * cos(φ) + xo * sin(φ))");
        AddOutputRow(grid, 30, "ΔU, %", "suppLineVoltageDropPercent", "ΔU * 100 / 380");

        return CreateSection("Таблица 4", grid);
    }

    private static UIElement CreateSection(string header, UIElement content)
    {
        GroupBox groupBox = new()
        {
            Header = header,
            Content = content,
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(6)
        };
        return groupBox;
    }

    private static Grid CreateTableGrid()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(700) });
        for (int index = 0; index < 32; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        return grid;
    }

    private void AddInputRow(Grid grid, int row, string label, string key)
    {
        AddLabel(grid, row, label);
        TextBox textBox = CreateInputTextBox();
        inputs[key] = textBox;
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
        AddMutedText(grid, row, "Ввод", 2);
    }

    private void AddComboRow(Grid grid, int row, string label, ComboBox comboBox, string note)
    {
        AddLabel(grid, row, label);
        Grid.SetRow(comboBox, row);
        Grid.SetColumn(comboBox, 1);
        grid.Children.Add(comboBox);
        AddMutedText(grid, row, note, 2);
    }

    private void AddOutputRow(Grid grid, int row, string label, string key, string formula)
    {
        AddLabel(grid, row, label);
        TextBlock value = CreateOutputTextBlock();
        outputs[key] = value;
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        AddMutedText(grid, row, formula, 2);
    }

    private static void AddTableHeader(Grid grid, string title, int columnSpan)
    {
        TextBlock textBlock = new()
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(textBlock, 0);
        Grid.SetColumnSpan(textBlock, grid.ColumnDefinitions.Count);
        grid.Children.Add(textBlock);
    }

    private static TextBlock CreateSubHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 3)
        };
    }

    private static void AddLabel(Grid grid, int row, string label)
    {
        TextBlock textBlock = new()
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 3, 6, 3)
        };
        Border cell = CreateTextCell(textBlock);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, 0);
        grid.Children.Add(cell);
    }

    private static void AddMutedText(Grid grid, int row, string text, int column)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 3, 8, 3)
        };
        Border cell = CreateTextCell(textBlock);
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static Border CreateTextCell(UIElement content)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 229)),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = Brushes.White,
            Child = content
        };
    }

    private TextBox CreateInputTextBox()
    {
        TextBox textBox = new()
        {
            Height = 24,
            MinWidth = 120,
            Background = new SolidColorBrush(Color.FromRgb(232, 246, 231)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 150, 80)),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1)
        };
        textBox.TextChanged += (_, _) => UpdateResults();
        return textBox;
    }

    private static ComboBox CreateReferenceComboBox(string toolTip)
    {
        return new ComboBox
        {
            DisplayMemberPath = "DisplayName",
            Height = 24,
            MinWidth = 140,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1),
            ToolTip = toolTip
        };
    }

    private static TextBlock CreateOutputTextBlock()
    {
        return new TextBlock
        {
            MinHeight = 24,
            Background = new SolidColorBrush(Color.FromRgb(229, 242, 249)),
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1)
        };
    }

    private void LoadDefaults()
    {
        VoltageDropInputs voltage = VoltageDropInputs.Default;
        SetInput("al400", voltage.AluminumCoefficient400);
        SetInput("cu400", voltage.CopperCoefficient400);
        SetInput("cu230", voltage.CopperCoefficient230);
        SetInput("al230", voltage.AluminumCoefficient230);
        SetInput("length", voltage.LineLength);
        SetInput("section", voltage.CableSection);
        SetInput("power", voltage.Power);

        ApartmentDemandInputs standard = ApartmentDemandInputs.Default;
        SetInput("stdFloors", standard.Floors);
        SetInput("stdApartmentCount", standard.ApartmentCount);
        SetInput("stdElevatorCount", standard.ElevatorCount);
        SetInput("stdApartmentUnitPower", standard.ApartmentUnitPower);
        SetInput("stdApartmentUsageFactor", standard.ApartmentUsageFactor);
        SetInput("stdApartmentCoincidenceFactor", standard.ApartmentCoincidenceFactor);
        SetInput("stdApartmentCosPhi", standard.ApartmentCosPhi);
        SetInput("stdLiftInstalledPower", standard.LiftInstalledPower);
        SetInput("stdLiftCoincidenceFactor", standard.LiftCoincidenceFactor);
        SetInput("stdLiftCosPhi", standard.LiftCosPhi);

        HighComfortApartmentDemandInputs comfort = HighComfortApartmentDemandInputs.Default;
        SetInput("comfortFloors", comfort.Floors);
        SetInput("comfortApartmentCount", comfort.ApartmentCount);
        SetInput("comfortSpecificDemandApartmentCount", comfort.SpecificDemandApartmentCount);
        SetInput("comfortElevatorCount", comfort.ElevatorCount);
        SetInput("comfortApartmentUnitPower", comfort.ApartmentUnitPower);
        SetInput("comfortApartmentUsageFactor", comfort.ApartmentUsageFactor);
        SetInput("comfortApartmentCoincidenceFactor", comfort.ApartmentCoincidenceFactor);
        SetInput("comfortApartmentCosPhi", comfort.ApartmentCosPhi);
        SetInput("comfortLiftInstalledPower", comfort.LiftInstalledPower);
        SetInput("comfortLiftCoincidenceFactor", comfort.LiftCoincidenceFactor);
        SetInput("comfortLiftCosPhi", comfort.LiftCosPhi);
        SetInput("comfortCombinedCosPhi", comfort.CombinedApartmentCosPhi);

        VoltageDropSupplementaryInputs supplementary = VoltageDropSupplementaryInputs.Default;
        SetInput("suppThreePhasePower", supplementary.ThreePhasePower);
        SetInput("suppThreePhaseCurrent", supplementary.ThreePhaseCurrent);
        SetInput("suppNewThreePhasePower", supplementary.NewThreePhasePower);
        SetInput("suppSinglePhasePower", supplementary.SinglePhasePower);
        SetInput("suppSinglePhaseCurrent", supplementary.SinglePhaseCurrent);
        SetInput("suppLegacyResistivity", supplementary.LegacyResistivity);
        SetInput("suppLegacyLength", supplementary.LegacyLength);
        SetInput("suppLegacySection", supplementary.LegacySection);
        SetInput("suppLegacyCosPhi", supplementary.LegacyCosPhi);
        SetInput("suppLegacyReactance", supplementary.LegacyReactance);
        SetInput("suppLegacyCurrent", supplementary.LegacyCurrent);
        SetInput("suppLineCurrent", supplementary.LineCurrent);
        SetInput("suppLineLength", supplementary.LineLength);
        SetInput("suppLineResistance", supplementary.LineResistance);
        SetInput("suppLineReactance", supplementary.LineReactance);
        SetInput("suppLineCosPhi", supplementary.LineCosPhi);
        SetInput("suppLineSinPhi", supplementary.LineSinPhi);
    }

    private void LoadReferenceSelectors()
    {
        conductorMaterialInput.ItemsSource = new[]
        {
            new ConductorMaterialOption(VoltageDropConductorMaterial.Aluminum, "Al"),
            new ConductorMaterialOption(VoltageDropConductorMaterial.Copper, "Cu")
        };
        conductorMaterialInput.SelectedIndex = 0;
        conductorMaterialInput.SelectionChanged += (_, _) => UpdateResults();

        voltageInput.ItemsSource = new[]
        {
            new VoltageOption(400, "400 В"),
            new VoltageOption(230, "230 В")
        };
        voltageInput.SelectedIndex = 0;
        voltageInput.SelectionChanged += (_, _) => UpdateResults();
    }

    private void UpdateResults()
    {
        if (isLoadingDefaults || inputs.Count == 0)
        {
            return;
        }

        try
        {
            ResetInputValidation();

            VoltageDropResult voltage;
            try
            {
                voltage = calculationService.CalculateVoltageDrop(ReadVoltageInputs());
            }
            catch (VoltageDropValidationException exception)
            {
                ApplyValidationErrors(exception, VoltageInputKeys);
                return;
            }

            ApartmentDemandResult standard;
            try
            {
                standard = calculationService.CalculateApartmentDemand(ReadApartmentInputs());
            }
            catch (VoltageDropValidationException exception)
            {
                ApplyValidationErrors(exception, StandardApartmentInputKeys);
                return;
            }

            HighComfortApartmentDemandResult comfort;
            try
            {
                comfort = calculationService.CalculateHighComfortApartmentDemand(
                    ReadHighComfortInputs(),
                    standard.ApartmentLoad.ActivePower);
            }
            catch (VoltageDropValidationException exception)
            {
                ApplyValidationErrors(exception, HighComfortApartmentInputKeys);
                return;
            }

            VoltageDropSupplementaryResult supplementary;
            try
            {
                supplementary = calculationService.CalculateSupplementary(ReadSupplementaryInputs());
            }
            catch (VoltageDropValidationException exception)
            {
                ApplyValidationErrors(exception, SupplementaryInputKeys);
                return;
            }

            SetOutput("loadMoment", voltage.LoadMoment);
            SetOutput("al400Drop", voltage.Aluminum400DropPercent);
            SetOutput("cu400Drop", voltage.Copper400DropPercent);
            SetOutput("cu230Drop", voltage.Copper230DropPercent);
            SetOutput("al230Drop", voltage.Aluminum230DropPercent);
            VoltageDropSelectedResult selectedVoltageDrop = calculationService.CalculateSelectedVoltageDrop(
                ReadVoltageInputs(),
                GetSelectedMaterial(),
                GetSelectedVoltage());
            SetOutput("selectedCoefficient", selectedVoltageDrop.Coefficient.Coefficient);
            SetOutput("selectedVoltageDrop", selectedVoltageDrop.DropPercent);
            LoadCurrentRows(threePhaseCurrentRows, voltage.ThreePhaseCurrents);
            LoadCurrentRows(singlePhaseCurrentRows, voltage.SinglePhaseCurrents);

            SetOutput("stdApartmentSpecificDemand", standard.ApartmentSpecificDemand);
            SetOutput("stdApartmentInstalledPower", standard.ApartmentInstalledPower);
            SetOutput("stdApartmentActivePower", standard.ApartmentLoad.ActivePower);
            SetOutput("stdApartmentReactivePower", standard.ApartmentLoad.ReactivePower);
            SetOutput("stdApartmentApparentPower", standard.ApartmentLoad.ApparentPower);
            SetOutput("stdApartmentCurrent", standard.ApartmentLoad.Current);
            SetOutput("stdLiftDemandFactor", standard.LiftDemandFactor);
            SetOutput("stdLiftActivePower", standard.LiftLoad.ActivePower);
            SetOutput("stdLiftReactivePower", standard.LiftLoad.ReactivePower);
            SetOutput("stdLiftApparentPower", standard.LiftLoad.ApparentPower);
            SetOutput("stdLiftCurrent", standard.LiftLoad.Current);

            SetOutput("comfortUnitPowerDemandFactor", comfort.UnitPowerDemandFactor);
            SetOutput("comfortApartmentCountDemandFactor", comfort.ApartmentCountDemandFactor);
            SetOutput("comfortApartmentInstalledPower", comfort.ApartmentInstalledPower);
            SetOutput("comfortApartmentActivePower", comfort.ApartmentLoad.ActivePower);
            SetOutput("comfortApartmentReactivePower", comfort.ApartmentLoad.ReactivePower);
            SetOutput("comfortApartmentApparentPower", comfort.ApartmentLoad.ApparentPower);
            SetOutput("comfortApartmentCurrent", comfort.ApartmentLoad.Current);
            SetOutput("comfortLiftDemandFactor", comfort.LiftDemandFactor);
            SetOutput("comfortLiftActivePower", comfort.LiftLoad.ActivePower);
            SetOutput("comfortLiftReactivePower", comfort.LiftLoad.ReactivePower);
            SetOutput("comfortLiftApparentPower", comfort.LiftLoad.ApparentPower);
            SetOutput("comfortLiftCurrent", comfort.LiftLoad.Current);
            SetOutput("comfortCombinedActivePower", comfort.CombinedApartmentLoad.ActivePower);
            SetOutput("comfortCombinedReactivePower", comfort.CombinedApartmentLoad.ReactivePower);
            SetOutput("comfortCombinedApparentPower", comfort.CombinedApartmentLoad.ApparentPower);
            SetOutput("comfortCombinedCurrent", comfort.CombinedApartmentLoad.Current);

            SetOutput("suppThreePhaseVoltageFactor", supplementary.ThreePhaseVoltageFactor);
            SetOutput("suppThreePhaseCosPhi", supplementary.ThreePhaseCosPhi);
            SetOutput("suppSinglePhaseVoltageFactor", supplementary.SinglePhaseVoltageFactor);
            SetOutput("suppSinglePhaseCosPhi", supplementary.SinglePhaseCosPhi);
            SetOutput("suppNewThreePhaseCurrent", supplementary.NewThreePhaseCurrent);
            SetOutput("suppLegacySinPhi", supplementary.LegacySinPhi);
            SetOutput("suppLegacyDropPercent", supplementary.LegacyDropPercent);
            SetOutput("suppLegacyVoltageDrop", supplementary.LegacyVoltageDrop);
            SetOutput("suppLegacyVoltageDropPercent", supplementary.LegacyVoltageDropPercent);
            SetOutput("suppLineVoltageDrop", supplementary.LineVoltageDrop);
            SetOutput("suppLineVoltageDropPercent", supplementary.LineVoltageDropPercent);

            statusText.Text = "Готово";
            statusText.Foreground = Brushes.DimGray;
        }
        catch (VoltageDropValidationException exception)
        {
            ApplyValidationErrors(exception, EmptyInputKeys);
        }
    }

    private VoltageDropInputs ReadVoltageInputs()
    {
        return new VoltageDropInputs(
            Read("al400"),
            Read("cu400"),
            Read("cu230"),
            Read("al230"),
            Read("length"),
            Read("section"),
            Read("power"));
    }

    private ApartmentDemandInputs ReadApartmentInputs()
    {
        return new ApartmentDemandInputs(
            Read("stdFloors"),
            Read("stdApartmentCount"),
            Read("stdElevatorCount"),
            Read("stdApartmentUnitPower"),
            Read("stdApartmentUsageFactor"),
            Read("stdApartmentCoincidenceFactor"),
            Read("stdApartmentCosPhi"),
            Read("stdLiftInstalledPower"),
            Read("stdLiftCoincidenceFactor"),
            Read("stdLiftCosPhi"));
    }

    private HighComfortApartmentDemandInputs ReadHighComfortInputs()
    {
        return new HighComfortApartmentDemandInputs(
            Read("comfortFloors"),
            Read("comfortApartmentCount"),
            Read("comfortSpecificDemandApartmentCount"),
            Read("comfortElevatorCount"),
            Read("comfortApartmentUnitPower"),
            Read("comfortApartmentUsageFactor"),
            Read("comfortApartmentCoincidenceFactor"),
            Read("comfortApartmentCosPhi"),
            Read("comfortLiftInstalledPower"),
            Read("comfortLiftCoincidenceFactor"),
            Read("comfortLiftCosPhi"),
            Read("comfortCombinedCosPhi"));
    }

    private VoltageDropSupplementaryInputs ReadSupplementaryInputs()
    {
        return new VoltageDropSupplementaryInputs(
            Read("suppThreePhasePower"),
            Read("suppThreePhaseCurrent"),
            Read("suppNewThreePhasePower"),
            Read("suppSinglePhasePower"),
            Read("suppSinglePhaseCurrent"),
            Read("suppLegacyResistivity"),
            Read("suppLegacyLength"),
            Read("suppLegacySection"),
            Read("suppLegacyCosPhi"),
            Read("suppLegacyReactance"),
            Read("suppLegacyCurrent"),
            Read("suppLineCurrent"),
            Read("suppLineLength"),
            Read("suppLineResistance"),
            Read("suppLineReactance"),
            Read("suppLineCosPhi"),
            Read("suppLineSinPhi"));
    }

    private double Read(string key)
    {
        string value = inputs[key].Text.Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double currentCultureValue))
        {
            return currentCultureValue;
        }

        string invariantValue = value.Replace(',', '.');
        if (double.TryParse(invariantValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedValue))
        {
            return parsedValue;
        }

        throw new VoltageDropValidationException(
        [
            new VoltageDropValidationError(key, "Проверьте числовые значения в подсвеченных полях ввода.")
        ]);
    }

    private VoltageDropConductorMaterial GetSelectedMaterial()
    {
        return conductorMaterialInput.SelectedItem is ConductorMaterialOption option
            ? option.Material
            : VoltageDropConductorMaterial.Aluminum;
    }

    private double GetSelectedVoltage()
    {
        return voltageInput.SelectedItem is VoltageOption option
            ? option.Voltage
            : 400;
    }

    private void SetInput(string key, double value)
    {
        inputs[key].Text = Format(value);
    }

    private void SetOutput(string key, double value)
    {
        outputs[key].Text = Format(value);
    }

    private static void LoadCurrentRows(StackPanel panel, IReadOnlyList<PhaseCurrentResult> currents)
    {
        panel.Children.Clear();
        foreach (PhaseCurrentResult current in currents)
        {
            Grid row = new()
            {
                Margin = new Thickness(0, 1, 0, 1)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(700) });

            AddCurrentCell(row, 0, current.Label, Brushes.Transparent);
            AddCurrentCell(row, 1, Format(current.Current), new SolidColorBrush(Color.FromRgb(229, 242, 249)));
            AddCurrentCell(row, 2, current.Note, Brushes.Transparent);
            panel.Children.Add(row);
        }
    }

    private static void AddCurrentCell(Grid row, int column, string text, Brush background)
    {
        TextBlock cell = new()
        {
            Text = text,
            Background = background,
            Padding = new Thickness(6, 4, 6, 4),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static string Format(double value)
    {
        return double.IsNaN(value)
            ? "-"
            : value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void ResetInputValidation()
    {
        foreach (TextBox input in inputs.Values)
        {
            input.Background = inputBackground;
            input.BorderBrush = inputBorder;
            input.ToolTip = null;
        }
    }

    private void ApplyValidationErrors(
        VoltageDropValidationException exception,
        IReadOnlyDictionary<string, string> fieldKeyMap)
    {
        foreach (VoltageDropValidationError error in exception.Errors)
        {
            string inputKey = ResolveInputKey(error.FieldKey, fieldKeyMap);
            if (!inputs.TryGetValue(inputKey, out TextBox? input))
            {
                continue;
            }

            input.Background = invalidInputBackground;
            input.BorderBrush = invalidInputBorder;
            input.ToolTip = error.Message;
        }

        statusText.Text = exception.Message;
        statusText.Foreground = Brushes.DarkRed;
    }

    private string ResolveInputKey(string fieldKey, IReadOnlyDictionary<string, string> fieldKeyMap)
    {
        if (inputs.ContainsKey(fieldKey))
        {
            return fieldKey;
        }

        return fieldKeyMap.TryGetValue(fieldKey, out string? inputKey)
            ? inputKey
            : fieldKey;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyInputKeys =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> VoltageInputKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(VoltageDropInputs.AluminumCoefficient400)] = "al400",
            [nameof(VoltageDropInputs.CopperCoefficient400)] = "cu400",
            [nameof(VoltageDropInputs.CopperCoefficient230)] = "cu230",
            [nameof(VoltageDropInputs.AluminumCoefficient230)] = "al230",
            [nameof(VoltageDropInputs.LineLength)] = "length",
            [nameof(VoltageDropInputs.CableSection)] = "section",
            [nameof(VoltageDropInputs.Power)] = "power"
        };

    private static readonly IReadOnlyDictionary<string, string> StandardApartmentInputKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ApartmentDemandInputs.Floors)] = "stdFloors",
            [nameof(ApartmentDemandInputs.ApartmentCount)] = "stdApartmentCount",
            [nameof(ApartmentDemandInputs.ElevatorCount)] = "stdElevatorCount",
            [nameof(ApartmentDemandInputs.ApartmentUnitPower)] = "stdApartmentUnitPower",
            [nameof(ApartmentDemandInputs.ApartmentUsageFactor)] = "stdApartmentUsageFactor",
            [nameof(ApartmentDemandInputs.ApartmentCoincidenceFactor)] = "stdApartmentCoincidenceFactor",
            [nameof(ApartmentDemandInputs.ApartmentCosPhi)] = "stdApartmentCosPhi",
            [nameof(ApartmentDemandInputs.LiftInstalledPower)] = "stdLiftInstalledPower",
            [nameof(ApartmentDemandInputs.LiftCoincidenceFactor)] = "stdLiftCoincidenceFactor",
            [nameof(ApartmentDemandInputs.LiftCosPhi)] = "stdLiftCosPhi"
        };

    private static readonly IReadOnlyDictionary<string, string> HighComfortApartmentInputKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(HighComfortApartmentDemandInputs.Floors)] = "comfortFloors",
            [nameof(HighComfortApartmentDemandInputs.ApartmentCount)] = "comfortApartmentCount",
            [nameof(HighComfortApartmentDemandInputs.SpecificDemandApartmentCount)] = "comfortSpecificDemandApartmentCount",
            [nameof(HighComfortApartmentDemandInputs.ElevatorCount)] = "comfortElevatorCount",
            [nameof(HighComfortApartmentDemandInputs.ApartmentUnitPower)] = "comfortApartmentUnitPower",
            [nameof(HighComfortApartmentDemandInputs.ApartmentUsageFactor)] = "comfortApartmentUsageFactor",
            [nameof(HighComfortApartmentDemandInputs.ApartmentCoincidenceFactor)] = "comfortApartmentCoincidenceFactor",
            [nameof(HighComfortApartmentDemandInputs.ApartmentCosPhi)] = "comfortApartmentCosPhi",
            [nameof(HighComfortApartmentDemandInputs.LiftInstalledPower)] = "comfortLiftInstalledPower",
            [nameof(HighComfortApartmentDemandInputs.LiftCoincidenceFactor)] = "comfortLiftCoincidenceFactor",
            [nameof(HighComfortApartmentDemandInputs.LiftCosPhi)] = "comfortLiftCosPhi",
            [nameof(HighComfortApartmentDemandInputs.CombinedApartmentCosPhi)] = "comfortCombinedCosPhi"
        };

    private static readonly IReadOnlyDictionary<string, string> SupplementaryInputKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(VoltageDropSupplementaryInputs.ThreePhasePower)] = "suppThreePhasePower",
            [nameof(VoltageDropSupplementaryInputs.ThreePhaseCurrent)] = "suppThreePhaseCurrent",
            [nameof(VoltageDropSupplementaryInputs.NewThreePhasePower)] = "suppNewThreePhasePower",
            [nameof(VoltageDropSupplementaryInputs.SinglePhasePower)] = "suppSinglePhasePower",
            [nameof(VoltageDropSupplementaryInputs.SinglePhaseCurrent)] = "suppSinglePhaseCurrent",
            [nameof(VoltageDropSupplementaryInputs.LegacyResistivity)] = "suppLegacyResistivity",
            [nameof(VoltageDropSupplementaryInputs.LegacyLength)] = "suppLegacyLength",
            [nameof(VoltageDropSupplementaryInputs.LegacySection)] = "suppLegacySection",
            [nameof(VoltageDropSupplementaryInputs.LegacyCosPhi)] = "suppLegacyCosPhi",
            [nameof(VoltageDropSupplementaryInputs.LegacyReactance)] = "suppLegacyReactance",
            [nameof(VoltageDropSupplementaryInputs.LegacyCurrent)] = "suppLegacyCurrent",
            [nameof(VoltageDropSupplementaryInputs.LineCurrent)] = "suppLineCurrent",
            [nameof(VoltageDropSupplementaryInputs.LineLength)] = "suppLineLength",
            [nameof(VoltageDropSupplementaryInputs.LineResistance)] = "suppLineResistance",
            [nameof(VoltageDropSupplementaryInputs.LineReactance)] = "suppLineReactance",
            [nameof(VoltageDropSupplementaryInputs.LineCosPhi)] = "suppLineCosPhi",
            [nameof(VoltageDropSupplementaryInputs.LineSinPhi)] = "suppLineSinPhi"
        };

    private sealed record ConductorMaterialOption(VoltageDropConductorMaterial Material, string DisplayName);

    private sealed record VoltageOption(double Voltage, string DisplayName);

    private sealed record TableViewport(double Width, double Height);
}
