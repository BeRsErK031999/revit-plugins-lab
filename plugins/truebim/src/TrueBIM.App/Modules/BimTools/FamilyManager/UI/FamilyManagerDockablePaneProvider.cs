using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;

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

    public static void Show(
        UIApplication uiApplication,
        UIDocument uiDocument,
        FileTrueBimLogger logger)
    {
        Guard.NotNull(uiApplication, nameof(uiApplication));
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(logger, nameof(logger));

        Host.LoadFamilyManager(
            uiApplication,
            uiDocument,
            new FamilyManagerProfileStorage(logger),
            new FamilyLibraryScanner(),
            new FamilyLoadService(),
            new FamilyMetadataService(),
            new FamilyThumbnailService(),
            logger);

        DockablePane pane = uiApplication.GetDockablePane(PaneId);
        pane.Show();
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

        public void LoadFamilyManager(
            UIApplication uiApplication,
            UIDocument uiDocument,
            FamilyManagerProfileStorage profileStorage,
            FamilyLibraryScanner scanner,
            FamilyLoadService loadService,
            FamilyMetadataService metadataService,
            FamilyThumbnailService thumbnailService,
            ITrueBimLogger logger)
        {
            Content = new FamilyManagerControl(
                uiApplication,
                uiDocument,
                profileStorage,
                scanner,
                loadService,
                metadataService,
                thumbnailService,
                logger);
        }

        private static UIElement CreatePlaceholder()
        {
            return new TextBlock
            {
                Text = "Откройте проект и нажмите TrueBIM -> Библиотека -> Диспетчер семейств.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(18)
            };
        }
    }
}
