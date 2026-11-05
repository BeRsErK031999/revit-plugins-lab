using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
using TrueBIM.App.UI.DesignSystem;
using TrueBIM.Updates;

namespace TrueBIM.App.Modules.Updates;

public sealed class UpdateWindow : TrueBimWindow
{
    private readonly SharedFolderUpdateService service;
    private readonly ITrueBimLogger logger;
    private readonly string components;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock availableVersion = new() { Name = "AvailableVersion", Text = "Проверяем…" };
    private readonly TextBlock status = new() { Name = "UpdateStatus", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly ProgressBar progress = new() { Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Button check;
    private readonly Button update;
    private readonly Button close;
    private UpdatePackage? package;

    public UpdateWindow(string revitYear, ITrueBimLogger logger, SharedFolderUpdateService? service = null)
    {
        this.logger = logger;
        this.service = service ?? new SharedFolderUpdateService();
        components = SharedFolderUpdateService.InstalledComponents(revitYear);
        Title = "Обновления TrueBIM";
        Width = 580;
        MinWidth = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = IconFactory.CreateImage(TrueBimIcon.Refresh, 32);

        StackPanel root = new() { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Проверить обновления", FontSize = 20, FontWeight = FontWeights.SemiBold });
        Grid versions = new() { Margin = new Thickness(0, 16, 0, 0) };
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        versions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel current = new();
        current.Children.Add(new TextBlock { Text = "Установлена", Foreground = TrueBimBrushes.TextSecondary });
        current.Children.Add(new TextBlock { Text = SharedFolderUpdateService.CurrentVersion.ToString(3), FontSize = 20, Margin = new Thickness(0, 4, 0, 0) });
        StackPanel available = new();
        available.Children.Add(new TextBlock { Text = "В общей папке", Foreground = TrueBimBrushes.TextSecondary });
        availableVersion.FontSize = 20;
        availableVersion.Margin = new Thickness(0, 4, 0, 0);
        available.Children.Add(availableVersion);
        Grid.SetColumn(available, 1);
        versions.Children.Add(current);
        versions.Children.Add(available);
        root.Children.Add(versions);
        root.Children.Add(status);
        root.Children.Add(progress);
        root.Children.Add(new TextBlock { Text = SharedFolderUpdateService.ReleaseDirectory, Foreground = TrueBimBrushes.TextSecondary,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        check = TrueBimUi.CreateSecondaryButton("Повторить", TrueBimIcon.Refresh, minWidth: 95);
        check.Name = "CheckUpdates";
        check.Margin = new Thickness(0, 0, 8, 0);
        check.Click += async (_, _) => await CheckAsync();
        update = TrueBimUi.CreatePrimaryButton("Обновить", TrueBimIcon.Import, minWidth: 110);
        update.Name = "PrepareUpdate";
        update.Margin = new Thickness(0, 0, 8, 0);
        update.IsEnabled = false;
        update.Click += async (_, _) => await PrepareAsync();
        close = TrueBimUi.CreateSecondaryButton("Закрыть", TrueBimIcon.Close, minWidth: 95);
        close.Name = "CloseUpdates";
        close.Click += (_, _) => Close();
        buttons.Children.Add(check);
        buttons.Children.Add(update);
        buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;
        Loaded += async (_, _) => await CheckAsync();
        Closed += (_, _) => lifetime.Cancel();
    }

    private async Task CheckAsync()
    {
        package = null;
        SetBusy(true);
        availableVersion.Text = "Проверяем…";
        status.Text = "Читаем сведения об обновлении из общей папки.";
        try
        {
            package = await service.CheckAsync(lifetime.Token);
            availableVersion.Text = package.Version.ToString(3);
            status.Text = package.Version > SharedFolderUpdateService.CurrentVersion
                ? "Доступна новая версия. После загрузки сохраните модели и закройте все окна Revit. Установка начнётся автоматически."
                : "У вас установлена актуальная или более новая версия TrueBIM.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { ShowError(exception); }
        finally { if (!lifetime.IsCancellationRequested) SetBusy(false); }
    }

    private async Task PrepareAsync()
    {
        if (package == null) return;
        SetBusy(true);
        close.Content = "Отмена";
        progress.IsIndeterminate = false;
        progress.Value = 0;
        status.Text = "Копируем и проверяем установщик. Revit можно продолжать использовать после подготовки обновления.";
        try
        {
            using PreparedUpdate prepared = await service.PrepareAsync(package,
                new Progress<int>(value => progress.Value = value), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            prepared.StartHelper(components);
            logger.Info($"Prepared TrueBIM {package.Version} update from the shared release folder.");
            status.Text = "Помощник обновления запущен. Сохраните модели и закройте все окна Revit. Помощник дождётся закрытия и установит новую версию; ожидание можно отменить в его окне.";
            package = null;
            check.IsEnabled = false;
            update.IsEnabled = false;
            progress.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ShowError(exception);
            if (!lifetime.IsCancellationRequested) SetBusy(false);
        }
        finally { close.Content = "Закрыть"; }
    }

    private void SetBusy(bool busy)
    {
        check.IsEnabled = !busy;
        update.IsEnabled = !busy && package != null && package.Version > SharedFolderUpdateService.CurrentVersion;
        progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        progress.IsIndeterminate = busy;
    }

    private void ShowError(Exception exception)
    {
        logger.Error("Shared-folder update failed.", exception);
        availableVersion.Text = "Недоступна";
        status.Text = exception switch
        {
            TimeoutException => exception.Message,
            InvalidDataException => exception.Message,
            JsonException or KeyNotFoundException or InvalidOperationException => "Описание обновления повреждено или имеет неподдерживаемый формат. Сообщите ответственному за публикацию TrueBIM.",
            FileNotFoundException => "Не найден файл обновления или помощник установки. Проверьте общую папку; при отсутствии помощника переустановите TrueBIM обычным установщиком.",
            IOException or UnauthorizedAccessException => "Не удалось прочитать обновление. Проверьте доступ к W:\\Shared\\RevitPlugins и повторите попытку. Установленная версия продолжает работать.",
            _ => "Не удалось подготовить обновление. Подробности записаны в логи TrueBIM."
        };
    }
}
