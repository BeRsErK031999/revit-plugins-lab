using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

namespace TrueBIM.Revit.ReplacementHarness;

public sealed class ReplacementHarnessApplication : IExternalApplication
{
    private readonly List<object> results = new();
    private string? reportPath;
    private UIDocument? uiDocument;
    private FamilySymbol? sourceType;
    private FamilySymbol? targetType;
    private Level? level;
    private List<ElementId> ids = new();
    private int scenario;
    private int phase;
    private bool busy;

    public Result OnStartup(UIControlledApplication application)
    {
        reportPath = Environment.GetEnvironmentVariable("TRUEBIM_REPLACEMENT_HARNESS_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllText(reportPath + ".progress.txt", "Harness loaded; waiting for Idling.");
            application.Idling += OnIdling;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdling;
        return Result.Succeeded;
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (busy || phase < 0 || sender is not UIApplication application)
            return;
        busy = true;
        try
        {
            if (sourceType is null)
            {
                Initialize(application);
                return;
            }
            if (phase == 0)
            {
                Prepare();
                phase = 1;
            }
            else if (phase++ >= 3)
            {
                RunScenario();
                scenario++;
                phase = scenario < 4 ? 0 : -1;
                if (phase < 0)
                    WriteReport(null);
            }
            args.SetRaiseWithoutDelay();
        }
        catch (Exception exception)
        {
            phase = -1;
            WriteReport(exception.ToString());
        }
        finally
        {
            busy = false;
            if (phase >= 0) args.SetRaiseWithoutDelay();
        }
    }

    private void Initialize(UIApplication application)
    {
        if (uiDocument is null)
        {
            File.AppendAllText(reportPath + ".progress.txt", "\nOpening template.");
            // Return to Revit before setting the active view in the next Idling event.
            uiDocument = application.OpenAndActivateDocument(@"C:\ProgramData\Autodesk\RVT 2022\Templates\English\DefaultMetric.rte");
            return;
        }
        Document document = uiDocument.Document;
        ViewPlan view = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .First(item => !item.IsTemplate && item.ViewType == ViewType.FloorPlan);
        if (uiDocument.ActiveView.Id != view.Id)
            uiDocument.ActiveView = view;
        level = view.GenLevel;
        using Document first = MakeFamily(application.Application, BuiltInCategory.OST_FireAlarmDevices, "Source");
        using Document second = MakeFamily(application.Application, BuiltInCategory.OST_NurseCallDevices, "Target");
        Family source = first.LoadFamily(document);
        Family target = second.LoadFamily(document);
        sourceType = (FamilySymbol)document.GetElement(source.GetFamilySymbolIds().First());
        targetType = (FamilySymbol)document.GetElement(target.GetFamilySymbolIds().First());
        first.Close(false);
        second.Close(false);
        using Transaction transaction = new(document, "Replacement fixture reference walls");
        transaction.Start();
        WallType wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().First();
        Wall.Create(document, Line.CreateBound(new XYZ(-5, -5, level.Elevation), new XYZ(50, -5, level.Elevation)), wallType.Id, level.Id, 10, 0, false, false);
        Wall.Create(document, Line.CreateBound(new XYZ(-5, -5, level.Elevation), new XYZ(-5, 20, level.Elevation)), wallType.Id, level.Id, 10, 0, false, false);
        transaction.Commit();
    }

    private static Document MakeFamily(Application application, BuiltInCategory category, string typeName)
    {
        Document family = application.NewFamilyDocument(@"C:\ProgramData\Autodesk\RVT 2022\Family Templates\English\Metric Generic Model.rft");
        using Transaction transaction = new(family, "Replacement test family");
        transaction.Start();
        family.OwnerFamily.FamilyCategory = family.Settings.Categories.get_Item(category);
        family.FamilyManager.NewType(typeName);
        CurveArray profile = new();
        XYZ[] points = { new(-0.5, -0.5, 0), new(0.5, -0.5, 0), new(0.5, 0.5, 0), new(-0.5, 0.5, 0) };
        for (int index = 0; index < points.Length; index++)
            profile.Append(Line.CreateBound(points[index], points[(index + 1) % points.Length]));
        CurveArrArray profiles = new();
        profiles.Append(profile);
        SketchPlane plane = SketchPlane.Create(family, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        family.FamilyCreate.NewExtrusion(true, profiles, plane, 1);
        transaction.Commit();
        return family;
    }

    private void Prepare()
    {
        Document document = uiDocument!.Document;
        uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
        ids = new List<ElementId>();
        using (Transaction transaction = new(document, "Replacement test instances"))
        {
            transaction.Start();
            if (!sourceType!.IsActive) sourceType.Activate();
            for (int index = 0; index < (scenario == 1 ? 2 : 1); index++)
            {
                FamilyInstance source = document.Create.NewFamilyInstance(new XYZ(3 + 10 * scenario + 3 * index, 1, level!.Elevation + 1), sourceType, level, StructuralType.NonStructural);
                source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set($"T{scenario}-{index}");
                source.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Single/multiple selection regression");
                ids.Add(source.Id);
            }
            transaction.Commit();
        }
        uiDocument.ShowElements(ids);
        uiDocument.Selection.SetElementIds(scenario == 2 ? Array.Empty<ElementId>() : ids);
        uiDocument.RefreshActiveView();
    }

    private void RunScenario()
    {
        Document document = uiDocument!.Document;
        long[] sourceIds = ids.Select(id => (long)id.IntegerValue).ToArray();
        object[] dependents = ids.SelectMany(id => document.GetElement(id).GetDependentElements(null))
            .Distinct().Select(id => Describe(document, id)).ToArray();
        FamilyReplacementResult result = scenario == 3
            ? FamilyReplacementSelectionRunner.Replace(uiDocument, sourceIds, targetType!.Id.IntegerValue,
                FamilyReplacementAlignment.GeometryCenter, null, exception => throw exception)
            : new FamilyReplacementService().Replace(document, sourceIds, targetType!.Id.IntegerValue, FamilyReplacementAlignment.GeometryCenter);
        // Inspect rolled-back failures only after testing the original selection state.
        List<object> deletedDetails = new();
        if (result.Skipped > 0)
        {
            using Transaction probe = new(document, "Replacement deletion probe");
            probe.Start();
            ElementId[] removed = document.Delete(ids.Where(id => document.GetElement(id) is not null).ToList()).ToArray();
            probe.RollBack();
            deletedDetails.AddRange(removed.Select(id => Describe(document, id)));
        }
        results.Add(new
        {
            Scenario = scenario == 0 ? "SingleSelected" : scenario == 1 ? "MultipleSelected" : scenario == 2 ? "SingleUnselected" : "SingleSelectionFixed",
            result.Total, result.Replaced, result.Skipped, result.Items, Dependents = dependents, DeletedByProbe = deletedDetails
        });
    }

    private static object Describe(Document document, ElementId id)
    {
        Element? element = document.GetElement(id);
        return new { Id = id.IntegerValue, Type = element?.GetType().FullName, Category = element?.Category?.Name,
            Transient = element?.IsTransient, OwnerViewId = element?.OwnerViewId.IntegerValue };
    }

    private void WriteReport(string? error)
    {
        File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = error, Scenarios = results }));
    }
}
