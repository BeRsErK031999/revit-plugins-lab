using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ModelSheetPublisher
{
    public sealed class OutputForm : Form
    {
        public OutputForm(PublishOptions options, int sheetCount, bool publish)
        {
            Text = publish ? "Создать PDF · имя и папка" : "Имя и сохранение PDF";
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(650, 335); MinimumSize = Size; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 5 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            var name = new TextBox { Text = options.BaseName, Dock = DockStyle.Fill };
            var folder = new TextBox { Text = options.Folder, Dock = DockStyle.Fill };
            var mode = MainForm.Combo(350); mode.Dock = DockStyle.Fill;
            mode.Items.AddRange(new object[] { "Один общий PDF", "Отдельный PDF на каждый лист" }); mode.SelectedIndex = options.Separate ? 1 : 0;
            root.Controls.Add(MainForm.Label("Имя PDF:"), 0, 0); root.Controls.Add(name, 1, 0);
            root.Controls.Add(MainForm.Label("Результат:"), 0, 1); root.Controls.Add(mode, 1, 1);
            var pathPanel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.Controls.Add(folder, 0, 0);
            pathPanel.Controls.Add(MainForm.Button("Обзор…", () => { using (var dlg = new FolderBrowserDialog { SelectedPath = folder.Text }) if (dlg.ShowDialog(this) == DialogResult.OK) folder.Text = dlg.SelectedPath; }), 1, 0);
            root.Controls.Add(MainForm.Label("Папка:"), 0, 2); root.Controls.Add(pathPanel, 1, 2);
            var preview = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(3, 8, 3, 3) };
            root.Controls.Add(preview, 0, 3); root.SetColumnSpan(preview, 2);
            Func<PublishOptions> values = () => new PublishOptions { BaseName = OutputNames.Normalize(name.Text), Folder = folder.Text, Separate = mode.SelectedIndex == 1 };
            Action refresh = () =>
            {
                try
                {
                    var paths = OutputNames.Get(values(), Math.Max(1, sheetCount));
                    preview.Text = "Будет создано: " + Path.GetFileName(paths[0]) + (paths.Length > 1 ? " … " + Path.GetFileName(paths.Last()) + " (" + paths.Length + " файлов)" : "")
                        + "\n" + Path.GetDirectoryName(paths[0]) + "\nСуществующие PDF можно заменить после подтверждения.";
                }
                catch (Exception ex) { preview.Text = ex.Message; }
            };
            name.TextChanged += (s, e) => refresh(); folder.TextChanged += (s, e) => refresh(); mode.SelectedIndexChanged += (s, e) => refresh();
            var buttons = MainForm.Flow();
            var ok = MainForm.Button(publish ? "Создать PDF" : "Сохранить", () =>
            {
                var selected = values(); var paths = OutputNames.Get(selected, Math.Max(1, sheetCount));
                int existing = paths.Count(File.Exists);
                if (publish && existing > 0 && MessageBox.Show(this, "Заменить существующие PDF (" + existing + ")?\n\n" + string.Join("\n", paths.Where(File.Exists).Take(5).Select(Path.GetFileName))
                    + (existing > 5 ? "\n…" : "") + "\n\nЗамена начнётся после успешной подготовки всех листов.", "Перезапись PDF", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                options.BaseName = selected.BaseName; options.Folder = Path.GetFullPath(selected.Folder); options.Separate = selected.Separate;
                options.Overwrite = publish && existing > 0;
                DialogResult = DialogResult.OK; Close();
            });
            var cancel = MainForm.Button("Отмена", () => { DialogResult = DialogResult.Cancel; Close(); });
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 4); root.SetColumnSpan(buttons, 2);
            AcceptButton = ok; CancelButton = cancel;
            Shown += (s, e) => { name.Focus(); name.SelectAll(); }; refresh();
        }
    }
}
