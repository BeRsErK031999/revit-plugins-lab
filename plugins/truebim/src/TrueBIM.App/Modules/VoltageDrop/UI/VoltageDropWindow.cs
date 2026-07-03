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
        Width = 1100;
        Height = 760;
        MinWidth = 980;
        MinHeight = 640;
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
            Margin = new Thickness(20)
        };

        UIElement footer = CreateFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        statusText.Margin = new Thickness(0, 0, 0, 12);
        statusText.Foreground = Brushes.DimGray;
        DockPanel.SetDock(statusText, Dock.Top);
        root.Children.Add(statusText);

        ScrollViewer scrollViewer = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        StackPanel content = new();
        content.Children.Add(CreateVoltageDropTable());
        content.Children.Add(CreateApartmentDemandTable());
        content.Children.Add(CreateHighComfortDemandTable());
        scrollViewer.Content = content;
        root.Children.Add(scrollViewer);

        return root;
    }

    private UIElement CreateFooter()
    {
        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

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

    private UIElement CreateVoltageDropTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Исходные данные и расчет потери напряжения", 4);
        AddComboRow(grid, 1, "Материал кабеля", conductorMaterialInput, "Справочник коэффициентов C");
        AddComboRow(grid, 2, "Напряжение сети", voltageInput, "Справочник коэффициентов C");
        AddOutputRow(grid, 3, "C выбранного варианта", "selectedCoefficient", "Справочник C");
        AddOutputRow(grid, 4, "ΔU% выбранного варианта", "selectedVoltageDrop", "M/(C*F)");
        AddInputRow(grid, 5, "Коэффициент C для Al, 400 В", "al400");
        AddInputRow(grid, 6, "Коэффициент C для Cu, 400 В", "cu400");
        AddInputRow(grid, 7, "Коэффициент C для Cu, 230 В", "cu230");
        AddInputRow(grid, 8, "Коэффициент C для Al, 230 В", "al230");
        AddInputRow(grid, 9, "Длина линии L, м", "length");
        AddInputRow(grid, 10, "Сечение кабеля F, мм2", "section");
        AddInputRow(grid, 11, "Мощность Pp, кВт", "power");
        AddOutputRow(grid, 12, "Момент нагрузки M", "loadMoment", "B6*B8");
        AddOutputRow(grid, 13, "ΔU%, Al 400 В", "al400Drop", "B9/(B2*B7)");
        AddOutputRow(grid, 14, "ΔU%, Cu 400 В", "cu400Drop", "B9/(B3*B7)");
        AddOutputRow(grid, 15, "ΔU%, Cu 230 В", "cu230Drop", "B9/(B4*B7)");
        AddOutputRow(grid, 16, "ΔU%, Al 230 В", "al230Drop", "B9/(B5*B7)");

        TextBlock threePhaseHeader = CreateSubHeader("Расчет 3Ф тока");
        Grid.SetRow(threePhaseHeader, 17);
        Grid.SetColumnSpan(threePhaseHeader, 4);
        grid.Children.Add(threePhaseHeader);
        Grid.SetRow(threePhaseCurrentRows, 18);
        Grid.SetColumnSpan(threePhaseCurrentRows, 4);
        grid.Children.Add(threePhaseCurrentRows);

        TextBlock singlePhaseHeader = CreateSubHeader("Расчет 1Ф тока");
        Grid.SetRow(singlePhaseHeader, 19);
        Grid.SetColumnSpan(singlePhaseHeader, 4);
        grid.Children.Add(singlePhaseHeader);
        Grid.SetRow(singlePhaseCurrentRows, 20);
        Grid.SetColumnSpan(singlePhaseCurrentRows, 4);
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
        AddOutputRow(grid, 4, "Ко/Руд, кВт", "stdApartmentSpecificDemand", "G67");
        AddOutputRow(grid, 5, "Ру.общ., кВт", "stdApartmentInstalledPower", "C67*F67");
        AddInputRow(grid, 6, "Кс квартир", "stdApartmentUsageFactor");
        AddInputRow(grid, 7, "Кс.об квартир", "stdApartmentCoincidenceFactor");
        AddOutputRow(grid, 8, "Рр квартир, кВт", "stdApartmentActivePower", "C67*G67*I67*J67");
        AddInputRow(grid, 9, "cos(ϕ) квартир", "stdApartmentCosPhi");
        AddOutputRow(grid, 10, "Qр квартир, кВАр", "stdApartmentReactivePower", "SQRT(N67^2-K67^2)");
        AddOutputRow(grid, 11, "Sр квартир, кВА", "stdApartmentApparentPower", "K67/L67");
        AddOutputRow(grid, 12, "Ip квартир, A", "stdApartmentCurrent", "K67/(0.38*SQRT(3)*L67)");
        AddInputRow(grid, 13, "Кол-во лифтов", "stdElevatorCount");
        AddInputRow(grid, 14, "Ру.общ. лифтов, кВт", "stdLiftInstalledPower");
        AddOutputRow(grid, 15, "Кс лифтов", "stdLiftDemandFactor", "I68");
        AddInputRow(grid, 16, "Кс.об лифтов", "stdLiftCoincidenceFactor");
        AddOutputRow(grid, 17, "Рр лифтов, кВт", "stdLiftActivePower", "H68*I68*J68");
        AddInputRow(grid, 18, "cos(ϕ) лифтов", "stdLiftCosPhi");
        AddOutputRow(grid, 19, "Qр лифтов, кВАр", "stdLiftReactivePower", "SQRT(N68^2-K68^2)");
        AddOutputRow(grid, 20, "Sр лифтов, кВА", "stdLiftApparentPower", "K68/L68");
        AddOutputRow(grid, 21, "Ip лифтов, A", "stdLiftCurrent", "K68/(0.38*SQRT(3)*L68)");

        return CreateSection("Таблица 2", grid);
    }

    private UIElement CreateHighComfortDemandTable()
    {
        Grid grid = CreateTableGrid();
        AddTableHeader(grid, "Расчет мощности квартир повышенной комфортности с электроплитами и лифтов", 4);
        AddInputRow(grid, 1, "Кол-во этажей", "comfortFloors");
        AddInputRow(grid, 2, "Кол-во квартир", "comfortApartmentCount");
        AddInputRow(grid, 3, "Ру., кВт на квартиру", "comfortApartmentUnitPower");
        AddOutputRow(grid, 4, "Кс/Ко по Ру.", "comfortUnitPowerDemandFactor", "G73");
        AddInputRow(grid, 5, "Кол-во квартир для Кс/Ко", "comfortSpecificDemandApartmentCount");
        AddOutputRow(grid, 6, "Кс/Ко по количеству", "comfortApartmentCountDemandFactor", "G74");
        AddOutputRow(grid, 7, "Ру.общ., кВт", "comfortApartmentInstalledPower", "C73*F73");
        AddInputRow(grid, 8, "Кс квартир", "comfortApartmentUsageFactor");
        AddInputRow(grid, 9, "Кс.об квартир", "comfortApartmentCoincidenceFactor");
        AddOutputRow(grid, 10, "Рр квартир, кВт", "comfortApartmentActivePower", "C73*F73*G73*G74*I73*J73");
        AddInputRow(grid, 11, "cos(ϕ) квартир", "comfortApartmentCosPhi");
        AddOutputRow(grid, 12, "Qр квартир, кВАр", "comfortApartmentReactivePower", "SQRT(N73^2-K73^2)");
        AddOutputRow(grid, 13, "Sр квартир, кВА", "comfortApartmentApparentPower", "K73/L73");
        AddOutputRow(grid, 14, "Ip квартир, A", "comfortApartmentCurrent", "K73/(0.38*SQRT(3)*L73)");
        AddInputRow(grid, 15, "Кол-во лифтов", "comfortElevatorCount");
        AddInputRow(grid, 16, "Ру.общ. лифтов, кВт", "comfortLiftInstalledPower");
        AddOutputRow(grid, 17, "Кс лифтов", "comfortLiftDemandFactor", "I75");
        AddInputRow(grid, 18, "Кс.об лифтов", "comfortLiftCoincidenceFactor");
        AddOutputRow(grid, 19, "Рр лифтов, кВт", "comfortLiftActivePower", "H75*I75*J75");
        AddInputRow(grid, 20, "cos(ϕ) лифтов", "comfortLiftCosPhi");
        AddOutputRow(grid, 21, "Qр лифтов, кВАр", "comfortLiftReactivePower", "SQRT(N75^2-K75^2)");
        AddOutputRow(grid, 22, "Sр лифтов, кВА", "comfortLiftApparentPower", "K75/L75");
        AddOutputRow(grid, 23, "Ip лифтов, A", "comfortLiftCurrent", "K75/(0.38*SQRT(3)*L75)");
        AddInputRow(grid, 24, "cos(ϕ) общего по квартирам", "comfortCombinedCosPhi");
        AddOutputRow(grid, 25, "Общее по квартирам Рр, кВт", "comfortCombinedActivePower", "K67+K73");
        AddOutputRow(grid, 26, "Общее по квартирам Qр, кВАр", "comfortCombinedReactivePower", "SQRT(N77^2-K77^2)");
        AddOutputRow(grid, 27, "Общее по квартирам Sр, кВА", "comfortCombinedApparentPower", "K77/L77");
        AddOutputRow(grid, 28, "Общее по квартирам Ip, A", "comfortCombinedCurrent", "K77/(0.38*SQRT(3)*L77)");

        return CreateSection("Таблица 3", grid);
    }

    private static UIElement CreateSection(string header, UIElement content)
    {
        GroupBox groupBox = new()
        {
            Header = header,
            Content = content,
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(10)
        };
        return groupBox;
    }

    private static Grid CreateTableGrid()
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
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
        AddMutedText(grid, row, "Ввод", 3);
    }

    private void AddComboRow(Grid grid, int row, string label, ComboBox comboBox, string note)
    {
        AddLabel(grid, row, label);
        Grid.SetRow(comboBox, row);
        Grid.SetColumn(comboBox, 1);
        grid.Children.Add(comboBox);
        AddMutedText(grid, row, note, 3);
    }

    private void AddOutputRow(Grid grid, int row, string label, string key, string formula)
    {
        AddLabel(grid, row, label);
        TextBlock value = CreateOutputTextBlock();
        outputs[key] = value;
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
        AddMutedText(grid, row, formula, 3);
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
        Grid.SetColumnSpan(textBlock, columnSpan);
        grid.Children.Add(textBlock);
    }

    private static TextBlock CreateSubHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 4)
        };
    }

    private static void AddLabel(Grid grid, int row, string label)
    {
        TextBlock textBlock = new()
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 10, 2)
        };
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);
    }

    private static void AddMutedText(Grid grid, int row, string text, int column)
    {
        TextBlock textBlock = new()
        {
            Text = text,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 2, 0, 2)
        };
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, column);
        grid.Children.Add(textBlock);
    }

    private TextBox CreateInputTextBox()
    {
        TextBox textBox = new()
        {
            Height = 26,
            MinWidth = 120,
            Background = new SolidColorBrush(Color.FromRgb(232, 246, 231)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 150, 80)),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
        };
        textBox.TextChanged += (_, _) => UpdateResults();
        return textBox;
    }

    private static ComboBox CreateReferenceComboBox(string toolTip)
    {
        return new ComboBox
        {
            DisplayMemberPath = "DisplayName",
            Height = 28,
            MinWidth = 140,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
            ToolTip = toolTip
        };
    }

    private static TextBlock CreateOutputTextBlock()
    {
        return new TextBlock
        {
            MinHeight = 26,
            Background = new SolidColorBrush(Color.FromRgb(229, 242, 249)),
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2)
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
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

    private sealed record ConductorMaterialOption(VoltageDropConductorMaterial Material, string DisplayName);

    private sealed record VoltageOption(double Voltage, string DisplayName);
}
