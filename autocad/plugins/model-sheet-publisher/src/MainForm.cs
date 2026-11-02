using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Font = System.Drawing.Font;

namespace ModelSheetPublisher
{
    public enum UiAction { Close, Click, Window, Open, Publish, Preview }
    public sealed class UiCatalog
    {
        public List<string> Drawings = new List<string>();
        public List<string> Devices = new List<string>();
        public List<string> Styles = new List<string>();
        public string ActiveDrawing;
    }
    public sealed class MainForm : Form
    {
        private readonly QueueFile queue;
        private readonly DataGridView grid;
        private readonly ComboBox drawings, device, style, mode;
        private readonly TextBox folder, name;
        private readonly CheckBox lineweights, styles, transparency;
        private readonly Label count;
        public UiAction Action { get; private set; }
        public string Source => drawings.SelectedItem as string;
        public Sheet SelectedSheet => grid.CurrentRow?.Tag as Sheet;

        public MainForm(QueueFile data, UiCatalog catalog)
        {
            queue = data;
            Text = "Печать листов из модели · AutoCAD 2021–2026";
            Font = new Font("Segoe UI", 9F); StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1120, 750); MinimumSize = new Size(920, 660); AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 6 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            var sourcePanel = Flow();
            sourcePanel.Controls.Add(Label("Чертёж:"));
            drawings = Combo(555);
            foreach (var d in catalog.Drawings) drawings.Items.Add(d);
            drawings.SelectedItem = catalog.ActiveDrawing;
            sourcePanel.Controls.Add(drawings); sourcePanel.Controls.Add(Button("Открыть DWG…", () => Exit(UiAction.Open)));
            root.Controls.Add(sourcePanel, 0, 0);
            var select = Flow();
            select.Controls.Add(Button("+ Листы щелчками", () => Exit(UiAction.Click)));
            select.Controls.Add(Button("+ Области по 2 углам", () => Exit(UiAction.Window)));
            select.Controls.Add(Label("Enter / Esc возвращает в окно. Выбирайте внешнюю рамку листа.")); root.Controls.Add(select, 0, 1);

            grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                BackgroundColor = Color.White, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            AddColumn("№", 40); AddColumn("DWG", 240); AddColumn("Формат", 90); AddColumn("Бумага, мм", 125); AddColumn("Масштаб", 160); AddColumn("Центр", 55); AddColumn("Проверка", 160);
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Edit(); };
            root.Controls.Add(grid, 0, 2);
            var actions = Flow();
            actions.Controls.Add(Button("↑ Выше", () => MoveRows(-1))); actions.Controls.Add(Button("↓ Ниже", () => MoveRows(1)));
            actions.Controls.Add(Button("Параметры листов…", Edit)); actions.Controls.Add(Button("Удалить", Remove));
            actions.Controls.Add(Button("Сохранить очередь…", Save)); actions.Controls.Add(Button("Загрузить очередь…", LoadQueue));
            count = Label(""); actions.Controls.Add(count); root.Controls.Add(actions, 0, 3);

