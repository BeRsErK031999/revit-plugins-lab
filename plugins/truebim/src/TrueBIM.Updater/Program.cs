using System.Diagnostics;
using System.Security.Principal;
using TrueBIM.Updates;

namespace TrueBIM.Updater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (args.Length != 4) throw new InvalidDataException("Запустите обновление кнопкой «Проверить обновления» в TrueBIM.");
            string installer = Path.GetFullPath(args[0]);
            UpdatePackage package = new(args[1], Path.GetFileName(installer), args[2]);
            string components = UpdateInstallerLaunch.ValidateComponents(args[3]);
            using Mutex mutex = new(true, @"Local\TrueBIM.Updater." + WindowsIdentity.GetCurrent().User!.Value, out bool created);
            if (!created)
            {
                MessageBox.Show("Помощник обновления уже запущен. Используйте его открытое окно или отмените ожидание перед повторной попыткой.", "Обновление TrueBIM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Application.Run(new UpdateForm(installer, package, components)); }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Обновление TrueBIM", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}

internal sealed class UpdateForm : Form
{
    private readonly string installer;
    private readonly UpdatePackage package;
    private readonly string components;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(470, 0), Margin = new Padding(0, 12, 0, 12) };
    private readonly Button close = new() { AutoSize = true, Text = "Отменить обновление", Padding = new Padding(8, 3, 8, 3) };
    private readonly ProgressBar progress = new() { Width = 470, Height = 6, Style = ProgressBarStyle.Marquee, Margin = new Padding(0, 0, 0, 16) };
    private bool installing;
    private bool completed;
    private string LogPath => Path.Combine(Path.GetDirectoryName(installer)!, "update.log");

    public UpdateForm(string installer, UpdatePackage package, string components)
    {
        this.installer = installer;
        this.package = package;
        this.components = components;
        Text = "Обновление TrueBIM " + package.Version.ToString(3);
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(514, 240);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowLayoutPanel panel = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(22), Dock = DockStyle.Fill };
        panel.Controls.Add(new Label { Text = "TrueBIM " + package.Version.ToString(3), Font = new Font(Font.FontFamily, 16, FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(status);
        panel.Controls.Add(progress);
        close.Click += (_, _) => Close();
        panel.Controls.Add(close);
        Controls.Add(panel);
        FormClosing += (_, args) =>
        {
            if (installing) { args.Cancel = true; return; }
            if (!completed) { cancellation.Cancel(); WriteLog("Cancelled before installation."); }
        };
        Shown += async (_, _) => await InstallAsync();
    }

    private async Task InstallAsync()
    {
        try
        {
            status.Text = "Сохраните модели и закройте все окна Revit. Помощник дождётся закрытия; ожидание можно отменить.";
            WriteLog("Waiting for all Revit processes to close.");
            await UpdateInstallerLaunch.WaitUntilClosedAsync(UpdateInstallerLaunch.CountRevitProcesses,
                new Progress<int>(count =>
                {
                    if (!IsDisposed && !cancellation.IsCancellationRequested)
                        status.Text = $"Открыто процессов Revit: {count}.\r\nСохраните модели и закройте все окна Revit. Затем начнётся установка.";
                }), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            using FileStream verified = package.OpenVerifiedInstaller(installer);
            // Recheck after disk verification. The installer also checks independently.
            await UpdateInstallerLaunch.WaitUntilClosedAsync(UpdateInstallerLaunch.CountRevitProcesses, null, cancellation.Token);
            installing = true;
            close.Enabled = false;
            status.Text = "Устанавливаем обновление. Дождитесь завершения перед запуском Revit.";
            string installLog = Path.Combine(Path.GetDirectoryName(installer)!, "installer.log");
            using Process process = Process.Start(new ProcessStartInfo(installer, UpdateInstallerLaunch.Arguments(components, installLog))
                { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installer)! })
                ?? throw new IOException("Не удалось запустить установщик.");
            WriteLog("Installer started. PID=" + process.Id);
            await Task.Run(() => process.WaitForExit());
            WriteLog("Installer exit code=" + process.ExitCode);
            if (process.ExitCode != 0) throw new IOException($"Установка не завершилась (код {process.ExitCode}). Подробности: {installLog}");
            status.Text = "TrueBIM " + package.Version.ToString(3) + " установлен. Можно запускать Revit.";
            completed = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Environment.ExitCode = 1;
            WriteLog(exception.ToString());
            status.Text = exception.Message + "\r\nПовторную попытку можно выполнить через «Проверить обновления» в Revit.";
            completed = true;
        }
        finally
        {
            installing = false;
            if (!IsDisposed)
            {
                progress.Visible = false;
                close.Enabled = true;
                close.Text = "Закрыть";
            }
        }
    }

    private void WriteLog(string message)
    {
        try { File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
