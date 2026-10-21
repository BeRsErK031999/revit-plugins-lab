using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Vernadskogo.App.Core;
using Vernadskogo.App.Revit;

namespace Vernadskogo.RevitHarness;

public sealed class ModelAuditApplication : IExternalApplication
{
    private readonly List<string> passed = new();
    private readonly Dictionary<string, object> facts = new();
    private string? reportPath;
    private bool started;

    public Result OnStartup(UIControlledApplication application)
    {
        reportPath = Environment.GetEnvironmentVariable("VERNADSKOGO_AUDIT_REPORT");
        if (!string.IsNullOrEmpty(reportPath))
        {
            new Vernadskogo.App.App().OnStartup(application);
            Check(application.GetRibbonPanels("Вернадского").Any(p => p.Name == "АР"
                && p.GetItems().Any(i => i.Name == "Vernadskogo.MarkPanels")),
                "Ribbon tab Вернадского / panel АР / marking command registered");
            application.Idling += Run;
            application.DialogBoxShowing += Dialog;
            Progress("Harness loaded; waiting for Revit.");
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= Run;
        application.DialogBoxShowing -= Dialog;
        return Result.Succeeded;
    }

    private void Dialog(object sender, DialogBoxShowingEventArgs args)
    {
        if (args is TaskDialogShowingEventArgs dialog)
            Progress("Revit dialog: " + dialog.DialogId + " | " + dialog.Message);
    }

    private void Run(object sender, IdlingEventArgs args)
    {
        if (started || !(sender is UIApplication application)) return;
        started = true;
        string? failure = null;
        Document? document = null;
        try
        {
            string model = Environment.GetEnvironmentVariable("VERNADSKOGO_AUDIT_MODEL")
                ?? throw new InvalidOperationException("No disposable model path.");
            Progress("Opening disposable copy: " + model);
            using var options = new OpenOptions { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
            document = application.Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(model), options);
            facts["RevitVersion"] = application.Application.VersionBuild;
            string building = PanelNumbering.ParseBuilding(document.Title);
            PanelData data = PanelReader.Read(document);
            facts["PanelCount"] = data.Panels.Count;
            facts["Floors"] = data.Panels.GroupBy(p => p.Floor).OrderBy(g => int.Parse(g.Key))
                .Select(g => new { Floor = g.Key, Count = g.Count() }).ToArray();
            facts["Materials"] = data.Materials;
            facts["MaterialParameter"] = "Материал несущих конструкций";
            Check(data.Panels.Count > 0, "Matching panels collected from the supplied model");
            Check(data.Panels.All(p =>
            {
                Element type = document.GetElement(document.GetElement(new ElementId(p.Id)).GetTypeId());
                Parameter material = type.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM)
                    ?? PanelReader.NamedParameter(type, "Материал несущих конструкций")!;
                return document.GetElement(material.AsElementId()).UniqueId == p.MaterialKey;
            }), "Every panel uses its type structural material");
            var floorParameters = data.Panels.Select(p => PanelReader.NamedParameter(document.GetElement(new ElementId(p.Id)), "GP_Этаж")!).ToArray();
            facts["FloorParameterTypes"] = floorParameters.GroupBy(p => p.StorageType + " / " + p.Definition.GetDataType().TypeId)
                .ToDictionary(g => g.Key, g => g.Count());
            facts["ReportedElement"] = Inspect(document.GetElement(new ElementId(2626276L)));
            Progress("Read " + data.Panels.Count + " panels; " + data.Materials.Count + " materials.");

            var numbers = data.Materials.OrderBy(p => p.Value, StringComparer.Ordinal).Select((pair, index) => new { pair.Key, Number = (index + 1).ToString(CultureInfo.InvariantCulture) })
                .ToDictionary(p => p.Key, p => p.Number);
            facts["TestMaterialNumbers"] = numbers;
            facts["Note"] = "Material numbers are test fixtures only; original RVT is never modified.";
            var original = Snapshot(document, data);
            int originalParameterCount = new FilteredElementCollector(document).OfClass(typeof(ParameterElement)).GetElementCount();
            PanelSettings originalSettings = SettingsStorage.Read(document);
            var south = PanelNumbering.CreateMarks(data.Panels, building, numbers, StartDirection.South);

            using (var group = new TransactionGroup(document, "Disposable marking regression"))
            {
                group.Start();
                Progress("Writing all panels, start South.");
                PanelWriter.Write(document, south, numbers, StartDirection.South);
                Verify(document, south);
                Check(true, "All panels: section, numeric floor and complete mark written and read back");
                facts["ReportedElementAfterWrite"] = Inspect(document.GetElement(new ElementId(2626276L)));
                Progress("Repeating identical write.");
                PanelWriter.Write(document, south, numbers, StartDirection.South);
                Verify(document, south);
                Check(true, "Repeated marking with unchanged values succeeds");
                var northeast = PanelNumbering.CreateMarks(data.Panels, building, numbers, StartDirection.NorthEast);
                Progress("Writing all panels, start NorthEast.");
                PanelWriter.Write(document, northeast, numbers, StartDirection.NorthEast);
                Verify(document, northeast);
                Check(northeast.Any(m => south.Single(s => s.Panel.Id == m.Panel.Id).Mark != m.Mark), "Changing the starting direction changes numbering");
                Check(SettingsStorage.Read(document).Direction == StartDirection.NorthEast, "Last direction stored inside model");
                Check(numbers.All(p => SettingsStorage.Read(document).Materials[p.Key] == p.Value), "All material mappings stored inside model");
                Check(new FilteredElementCollector(document).OfClass(typeof(ParameterElement)).GetElementCount() == originalParameterCount,
                    "No project or shared parameter definitions added");
                group.RollBack();
            }
            Check(Snapshot(document, data).OrderBy(p => p.Key).SequenceEqual(original.OrderBy(p => p.Key)),
                "Transaction-group rollback restores every original panel value");
            PanelSettings restored = SettingsStorage.Read(document);
            Check(restored.Direction == originalSettings.Direction && restored.Materials.OrderBy(p => p.Key)
                .SequenceEqual(originalSettings.Materials.OrderBy(p => p.Key)), "Rollback restores previous settings");

            Progress("Writing and saving a marked test copy for reopen verification.");
            PanelWriter.Write(document, south, numbers, StartDirection.South);
            string saved = Path.Combine(Path.GetDirectoryName(model)!, "verified_К" + building + "_panels.rvt");
            using (var save = new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 }) document.SaveAs(saved, save);
            document.Close(false);
            document = application.Application.OpenDocumentFile(saved);
            Verify(document, south);
            Check(true, "All panel values survive saving and reopening RVT");
            PanelSettings reopened = SettingsStorage.Read(document);
            Check(reopened.Direction == StartDirection.South && numbers.All(p => reopened.Materials[p.Key] == p.Value),
                "Material mappings and direction survive saving and reopening RVT");
            Check(PanelReader.Read(document).Panels.Count == data.Panels.Count, "All panels still found after reopen");
            facts["VerifiedCopy"] = saved;
        }
        catch (Exception exception)
        {
            failure = exception.ToString();
            Progress("FAIL: " + failure);
        }
        finally
        {
            if (document != null && document.IsValidObject)
            {
                try { document.Close(false); }
                catch (Exception exception) { failure = (failure ?? "") + "\nClose: " + exception; }
            }
            File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = failure, Passed = passed, Facts = facts }));
        }
    }

    private static void Verify(Document document, IReadOnlyList<PanelMark> marks)
    {
        foreach (PanelMark mark in marks)
        {
            Element element = document.GetElement(new ElementId(mark.Panel.Id));
            if (PanelReader.NamedParameter(element, "• Секция")!.AsString() != mark.Section
                || element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() != mark.Mark)
                throw new InvalidOperationException("Wrong section or mark: " + element.Id.Value);
            Parameter floor = PanelReader.NamedParameter(element, "GP_Этаж")!;
            double expected = double.Parse(mark.Panel.Floor, CultureInfo.InvariantCulture);
            bool correct = floor.StorageType == StorageType.Double ? Math.Abs(floor.AsDouble() - expected) < 1e-9
                : floor.StorageType == StorageType.Integer ? floor.AsInteger() == expected : floor.AsString() == mark.Panel.Floor;
            if (!correct) throw new InvalidOperationException("Wrong floor: " + element.Id.Value);
        }
        if (marks.Select(m => m.Mark).Distinct().Count() != marks.Count) throw new InvalidOperationException("Duplicate marks");
        foreach (var floor in marks.GroupBy(m => m.Panel.Floor))
        {
            int[] ordinals = floor.Select(m => int.Parse(m.Mark.Split('_').Last())).OrderBy(i => i).ToArray();
            if (!ordinals.SequenceEqual(Enumerable.Range(1, floor.Count()))) throw new InvalidOperationException("Non-contiguous floor numbering");
        }
    }

    private static Dictionary<long, string> Snapshot(Document document, PanelData data) => data.Panels.ToDictionary(p => p.Id, p =>
    {
        Element element = document.GetElement(new ElementId(p.Id));
        Parameter floor = PanelReader.NamedParameter(element, "GP_Этаж")!;
        string floorValue = floor.StorageType == StorageType.Double ? floor.AsDouble().ToString("R", CultureInfo.InvariantCulture)
            : floor.StorageType == StorageType.Integer ? floor.AsInteger().ToString(CultureInfo.InvariantCulture) : floor.AsString();
        return PanelReader.NamedParameter(element, "• Секция")!.AsString() + "|" + floorValue + "|"
            + element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString();
    });

    private static object Inspect(Element? element)
    {
        if (element == null) return "Element not present";
        Parameter? parameter = PanelReader.NamedParameter(element, "GP_Этаж");
        return new { ElementId = element.Id.Value, Name = element.Name, StorageType = parameter?.StorageType.ToString(),
            Spec = parameter?.Definition.GetDataType().TypeId, Display = parameter?.AsValueString(),
            Numeric = parameter?.StorageType == StorageType.Double ? parameter.AsDouble() : (double?)null };
    }

    private void Check(bool condition, string text)
    {
        if (!condition) throw new InvalidOperationException(text);
        passed.Add(text);
        Progress("PASS: " + text);
    }

    private void Progress(string text) => File.AppendAllText(reportPath + ".progress.txt", DateTime.Now.ToString("HH:mm:ss") + " " + text + Environment.NewLine);
}
