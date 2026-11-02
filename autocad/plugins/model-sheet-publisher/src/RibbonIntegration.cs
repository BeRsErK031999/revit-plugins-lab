using System;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace ModelSheetPublisher
{
    internal static class RibbonIntegration
    {
        private const string TabId = "ModelSheetPublisher.Tab";
        private static bool attached;
        public static void Start()
        {
            if (attached) return;
            attached = true;
            // Also restores our tab after a workspace/CUI reload. Cheap identity check while idle.
            AcApp.Idle += OnIdle;
        }
        public static void Stop() { if (attached) AcApp.Idle -= OnIdle; attached = false; }
        private static void OnIdle(object sender, EventArgs args)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null || ribbon.Tabs.Any(t => t.Id == TabId)) return;
            var tab = new RibbonTab { Id = TabId, Title = "Печать PDF" };
            var panel = new RibbonPanelSource { Title = "Из модели" };
            var button = new RibbonButton
            {
                Id = "ModelSheetPublisher.Publish", Text = "Листы\nв PDF", ShowText = true, ShowImage = true,
                Size = RibbonItemSize.Large, Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = Icon(), CommandHandler = new PublishCommand(),
                ToolTip = "Выбрать рамки листов в модели и создать общий PDF или отдельные файлы."
            };
            panel.Items.Add(button); tab.Panels.Add(new RibbonPanel { Source = panel }); ribbon.Tabs.Add(tab);
        }
        private static ImageSource Icon()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.White, new Pen(Brushes.SteelBlue, 2), new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(5, 2, 22, 28))));
            group.Children.Add(new GeometryDrawing(Brushes.Firebrick, null, new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(1, 15, 30, 12))));
            var text = new System.Windows.Media.FormattedText("PDF", System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White, 1);
            group.Children.Add(new GeometryDrawing(Brushes.White, null, text.BuildGeometry(new System.Windows.Point(6, 14))));
            var image = new DrawingImage(group); image.Freeze(); return image;
        }
        private sealed class PublishCommand : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (doc == null) AcApp.ShowAlertDialog("Откройте чертёж DWG.");
                else doc.SendStringToExecute("MSPUBLISH ", true, false, true);
            }
        }
    }
}
