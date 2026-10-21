using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrueBIM.App.Modules.Tep;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Tep;

public sealed class TepWindowTests
{
    [Fact]
    public async Task ReadModelsShowsSourceRowsResultsAndDiagnostics()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            TepWindow? window = null;
            try
            {
                TepSnapshot snapshot = new()
                {
                    Project = "Проверка интерфейса", AvailableParts = TepParts.All.ToList(),
                    Items = [new TepItem { Key = "sample", Kind = "Помещение", Part = TepParts.B1, Role = TepRoles.Apartment, Name = "Спальня",
                        Area = 12.345, Group = "101", RoomsInApartment = 2, Living = true, Document = "Б1.rvt" },
                        new TepItem { Key = "zero", Kind = "Помещение", Part = TepParts.B1, Role = TepRoles.Apartment,
                            Name = "Нулевое помещение", Area = 0, Group = "101", RoomsInApartment = 2, Living = true, Document = "Б1.rvt" }]
                };
                window = new TepWindow([], _ => snapshot);
                FrameworkElement content = (FrameworkElement)window.Content;
                if (content is Panel panel) panel.Background = window.Background;
                Arrange(content);
                Render(content, "settings");
                Button read = Descendants(content).OfType<Button>().Single(b => Equals(b.Content, "Прочитать модели"));
                read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                TabControl tabs = Descendants(content).OfType<TabControl>().Single();
                Assert.Equal(5, tabs.Items.Count);
                Assert.Equal(1, tabs.SelectedIndex);
                tabs.SelectedIndex = 3;
                Arrange(content);
                DataGrid results = (DataGrid)((TabItem)tabs.Items[3]).Content;
                Assert.Equal(37, results.Items.Count);
                Assert.Equal("12,3 *", ((TepResultRow)results.Items[12]).B1);
                Assert.Contains("Неполный расчёт", ((TepResultRow)results.Items[12]).Notes);
                Assert.Contains(Descendants(content).OfType<TextBlock>(), t => t.Text.Contains("* — неполный расчёт"));
                Render(content, "results");
                completion.SetResult(true);
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (DependencyObject item in Descendants(child)) yield return item;
    }
    private static void Arrange(FrameworkElement content)
    {
        content.Measure(new Size(1300, 790));
        content.Arrange(new Rect(0, 0, 1300, 790));
        content.UpdateLayout();
    }
    private static void Render(FrameworkElement content, string suffix)
    {
        string? directory = Environment.GetEnvironmentVariable("TRUEBIM_TEP_UI_PREVIEW");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        RenderTargetBitmap bitmap = new(1300, 790, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, "tep-" + suffix + ".png"));
        encoder.Save(stream);
    }
}
