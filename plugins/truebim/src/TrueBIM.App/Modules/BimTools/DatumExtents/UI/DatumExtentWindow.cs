using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Modules.BimTools.DatumExtents.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using RevitDocument = Autodesk.Revit.DB.Document;
using RevitView = Autodesk.Revit.DB.View;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.UI;

public sealed class DatumExtentWindow : Window
{
    private readonly RevitDocument document;
    private readonly RevitView activeView;
    private readonly DatumExtentService datumExtentService;
    private readonly ITrueBimLogger logger;
    private readonly List<Button> actionButtons = [];
    private readonly TextBlock statusText = new();

    public DatumExtentWindow(
        RevitDocument document,
        RevitView activeView,
        DatumExtentService datumExtentService,
        ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.activeView = activeView ?? throw new ArgumentNullException(nameof(activeView));
        this.datumExtentService = datumExtentService ?? throw new ArgumentNullException(nameof(datumExtentService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Title = "РЕЖИМ ОСЕЙ";
        Icon = IconFactory.CreateImage(TrueBimIcon.DatumExtents, 32);
        Width = 380;
        MinWidth = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = CreateContent();

        RefreshActionAvailability();
        logger.Info($"Datum Extents quick mode window opened for '{document.Title}' and view '{activeView.Name}'.");
    }

    private UIElement CreateContent()
    {
        StackPanel root = new()
        {
            Margin = new Thickness(14, 12, 14, 12)
        };

        root.Children.Add(CreateActionButton(
            "Переключить все оси на текущем виде в 2D",
            DatumExtentMode.ViewSpecific));
        root.Children.Add(CreateActionButton(
            "Переключить все оси на текущем виде в 3D",
            DatumExtentMode.Model));
        root.Children.Add(CreateActionButton(
            "Инвертировать режим осей",
            DatumExtentMode.Invert,
            centerText: true));

        statusText.Foreground = Brushes.DimGray;
        statusText.Margin = new Thickness(6, 6, 6, 0);
        statusText.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(statusText);

        return root;
    }

    private Button CreateActionButton(string text, DatumExtentMode mode, bool centerText = false)
    {
        Button button = new()
        {
            Content = text,
            Height = 34,
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(8, 0, 8, 0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Foreground = new SolidColorBrush(Color.FromRgb(45, 112, 177)),
            HorizontalContentAlignment = centerText ? HorizontalAlignment.Center : HorizontalAlignment.Left
        };
        button.Click += (_, _) => Apply(mode);
        ToolTipService.SetShowOnDisabled(button, true);
        actionButtons.Add(button);
        return button;
    }

    private void RefreshActionAvailability()
    {
        string? disabledReason = TryGetDisabledReason(out int gridCount);
        bool canApply = disabledReason is null;
        string toolTip = canApply
            ? $"Будет обработано осей: {gridCount}. Вид: {activeView.Name}."
            : disabledReason ?? "Действие недоступно для активного вида.";

        foreach (Button button in actionButtons)
        {
            button.IsEnabled = canApply;
            button.ToolTip = toolTip;
            button.Cursor = canApply ? Cursors.Hand : Cursors.Arrow;
            button.Foreground = canApply
                ? new SolidColorBrush(Color.FromRgb(45, 112, 177))
                : Brushes.Gray;
        }

        statusText.Text = disabledReason ?? string.Empty;
        statusText.Visibility = canApply ? Visibility.Collapsed : Visibility.Visible;
    }

    private string? TryGetDisabledReason(out int gridCount)
    {
        gridCount = 0;
        if (!DatumExtentCollectorService.CanUseActiveView(activeView, out string viewMessage))
        {
            return viewMessage;
        }

        try
        {
            gridCount = DatumExtentCollectorService.CollectVisibleGrids(document, activeView).Count;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect visible grids for Datum Extents quick mode.", exception);
            return "Не удалось прочитать оси на активном виде. Подробности записаны в лог TrueBIM.";
        }

        return gridCount == 0
            ? "На активном виде не найдено видимых осей."
            : null;
    }

    private void Apply(DatumExtentMode mode)
    {
        string? disabledReason = TryGetDisabledReason(out _);
        if (disabledReason is not null)
        {
            RefreshActionAvailability();
            Autodesk.Revit.UI.TaskDialog.Show("РЕЖИМ ОСЕЙ", disabledReason);
            return;
        }

        DatumExtentApplyResult result = datumExtentService.ApplyToVisibleGrids(document, activeView, mode, logger);
        string resultText = result.Rows.Count == 0
            ? "На активном виде не найдено видимых осей."
            : result.ToDialogText();

        logger.Info($"Datum Extents quick mode '{mode}' completed for '{activeView.Name}': {resultText}");
        Autodesk.Revit.UI.TaskDialog.Show("РЕЖИМ ОСЕЙ", resultText);
        Close();
    }
}
