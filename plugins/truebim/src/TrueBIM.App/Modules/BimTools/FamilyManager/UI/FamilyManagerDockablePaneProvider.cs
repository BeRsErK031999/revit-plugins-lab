using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.UI;

public sealed class FamilyManagerDockablePaneProvider : IDockablePaneProvider
{
    public static readonly DockablePaneId PaneId = new(new Guid("9C06F88A-4782-4F1E-8C43-81DD5F50F45E"));

    private static readonly FamilyManagerDockablePaneHost Host = new();

    public static void Register(UIControlledApplication application)
    {
        Guard.NotNull(application, nameof(application));

        application.RegisterDockablePane(PaneId, "Диспетчер семейств", new FamilyManagerDockablePaneProvider());
    }

    public static void ShowManagerDialog(
        UIApplication uiApplication,
        UIDocument uiDocument,
        ITrueBimLogger logger)
    {
        Guard.NotNull(uiApplication, nameof(uiApplication));
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(logger, nameof(logger));

        const string windowKey = "truebim.family-manager";
        if (ModelessWindowService.Activate(windowKey, logger))
        {
            return;
        }

        FamilyManagerWindow window = new(
            uiApplication,
            uiDocument,
            new FamilyManagerProfileStorage(logger),
            new FamilyLibraryScanner(),
            new FamilyLoadService(),
            new FamilyMetadataService(),
            new FamilyThumbnailService(),
            logger,
            folderPath => ShowCompactPane(uiApplication, uiDocument, folderPath, logger));

        ModelessWindowService.Show(windowKey, window, uiApplication.MainWindowHandle, logger);
    }

    public static void ShowCompactPane(
        UIApplication uiApplication,
        UIDocument uiDocument,
        string folderPath,
        ITrueBimLogger logger)
    {
        Guard.NotNull(uiApplication, nameof(uiApplication));
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNullOrWhiteSpace(folderPath, nameof(folderPath));
        Guard.NotNull(logger, nameof(logger));

        Host.LoadCompactPane(
            uiApplication,
            uiDocument,
            folderPath,
            new FamilyManagerProfileStorage(logger),
            logger);

        DockablePane pane = uiApplication.GetDockablePane(PaneId);
        pane.Show();
    }

    public static void Hide(UIApplication uiApplication)
    {
        Guard.NotNull(uiApplication, nameof(uiApplication));

        DockablePane pane = uiApplication.GetDockablePane(PaneId);
        pane.Hide();
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        Guard.NotNull(data, nameof(data));

        data.FrameworkElement = Host;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Left
        };
    }

    private sealed class FamilyManagerDockablePaneHost : UserControl
    {
        public FamilyManagerDockablePaneHost()
        {
            Content = CreatePlaceholder();
        }

        public void LoadCompactPane(
            UIApplication uiApplication,
            UIDocument uiDocument,
            string folderPath,
            FamilyManagerProfileStorage profileStorage,
            ITrueBimLogger logger)
        {
            Content = new FamilyManagerCompactPaneControl(
                folderPath,
                profileStorage,
                logger,
                () => ShowManagerDialog(uiApplication, uiDocument, logger),
                () => Hide(uiApplication));
        }

        private static UIElement CreatePlaceholder()
        {
            Border border = new()
            {
                Background = Brushes.WhiteSmoke,
                Padding = new Thickness(18)
            };

            border.Child = new TextBlock
            {
                Text = "Откройте проект и нажмите TrueBIM -> Библиотека -> Диспетчер семейств.",
                TextWrapping = TextWrapping.Wrap
            };

            return border;
        }
    }
}
