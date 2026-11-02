using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using CadUI = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(ModelSheetPublisher.Commands))]
namespace ModelSheetPublisher
{
    public sealed class Commands
    {
        private static QueueFile queue = new QueueFile();
        private static bool running;
        [CommandMethod("MSPUBLISH", CommandFlags.Session)]
        public void ShowPublisher()
        {
            if (running) return;
            running = true;
            try
            {
                if (AcApp.DocumentManager.MdiActiveDocument == null) { AcApp.ShowAlertDialog("Откройте чертёж DWG."); return; }
                if (queue.Sheets.Count == 0) queue.Options.BaseName = Path.GetFileNameWithoutExtension(AcApp.DocumentManager.MdiActiveDocument.Name);
                while (true)
                {
                    var catalog = new UiCatalog { ActiveDrawing = AcApp.DocumentManager.MdiActiveDocument?.Name };
                    foreach (Document d in AcApp.DocumentManager) catalog.Drawings.Add(d.Name);
                    catalog.Devices.AddRange(PlotSettingsValidator.Current.GetPlotDeviceList().Cast<string>());
                    catalog.Styles.AddRange(PlotSettingsValidator.Current.GetPlotStyleSheetList().Cast<string>());
                    using (var form = new MainForm(queue, catalog))
                    {
                        CadUI.ShowModalDialog(form);
                        if (form.Action == UiAction.Close) break;
                        try
                        {
                            switch (form.Action)
                            {
                                case UiAction.Click:
                                case UiAction.Window:
                                    Select(form.Source, form.Action == UiAction.Click); break;
                                case UiAction.Open:
                                    OpenDrawings(); break;
                                case UiAction.Publish:
                                    RunPublish(queue.Sheets, queue.Options); break;
                                case UiAction.Preview:
                                    if (form.SelectedSheet != null) Preview(form.SelectedSheet); break;
                            }
                        }
                        catch (OperationCanceledException) { AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nПечать отменена. Очередь сохранена в окне плагина.\n"); }
                        catch (System.Exception ex) { AcApp.ShowAlertDialog("Model Sheet Publisher\n\n" + ex.Message); }
                    }
                }
            }
            finally { running = false; }
        }

        private static void OpenDrawings()
        {
            using (var dialog = new OpenFileDialog { Filter = "Чертежи AutoCAD (*.dwg)|*.dwg", Multiselect = true, Title = "Открыть DWG для выбора листов (только чтение)" })
                if (dialog.ShowDialog() == DialogResult.OK)
                    foreach (var path in dialog.FileNames) AcApp.DocumentManager.MdiActiveDocument = Publisher.FindDocument(path);
        }

        private static void Select(string source, bool click)
        {
            var doc = Publisher.FindDocument(source); AcApp.DocumentManager.MdiActiveDocument = doc;
            using (doc.LockDocument())
            {
                var ed = doc.Editor;
                if (LayoutManager.Current.CurrentLayout != "Model") LayoutManager.Current.CurrentLayout = "Model";
                using (var view = ed.GetCurrentView())
                {
                    if (!view.ViewDirection.GetNormal().IsEqualTo(Vector3d.ZAxis, new Tolerance(1e-6, 1e-6)) || Math.Abs(view.ViewTwist) > 1e-6 || view.PerspectiveEnabled)
                        throw new InvalidOperationException("Для выбора установите вид сверху в мировой системе координат: команда _PLAN → _World. Затем повторите выбор.");
                }
                double mm = UnitsConverter.GetConversionFactor(doc.Database.Insunits == UnitsValue.Undefined ? UnitsValue.Millimeters : doc.Database.Insunits, UnitsValue.Millimeters);
                List<Box> frames = click ? FrameFinder.Read(doc.Database) : null;
                ed.WriteMessage(click ? "\nОДИН ЩЕЛЧОК по объекту рамки: полилинии, отрезку или блоку. Enter/Esc — готово.\n" : "\nДВА УГЛА: задайте прямоугольную область печати. Enter/Esc — готово.\n");
                var outlines = new List<Polyline>();
                try
                {
                  while (true)
                  {
                    Box box;
                    if (click)
                    {
                        var prompt = new PromptEntityOptions("\nРамка листа (один щелчок; Enter — готово): ") { AllowNone = true };
                        prompt.SetRejectMessage("\nВыберите полилинию, отрезок или блок рамки.");
                        prompt.AddAllowedClass(typeof(Polyline), false); prompt.AddAllowedClass(typeof(Line), false); prompt.AddAllowedClass(typeof(BlockReference), false);
                        var picked = ed.GetEntity(prompt);
                        if (picked.Status != PromptStatus.OK) break;
                        box = FrameFinder.FromEntity(doc.Database, picked.ObjectId, picked.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem), frames);
                        if (box == null) { ed.WriteMessage("\nУ выбранного объекта не найдена замкнутая прямоугольная рамка. Выберите внешнюю границу или используйте «Область по 2 углам».\n"); continue; }
                    }
                    else
                    {
                        var first = ed.GetPoint(new PromptPointOptions("\nПервый угол области: ") { AllowNone = true });
                        if (first.Status != PromptStatus.OK) break;
                        var a = first.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                        var second = ed.GetCorner(new PromptCornerOptions("\nПротивоположный угол: ", first.Value));
                        if (second.Status != PromptStatus.OK) break;
                        var b = second.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                        box = new Box(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
                        if (box.Width < 1e-6 || box.Height < 1e-6) { ed.WriteMessage("\nОбласть должна иметь ненулевую ширину и высоту.\n"); continue; }
                    }
                    if (queue.Sheets.Any(s => s.Source.Equals(source, StringComparison.OrdinalIgnoreCase) && s.Window.ToString() == box.ToString()))
                    { ed.WriteMessage("\nЭтот лист уже добавлен.\n"); continue; }
                    var sheet = Sheet.Create(source, box, mm); queue.Sheets.Add(sheet);
                    ed.WriteMessage($"\nДобавлен № {queue.Sheets.Count}: {sheet.Format}, {sheet.Size} мм. Размер можно исправить в окне.\n");
                    var outline = new Polyline(4) { Closed = true, ColorIndex = 3, ConstantWidth = 0 };
                    outline.AddVertexAt(0, new Point2d(box.X, box.Y), 0, 0, 0); outline.AddVertexAt(1, new Point2d(box.Right, box.Y), 0, 0, 0);
                    outline.AddVertexAt(2, new Point2d(box.Right, box.Top), 0, 0, 0); outline.AddVertexAt(3, new Point2d(box.X, box.Top), 0, 0, 0);
                    outlines.Add(outline);
                    TransientManager.CurrentTransientManager.AddTransient(outline, TransientDrawingMode.Highlight, 128, new IntegerCollection());
                  }
                }
                finally
                {
                    foreach (var outline in outlines)
                    {
                        TransientManager.CurrentTransientManager.EraseTransient(outline, new IntegerCollection()); outline.Dispose();
                    }
                    ed.Regen();
                }
            }
        }

        private static void Preview(Sheet sheet)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ModelSheetPublisher", "preview-" + Guid.NewGuid().ToString("N"));
            var o = queue.Options;
            var options = new PublishOptions { Folder = folder, BaseName = "Предпросмотр", Device = o.Device, Style = o.Style, Lineweights = o.Lineweights, UseStyles = o.UseStyles, Transparency = o.Transparency };
            RunPublish(new[] { sheet }, options, false);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(folder, "Предпросмотр.pdf")) { UseShellExecute = true });
        }

        private static void RunPublish(IList<Sheet> sheets, PublishOptions options, bool notify = true)
        {
            using (var progress = new ProgressForm(sheets.Count))
            {
                // Document open/switch requires the Session command context, outside a modal form callback.
                CadUI.ShowModelessDialog(progress);
                try { Publisher.Publish(sheets, options, progress.Report); }
                finally { progress.Complete(); }
            }
            if (notify) AcApp.ShowAlertDialog($"Готово: {sheets.Count} листов.\n\n{options.Folder}\n\n" + (options.Separate ? "Созданы отдельные PDF." : "Создан общий PDF."));
        }
    }
}
