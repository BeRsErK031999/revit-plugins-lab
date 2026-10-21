using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.Tep;

namespace TrueBIM.Revit.TepHarness;

/// <summary>Opt-in read-only integration check. Input must be an isolated copy with links already repathed.</summary>
public sealed class TepHarnessApplication : IExternalApplication
{
    private bool _ran;
    private string? _output;
    public Result OnStartup(UIControlledApplication application)
    {
        _output = Environment.GetEnvironmentVariable("TRUEBIM_TEP_TEST_OUTPUT");
        if (!string.IsNullOrWhiteSpace(_output)) application.Idling += Run;
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication application) { application.Idling -= Run; return Result.Succeeded; }
    private void Log(string message) => File.AppendAllText(_output + ".log", DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
    private void Run(object? sender, IdlingEventArgs args)
    {
        if (_ran) return;
        _ran = true;
        Document? document = null;
        try
        {
            UIApplication app = (UIApplication)sender!;
            string model = Environment.GetEnvironmentVariable("TRUEBIM_TEP_TEST_MODEL") ?? throw new InvalidOperationException("No model.");
            Log("Opening " + model);
            using OpenOptions options = new() { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
            document = app.Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(model), options);
            Log("Opened");
            IReadOnlyList<TepDocumentSource> docs = TepCollector.Documents(document);
            TepSourceSelection selection = new()
            {
                B1 = docs.Single(d => d.Name.Contains("_Б1_")),
                B2 = docs.Single(d => d.Name.Contains("_Б2_")),
                Stylobate = docs.Single(d => d.Name.Contains("_СТЛБ_")),
                Areas = docs.Single(d => d.Name.Contains("_Б2_"))
            };
            TepSnapshot snapshot = new TepCollector().Collect(document, selection);
            Log("Collected " + snapshot.Items.Count);
            TepOptions calculationOptions = new();
            TepCalculation calculation = new TepCalculator().Calculate(snapshot, calculationOptions);
            JavaScriptSerializer serializer = new() { MaxJsonLength = int.MaxValue };
            File.WriteAllText(_output + ".snapshot.json", serializer.Serialize(snapshot));
            File.WriteAllText(_output + ".result.json", serializer.Serialize(calculation));
            string? template = Environment.GetEnvironmentVariable("TRUEBIM_TEP_TEST_TEMPLATE");
            if (!string.IsNullOrEmpty(template)) new TepExcel().Export(template, _output + ".xlsx", snapshot, calculationOptions, calculation);
            // Evidence for parking scope: actual coordinates and room containment, not category count alone.
            Room[] parkingRooms = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>()
                .Where(r => r.Area > 0 && r.LookupParameter("• Группа помещений • Тип")?.AsString() == "Парковка").ToArray();
            var parking = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Parking).WhereElementIsNotElementType()
                .Select(p =>
                {
                    XYZ? point = (p.Location as LocationPoint)?.Point;
                    return new
                    {
                        Id = p.Id.Value, Mark = p.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
                        Level = (document.GetElement(p.LevelId) as Level)?.Name,
                        X = point?.X, Y = point?.Y, Z = point?.Z,
                        Rooms = point is null ? [] : parkingRooms.Where(r => r.IsPointInRoom(point) || r.IsPointInRoom(point + XYZ.BasisZ * 0.5))
                            .Select(r => r.Id.Value).ToArray()
                    };
                }).ToArray();
            File.WriteAllText(_output + ".parking.json", serializer.Serialize(parking));
            File.WriteAllText(_output + ".done", "Complete; model modified=" + document.IsModified);
            Log("Done");
        }
        catch (Exception exception) { File.WriteAllText(_output + ".error", exception.ToString()); Log("Failed " + exception.Message); }
        finally { if (document is not null) document.Close(false); }
    }
}