            var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(0, 6, 0, 6) };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            device = Combo(250); style = Combo(250); device.Dock = style.Dock = DockStyle.Fill;
            foreach (string d in catalog.Devices) if (d.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0 && d.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase)) device.Items.Add(d);
            if (!device.Items.Contains(queue.Options.Device)) device.Items.Add(queue.Options.Device);
            device.SelectedItem = queue.Options.Device;
            style.Items.Add("Без таблицы"); foreach (string s in catalog.Styles) style.Items.Add(s);
            style.SelectedItem = string.IsNullOrEmpty(queue.Options.Style) ? "Без таблицы" : queue.Options.Style;
            if (style.SelectedIndex < 0) { style.Items.Add(queue.Options.Style); style.SelectedItem = queue.Options.Style; }
            settings.Controls.Add(Label("PDF-плоттер:"), 0, 0); settings.Controls.Add(device, 1, 0); settings.Controls.Add(Label("CTB / STB:"), 2, 0); settings.Controls.Add(style, 3, 0);
            var flags = Flow(); styles = Check("Стили печати", queue.Options.UseStyles); lineweights = Check("Толщины линий", queue.Options.Lineweights); transparency = Check("Прозрачность", queue.Options.Transparency);
            flags.Controls.AddRange(new Control[] { styles, lineweights, transparency }); settings.Controls.Add(flags, 0, 1); settings.SetColumnSpan(flags, 4);
            mode = Combo(250); mode.Dock = DockStyle.Fill; mode.Items.AddRange(new object[] { "Один общий PDF", "Отдельный PDF на каждый лист" }); mode.SelectedIndex = queue.Options.Separate ? 1 : 0;
            name = new TextBox { Text = queue.Options.BaseName, Dock = DockStyle.Fill };
            settings.Controls.Add(Label("Результат:"), 0, 2); settings.Controls.Add(mode, 1, 2); settings.Controls.Add(Label("Имя файла:"), 2, 2); settings.Controls.Add(name, 3, 2);
            folder = new TextBox { Text = queue.Options.Folder, Dock = DockStyle.Fill };
            settings.Controls.Add(Label("Папка:"), 0, 3); settings.Controls.Add(folder, 1, 3); settings.SetColumnSpan(folder, 2);
            settings.Controls.Add(Button("Выбрать папку…", () => { using (var dlg = new FolderBrowserDialog { SelectedPath = folder.Text }) if (dlg.ShowDialog(this) == DialogResult.OK) folder.Text = dlg.SelectedPath; }), 3, 3);
            root.Controls.Add(settings, 0, 4);
            var bottom = Flow(); bottom.Controls.Add(Button("Создать PDF", () => Exit(UiAction.Publish))); bottom.Controls.Add(Button("Предпросмотр листа", () => Exit(UiAction.Preview)));
            bottom.Controls.Add(Button("Имя PDF…", () => EditOutput(false))); bottom.Controls.Add(Button("Справка", Help)); bottom.Controls.Add(Button("Закрыть", () => Close()));
            bottom.Controls.Add(Label("Двойной щелчок — формат, масштаб, поля и центрирование.")); root.Controls.Add(bottom, 0, 5);
            FormClosing += (s, e) => CaptureOptions(); RefreshRows();
        }

        private void AddColumn(string title, int weight) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        internal static FlowLayoutPanel Flow() => new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 2) };
        internal static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 6, 4) };
        internal static ComboBox Combo(int width) => new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(3, 3, 3, 5) };
        internal static CheckBox Check(string text, bool value) => new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(5, 6, 12, 5) };
        internal static Button Button(string title, System.Action action)
        {
            var button = new Button { Text = title, AutoSize = true, MinimumSize = new Size(75, 30), Padding = new Padding(5, 1, 5, 1), Margin = new Padding(3) };
            button.Click += (s, e) => { try { action(); } catch (System.Exception ex) { MessageBox.Show(button.FindForm(), ex.Message, "Печать листов", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
            return button;
        }
        private void CaptureOptions()
        {
            var o = queue.Options; o.Device = device.Text; o.Style = style.SelectedIndex == 0 ? "" : style.Text;
            o.UseStyles = styles.Checked; o.Lineweights = lineweights.Checked; o.Transparency = transparency.Checked;
            o.Separate = mode.SelectedIndex == 1; o.BaseName = name.Text; o.Folder = folder.Text;
        }
        private void Exit(UiAction action)
        {
            CaptureOptions();
            if ((action == UiAction.Click || action == UiAction.Window) && Source == null) return;
            if (action == UiAction.Publish)
            {
                if (queue.Sheets.Count == 0) throw new InvalidOperationException("Добавьте листы в очередь.");
                if (queue.Sheets.Any(s => !string.IsNullOrEmpty(s.Warning)) && MessageBox.Show(this, "У некоторых листов есть предупреждения. Продолжить печать?", "Проверка листов", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                if (!EditOutput(true)) return;
            }
            if (action == UiAction.Preview && SelectedSheet == null) return;
            Action = action; Close();
        }
        private bool EditOutput(bool publish)
        {
            CaptureOptions(); queue.Options.Overwrite = false;
            using (var dialog = new OutputForm(queue.Options, queue.Sheets.Count, publish))
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            name.Text = queue.Options.BaseName; folder.Text = queue.Options.Folder; mode.SelectedIndex = queue.Options.Separate ? 1 : 0;
            return true;
        }
        private void RefreshRows(IEnumerable<Sheet> selected = null)
        {
            var selection = selected?.ToList() ?? new List<Sheet>(); grid.Rows.Clear();
            for (int i = 0; i < queue.Sheets.Count; i++)
            {
                var s = queue.Sheets[i]; int row = grid.Rows.Add(i + 1, s.Drawing, s.Format, s.Size, s.Scale, s.Center ? "Да" : "Нет", s.Warning);
                grid.Rows[row].Tag = s; grid.Rows[row].Cells[1].ToolTipText = s.Source;
                if (!string.IsNullOrEmpty(s.Warning)) grid.Rows[row].DefaultCellStyle.BackColor = Color.LemonChiffon;
            }
            grid.ClearSelection(); foreach (DataGridViewRow row in grid.Rows) row.Selected = selection.Contains(row.Tag as Sheet);
            count.Text = "Листов: " + queue.Sheets.Count;
        }
        private List<Sheet> Selected() => grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => (Sheet)r.Tag).ToList();
        private void MoveRows(int direction)
        {
            var selected = Selected(); var order = direction < 0 ? selected : selected.AsEnumerable().Reverse();
            foreach (var sheet in order)
            {
                int from = queue.Sheets.IndexOf(sheet), to = from + direction;
                if (to < 0 || to >= queue.Sheets.Count || selected.Contains(queue.Sheets[to])) continue;
                queue.Sheets.RemoveAt(from); queue.Sheets.Insert(to, sheet);
            }
            RefreshRows(selected);
        }
        private void Remove() { foreach (var s in Selected()) queue.Sheets.Remove(s); RefreshRows(); }
        private void Edit()
        {
            var selected = Selected(); if (selected.Count == 0) return;
            using (var dialog = new SheetForm(selected)) if (dialog.ShowDialog(this) == DialogResult.OK) RefreshRows(selected);
        }
        private void Save()
        {
            CaptureOptions(); using (var dialog = new SaveFileDialog { Filter = "Очередь печати (*.mspqueue)|*.mspqueue", FileName = queue.Options.BaseName + ".mspqueue" })
                if (dialog.ShowDialog(this) == DialogResult.OK) QueueStore.Save(dialog.FileName, queue);
        }
        private void LoadQueue()
        {
            using (var dialog = new OpenFileDialog { Filter = "Очередь печати (*.mspqueue)|*.mspqueue" })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    var loaded = QueueStore.Load(dialog.FileName);
                    if (queue.Sheets.Count > 0 && MessageBox.Show(this, "Заменить текущую очередь загруженной?", "Загрузка очереди", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    queue.Sheets = loaded.Sheets; queue.Options = loaded.Options;
                    var o = queue.Options; name.Text = o.BaseName; folder.Text = o.Folder; mode.SelectedIndex = o.Separate ? 1 : 0;
                    if (!device.Items.Contains(o.Device)) device.Items.Add(o.Device); device.SelectedItem = o.Device;
                    string styleName = string.IsNullOrEmpty(o.Style) ? "Без таблицы" : o.Style;
                    if (!style.Items.Contains(styleName)) style.Items.Add(styleName); style.SelectedItem = styleName;
                    styles.Checked = o.UseStyles; lineweights.Checked = o.Lineweights; transparency.Checked = o.Transparency;
                    RefreshRows();
                }
        }
        private void Help() => MessageBox.Show(this,
            "1. Выберите DWG и добавьте листы щелчками или областями по двум углам.\n2. Enter/Esc возвращает в очередь.\n3. Выделите строки (Ctrl/Shift) и задайте параметры листов. Кнопки ↑/↓ меняют порядок.\n4. Выберите PDF-плоттер, CTB/STB и папку. Проверьте один лист через предпросмотр.\n5. Создайте общий PDF или отдельные файлы с номерами.\n\nФормат определяется по геометрии; проверьте размер при нестандартном масштабе или выборе внутренней рамки. Поддерживаются прямоугольники, параллельные мировым X/Y; для остальных используйте область.\n\nОчередь можно сохранить. При печати используются текущие данные открытых DWG; закрытые файлы читаются с диска. Исходные DWG не сохраняются плагином.\n\nКачество PDF задаётся выбранным PC3. Длинные форматы собираются из векторных участков без уменьшения масштаба. Требуются шрифты и внешние ссылки исходного чертежа.",
            "Как пользоваться", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public sealed class SheetForm : Form
    {
        public SheetForm(IList<Sheet> sheets)
        {
            Text = sheets.Count == 1 ? "Параметры листа" : "Параметры выбранных листов: " + sheets.Count;
            Font = new Font("Segoe UI", 9); AutoScaleMode = AutoScaleMode.Dpi; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            var s = sheets[0];
            var root = new TableLayoutPanel { AutoSize = true, Padding = new Padding(14), ColumnCount = 2, Dock = DockStyle.Fill }; Controls.Add(root);
            var paper = MainForm.Combo(300); paper.Items.Add("Сохранить размер каждого листа"); paper.Items.AddRange(Paper.Catalog().Cast<object>().ToArray()); paper.Items.Add("Нестандартный"); paper.SelectedIndex = 0;
            NumericUpDown width = Number(s.PaperWidth, 10, 5080), height = Number(s.PaperHeight, 10, 5080);
            var dims = MainForm.Flow(); dims.Controls.AddRange(new Control[] { width, MainForm.Label("×"), height, MainForm.Label("мм") });
            var resize = MainForm.Check("Применить размер ко всем выбранным", sheets.Count == 1);
            paper.SelectedIndexChanged += (a, e) => { if (paper.SelectedItem is Paper p) { width.Value = (decimal)p.Width; height.Value = (decimal)p.Height; resize.Checked = true; } };
            var rotate = MainForm.Button("Поменять ширину / высоту", () => { decimal old = width.Value; width.Value = height.Value; height.Value = old; resize.Checked = true; });
            var fit = MainForm.Check("Вписать в лист", s.Fit); var center = MainForm.Check("Центрировать", s.Center);
            NumericUpDown numerator = Number(s.PaperUnits, .000001, 1000000, 6), denominator = Number(s.DrawingUnits, .000001, 10000000, 6);
            var scale = MainForm.Flow(); scale.Controls.AddRange(new Control[] { numerator, MainForm.Label("мм ="), denominator, MainForm.Label("ед. модели") });
            scale.Enabled = !fit.Checked; fit.CheckedChanged += (a, e) => scale.Enabled = !fit.Checked;
            NumericUpDown margin = Number(s.Margin, 0, 1000), x = Number(s.OffsetX, -5080, 5080), y = Number(s.OffsetY, -5080, 5080);
            var offsets = MainForm.Flow(); offsets.Controls.AddRange(new Control[] { MainForm.Label("X"), x, MainForm.Label("Y"), y }); offsets.Enabled = !center.Checked;
            center.CheckedChanged += (a, e) => offsets.Enabled = !center.Checked;
            int row = 0;
            Add(root, row++, "Формат:", paper); Add(root, row++, "Размер:", dims); Add(root, row++, "", resize); Add(root, row++, "Ориентация:", rotate);
            Add(root, row++, "Масштаб:", fit); Add(root, row++, "Точный масштаб:", scale); Add(root, row++, "Размещение:", center);
            Add(root, row++, "Поля, мм:", margin); Add(root, row++, "Смещение, мм:", offsets);
            var note = MainForm.Label("Без вписывания выход за границы бумаги обрезается.\nОбщие параметры применятся ко всем выбранным строкам."); root.Controls.Add(note, 0, row++); root.SetColumnSpan(note, 2);
            var buttons = MainForm.Flow();
            buttons.Controls.Add(MainForm.Button("Применить", () =>
            {
                foreach (var item in sheets)
                {
                    double w = resize.Checked ? (double)width.Value : item.PaperWidth, h = resize.Checked ? (double)height.Value : item.PaperHeight;
                    if ((double)margin.Value * 2 >= Math.Min(w, h)) throw new InvalidOperationException("Поля должны быть меньше половины размера бумаги.");
                }
                foreach (var item in sheets)
                {
                    if (resize.Checked)
                    {
                        bool changed = Math.Abs(item.PaperWidth - (double)width.Value) > .001 || Math.Abs(item.PaperHeight - (double)height.Value) > .001;
                        item.PaperWidth = (double)width.Value; item.PaperHeight = (double)height.Value;
                        if (paper.SelectedItem is Paper p) item.Format = p.Name;
                        else if (changed || paper.SelectedIndex > 0) item.Format = Paper.Guess(new Box(0, 0, item.PaperWidth, item.PaperHeight), 1).Name;
                    }
                    item.Fit = fit.Checked; item.Center = center.Checked; item.PaperUnits = (double)numerator.Value; item.DrawingUnits = (double)denominator.Value;
                    item.Margin = (double)margin.Value; item.OffsetX = (double)x.Value; item.OffsetY = (double)y.Value;
                }
                DialogResult = DialogResult.OK; Close();
            })); buttons.Controls.Add(MainForm.Button("Отмена", Close)); root.Controls.Add(buttons, 0, row); root.SetColumnSpan(buttons, 2);
        }
        private static NumericUpDown Number(double value, double min, double max, int decimals = 2) => new NumericUpDown { DecimalPlaces = decimals, Minimum = (decimal)min, Maximum = (decimal)max, Value = (decimal)Math.Max(min, Math.Min(max, value)), Width = 110 };
        private static void Add(TableLayoutPanel root, int row, string text, Control value) { root.Controls.Add(MainForm.Label(text), 0, row); root.Controls.Add(value, 1, row); }
    }

    public sealed class ProgressForm : Form
    {
        private readonly ProgressBar bar;
        private readonly Label label;
        private bool cancelled;
        private bool completed;
        public ProgressForm(int total)
        {
            Text = "Создание PDF"; Size = new Size(560, 160); Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterScreen;
            ControlBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3 }; Controls.Add(root);
            label = MainForm.Label("Подготовка…"); bar = new ProgressBar { Dock = DockStyle.Top, Maximum = total };
            root.Controls.Add(label); root.Controls.Add(bar); root.Controls.Add(MainForm.Button("Отменить после текущего участка", () => { cancelled = true; label.Text = "Отмена…"; }));
            FormClosing += (sender, args) => { if (!completed) { cancelled = true; args.Cancel = true; } };
        }
        public void Complete() { completed = true; Close(); }
        public bool Report(int value, string text)
        {
            label.Text = text; bar.Value = Math.Min(bar.Maximum, value); System.Windows.Forms.Application.DoEvents(); return !cancelled;
        }
    }
}
