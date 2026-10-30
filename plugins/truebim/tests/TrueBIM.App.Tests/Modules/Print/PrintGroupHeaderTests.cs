using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.UI;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print;

public sealed class PrintGroupHeaderTests
{
    [Fact]
    public async Task GroupCheckboxSelectsHundredSheetsWithoutSelectingOtherGroupOrPlaceholder()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Window? host = null;
            try
            {
                Type rowType = typeof(PrintWindow).GetNestedType("PrintSheetRow", BindingFlags.NonPublic)!;
                PropertyInfo selected = rowType.GetProperty("IsSelected")!;
                object Row(int id, string group, bool placeholder = false) => Activator.CreateInstance(rowType,
                    new PrintSheetInfo(id, "source", "Model", false, group, id.ToString(), "Sheet", "A3",
                        placeholder, !placeholder, new Dictionary<string, string>(), new Dictionary<string, string>()), false)!;
                ArrayList rows = new();
                for (int i = 0; i < 100; i++) rows.Add(Row(i, "Комплект"));
                object placeholder = Row(100, "Комплект", true);
                object other = Row(101, "Другой комплект");
                rows.Add(placeholder);
                rows.Add(other);
                selected.SetValue(rows[0], true);
                ListCollectionView view = new(rows);
                view.GroupDescriptions.Add(new PropertyGroupDescription("GroupName"));
                Action<IEnumerable<object>, bool> select = (items, value) =>
                {
                    foreach (object item in items) selected.SetValue(item, value);
                };
                GroupStyle style = (GroupStyle)typeof(PrintWindow).GetMethod("CreateSheetGroupStyle", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, ["Группа: {0}", select])!;
                ListBox list = new() { ItemsSource = view, Width = 800, Height = 600 };
                list.GroupStyle.Add(style);
                host = new Window { Content = list, Width = 820, Height = 640, Left = -10000,
                    Top = -10000, ShowActivated = false, ShowInTaskbar = false };
                host.Show();
                list.Measure(new Size(800, 600));
                list.Arrange(new Rect(0, 0, 800, 600));
                list.UpdateLayout();
                CheckBox header = Descendants(list).OfType<CheckBox>()
                    .First(box => box.DataContext is CollectionViewGroup group && Equals(group.Name, "Комплект"));
                header.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Null(header.IsChecked);
                void Click()
                {
                    typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(header, null);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                }
                Click();
                Assert.True(header.IsChecked);
                Assert.All(rows.Cast<object>().Take(100), row => Assert.Equal(true, selected.GetValue(row)));
                Assert.Equal(false, selected.GetValue(other));
                Assert.Equal(false, selected.GetValue(placeholder));
                selected.SetValue(rows[50], false);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Assert.Null(header.IsChecked);
                Click();
                Assert.True(header.IsChecked);
                Click();
                Assert.False(header.IsChecked);
                Assert.All(rows.Cast<object>(), row => Assert.Equal(false, selected.GetValue(row)));
                completion.SetResult(true);
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { host?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
