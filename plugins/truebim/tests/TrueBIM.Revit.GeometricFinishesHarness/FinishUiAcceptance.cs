using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.GeometricFinishes;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace TrueBIM.Revit.GeometricFinishesHarness;

internal static class FinishUiAcceptance
{
    public static void Run(UIApplication app, string root)
    {
        var checks = new List<object>();
        foreach (FinishKind kind in new[] { FinishKind.Floors, FinishKind.Ceilings, FinishKind.Walls })
        foreach (bool compact in new[] { false, true })
        {
            FinishWindow window = new(new FinishCatalog(app.ActiveUIDocument), kind);
            new System.Windows.Interop.WindowInteropHelper(window).Owner = app.MainWindowHandle;
            if (compact) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                try
                {
                    T Find<T>(string name) where T : FrameworkElement => Descendants(window).OfType<T>().Single(e => e.Name == name);
                    Find<ComboBox>("RoomScope").SelectedValue = "Project";
                    Find<TextBox>("RoomFilter").Text = "Кухня";
                    Find<Button>("RefreshRooms").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    DataGrid table = Find<DataGrid>("FinishAssignments");
                    bool filtered = table.Items.Cast<FinishMappingRow>().Any();
                    Find<Button>("AssignBulkType").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    bool assigned = table.Items.Cast<FinishMappingRow>().All(r => !string.IsNullOrEmpty(r.TypeUniqueId));
                    if (kind == FinishKind.Walls) Find<ComboBox>("Grouping").SelectedValue = "RoomAndWallType";
                    Find<TextBox>("RoomFilter").Text = "";
                    Find<Button>("CheckFinish").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (FileStream file = File.Create(Path.Combine(root, "ui-" + kind + (compact ? "-compact" : "") + ".png"))) encoder.Save(file);
                    Button create = Find<Button>("CreateFinish");
                    Point position = create.TranslatePoint(new Point(0, 0), window);
                    Expander issues = Descendants(window).OfType<Expander>().Single();
                    Point tablePosition = table.TranslatePoint(new Point(0, 0), window);
                    Point issuesPosition = issues.TranslatePoint(new Point(0, 0), window);
                    bool fits = position.X >= 0 && position.Y >= 0 && position.X + create.ActualWidth <= window.ActualWidth
                        && position.Y + create.ActualHeight <= window.ActualHeight && table.ActualHeight >= 130;
                    fits &= issues.Visibility != Visibility.Visible || tablePosition.Y + table.ActualHeight <= issuesPosition.Y + 1;
                    if (issues.Visibility == Visibility.Visible)
                    {
                        Button showRoom = Find<Button>("ShowIssueRoom");
                        fits &= showRoom.TranslatePoint(new Point(0, showRoom.ActualHeight), window).Y < position.Y;
                    }
                    checks.Add(new { Case = kind + (compact ? "Compact" : "Normal"), Passed = filtered && assigned && fits,
                        Filtered = filtered, Assigned = assigned, Fits = fits, Rows = table.Items.Count, TableHeight = table.ActualHeight });
                }
                catch (Exception ex) { checks.Add(new { Case = kind.ToString(), Passed = false, Error = ex.ToString() }); }
                finally { window.Close(); }
            }));
            window.ShowDialog();
        }
        File.WriteAllText(Path.Combine(root, "ui.json"), new JavaScriptSerializer().Serialize(checks));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject item)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(item, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }
}
