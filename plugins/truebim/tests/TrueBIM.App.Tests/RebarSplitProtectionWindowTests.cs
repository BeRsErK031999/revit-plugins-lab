using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class RebarSplitProtectionWindowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ShowsSeparateChoicesAndCopyableIds(bool groups) => InSta(groups, window =>
    {
        FrameworkElement content = (FrameworkElement)window.Content;
        if (content is Panel panel) panel.Background = window.Background;
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
        List<Button> buttons = Descendants(content).OfType<Button>().ToList();
        string[] expected = groups ? ["Извлечь из группы и разбить", "Пропустить обработку элементов"]
            : ["Пропустить проблемные элементы", "Обработать с сохранением закрепления", "Обработать без сохранения закрепления"];
        foreach (string title in expected)
        {
            Button button = buttons.Single(item => ButtonText(item).Contains(title));
            Point location = button.TranslatePoint(new Point(), content);
            Assert.True(button.ActualHeight > 0 && location.Y >= 0 && location.Y + button.ActualHeight <= window.Height,
                $"Action is clipped: {title}");
        }
        Assert.Single(buttons, button => button.IsCancel && button.IsDefault);
        TextBox ids = Assert.Single(Descendants(content).OfType<TextBox>());
        Assert.True(ids.IsReadOnly);
        ids.SelectAll();
        Assert.Equal(ids.Text, ids.SelectedText);
        Assert.Contains("2030787", ids.Text);
        Assert.Equal(RebarSplitProtectionAction.Cancel, window.Action);
        Assert.Equal(RebarSplitGroupAction.Cancel, window.GroupAction);
        if (groups) Assert.Contains(Descendants(content).OfType<TextBlock>(), text => text.Text.Contains("будут разгруппированы"));
        string? directory = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_UI_PREVIEW");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        RenderTargetBitmap bitmap = new((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(Path.Combine(directory, groups ? "group-choices.png" : "pin-choices.png"));
        encoder.Save(stream);
    });

    [Theory]
    [InlineData(true, "Извлечь из группы и разбить", 1)]
    [InlineData(true, "Пропустить обработку элементов", 2)]
    [InlineData(false, "Пропустить проблемные элементы", 1)]
    [InlineData(false, "Обработать с сохранением закрепления", 2)]
    [InlineData(false, "Обработать без сохранения закрепления", 3)]
    public Task ActionClosesDialogWithChosenValue(bool groups, string title, int expected) => InSta(groups, window =>
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Opacity = 0;
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            Descendants(window).OfType<Button>().Single(button => ButtonText(button).Contains(title))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        Assert.True(window.ShowDialog());
        Assert.Equal(expected, groups ? (int)window.GroupAction : (int)window.Action);
    });

    private static string ButtonText(Button button) => button.Content as string
        ?? string.Join(" ", Descendants(button).OfType<TextBlock>().Select(text => text.Text));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
    }

    private static async Task InSta(bool groups, Action<RebarSplitProtectionWindow> assertion)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            RebarSplitProtectionWindow? window = null;
            try
            {
                window = new([new(2030787, true, groups ? 2030825 : null, groups ? "Группа 1" : null)], groups);
                assertion(window);
                completion.SetResult(true);
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
