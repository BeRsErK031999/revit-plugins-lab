using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleRegister;

public sealed class ScheduleRegisterWindowTests
{
    [Theory]
    [InlineData("", new long[] { 1, 3 })]
    [InlineData("  план  ", new long[] { 1 })]
    [InlineData("03", new long[] { 3 })]
    [InlineData("нет совпадений", new long[] { })]
    public Task SelectWithSchedules_UpdatesCheckboxesAndResultWithoutHidingRows(string search, long[] expectedIds)
    {
        return InWindow(window =>
        {
            Click(window, "Снять выбор всех листов");
            Find<TextBox>(window, "Поиск листов").Text = search;
            DataGrid grid = Descendants(window).OfType<DataGrid>().Single();
            int visibleCount = grid.Items.Count;

            Click(window, "Выбрать листы со спецификациями");

            Assert.Equal(expectedIds, window.SelectedSheetIds);
            Assert.Equal(visibleCount, grid.Items.Count);
            Assert.Equal(expectedIds.Length > 0, RunButton(window).IsEnabled);
            foreach (ScheduleRegisterSheetOption sheet in grid.Items)
            {
                CheckBox checkBox = CreateBoundCheckBox(grid, sheet);
                Assert.Equal(expectedIds.Contains(sheet.SheetId), checkBox.IsChecked);
            }

            Click(window, "Выбрать листы со спецификациями");
            Assert.Equal(expectedIds, window.SelectedSheetIds);
            Assert.Contains(Descendants(window).OfType<TextBlock>(),
                text => text.Text.StartsWith($"Выбрано листов: {expectedIds.Length} из 3.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public Task SearchAndBulkActions_PreserveOrClearHiddenSelectionAsDocumented()
    {
        return InWindow(window =>
        {
            Assert.Equal(new long[] { 1, 2, 3 }, window.SelectedSheetIds);
            TextBox search = Find<TextBox>(window, "Поиск листов");
            search.Text = "план";
            Assert.Equal(new long[] { 1, 2, 3 }, window.SelectedSheetIds);
            Assert.Contains(Descendants(window).OfType<TextBlock>(),
                text => text.Text.Contains("Выбрано вне поиска: 1.", StringComparison.Ordinal));

            Click(window, "Выбрать листы со спецификациями");
            Assert.Equal(new long[] { 1 }, window.SelectedSheetIds);
            search.Text = "03";
            Click(window, "Выбрать найденные листы");
            Assert.Equal(new long[] { 1, 3 }, window.SelectedSheetIds);
            Click(window, "Снять выбор всех листов");
            Assert.Empty(window.SelectedSheetIds);
            Assert.False(RunButton(window).IsEnabled);
        });
    }

    [Fact]
    public Task ManualCheckboxChanges_UpdateSelectionAndCreateAvailabilityImmediately()
    {
        return InWindow(window =>
        {
            Click(window, "Снять выбор всех листов");
            DataGrid grid = Descendants(window).OfType<DataGrid>().Single();
            ScheduleRegisterSheetOption sheet = (ScheduleRegisterSheetOption)grid.Items[0];
            CheckBox checkBox = CreateBoundCheckBox(grid, sheet);

            checkBox.IsChecked = true;

            Assert.Equal(new long[] { sheet.SheetId }, window.SelectedSheetIds);
            Assert.True(RunButton(window).IsEnabled);
            Click(window, "Снять выбор всех листов");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            Assert.False(checkBox.IsChecked);
            Assert.False(RunButton(window).IsEnabled);
        });
    }

    [Fact]
    public Task InitialSelection_DoesNotRestrictSheetListOrHideSettings()
    {
        return InWindow(window =>
        {
            Assert.Equal(new long[] { 3 }, window.SelectedSheetIds);
            Assert.Equal(3, Descendants(window).OfType<DataGrid>().Single().Items.Count);
            Assert.True(Find<Button>(window, "Настройки ведомости").IsEnabled);
        }, preselectThirdOnly: true);
    }

    [Fact]
    public Task InvalidParameterFilter_BlocksCreationEvenWithSelectedSheets()
    {
        return InWindow(window =>
        {
            Assert.NotEmpty(window.SelectedSheetIds);
            Assert.False(RunButton(window).IsEnabled);
            Click(window, "Выбрать листы со спецификациями");
            Assert.Equal(new long[] { 1, 3 }, window.SelectedSheetIds);
            Assert.False(RunButton(window).IsEnabled);
        }, invalidFilter: true);
    }

    private static CheckBox CreateBoundCheckBox(DataGrid grid, ScheduleRegisterSheetOption sheet)
    {
        grid.ScrollIntoView(sheet);
        grid.UpdateLayout();
        FrameworkElement cell = grid.Columns[0].GetCellContent(sheet);
        Assert.NotNull(cell);
        CheckBox checkBox = VisualDescendants(cell).OfType<CheckBox>().Single();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.True(checkBox.IsEnabled);
        return checkBox;
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (DependencyObject descendant in VisualDescendants(VisualTreeHelper.GetChild(root, index)))
            {
                yield return descendant;
            }
        }
    }

    private static Button RunButton(ScheduleRegisterWindow window)
    {
        return Find<Button>(window, "Создать ведомость по выбранным листам");
    }

    private static void Click(ScheduleRegisterWindow window, string name)
    {
        Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static T Find<T>(DependencyObject root, string name) where T : DependencyObject
    {
        return Descendants(root).OfType<T>().Single(item => AutomationProperties.GetName(item) == name);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (DependencyObject descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task InWindow(
        Action<ScheduleRegisterWindow> assertion,
        bool preselectThirdOnly = false,
        bool invalidFilter = false)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            string settingsPath = Path.Combine(Path.GetTempPath(), $"truebim-register-ui-{Guid.NewGuid():N}.json");
            ScheduleRegisterWindow? window = null;
            try
            {
                ScheduleRegisterSettingsStorage storage = new(settingsPath, new TestLogger());
                if (invalidFilter)
                {
                    storage.Save(new ScheduleRegisterSettings { FilterEnabled = true });
                }

                ScheduleRegisterTemplateValidation validation = new ScheduleRegisterTemplateValidator().Validate(
                    new ScheduleRegisterTemplateSnapshot(
                    [
                        ["Ведомость спецификаций", "", ""],
                        ["Лист", "Наименование", "Примечание"],
                        ["", "", ""]
                    ]));
                window = new ScheduleRegisterWindow(storage, [], new(10, validation, 0),
                [
                    new(1, "01", "План первого этажа", 2, !preselectThirdOnly),
                    new(2, "02", "План кровли", 0, !preselectThirdOnly),
                    new(3, "03", "Фасад", 1, true)
                ]);
                FrameworkElement content = (FrameworkElement)window.Content;
                content.Measure(new Size(960, 700));
                content.Arrange(new Rect(0, 0, 960, 700));
                content.UpdateLayout();
                assertion(window);
                completion.SetResult(true);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                window?.Close();
                File.Delete(settingsPath);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class TestLogger : ITrueBimLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
