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
    private CommitMovementUpdater? movementUpdater;
    private static readonly string[] ScenarioNames =
    {
        "SingleSelected", "MultipleSelected", "SingleUnselected", "SingleSelectionFixed",
        "RotatedInsertion", "MirroredCenter", "OverlapStrict", "OverlapIgnored", "CommitMovementRollback"
    };

    public Result OnStartup(UIControlledApplication application)
    {
        reportPath = Environment.GetEnvironmentVariable("TRUEBIM_REPLACEMENT_HARNESS_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            File.WriteAllText(reportPath + ".progress.txt", "Harness loaded; waiting for Idling.");
            application.Idling += OnIdling;
            movementUpdater = new CommitMovementUpdater(application.ActiveAddInId);
            UpdaterRegistry.RegisterUpdater(movementUpdater, true);
            UpdaterRegistry.AddTrigger(movementUpdater.GetUpdaterId(),
                new ElementCategoryFilter(BuiltInCategory.OST_NurseCallDevices), Element.GetChangeTypeAny());
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdling;
        if (movementUpdater is not null)
            UpdaterRegistry.UnregisterUpdater(movementUpdater.GetUpdaterId());
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
                phase = scenario < ScenarioNames.Length ? 0 : -1;
                if (phase < 0)
                {
                    TestFailurePolicy();
                    WriteReport(null);
                }
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
        // Asymmetric geometry and different origins expose rotation/anchor regressions.
        double offset = typeName == "Target" ? 1.5 : 0;
        XYZ[] points = { new(offset - 0.5, -0.25, 0), new(offset + 1, -0.25, 0),
            new(offset + 1, 0.5, 0), new(offset - 0.5, 0.5, 0) };
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
            for (int index = 0; index < (scenario == 1 ? 2 : scenario is 4 or 5 ? 3 : 1); index++)
            {
                FamilyInstance source = document.Create.NewFamilyInstance(new XYZ(3 + 10 * scenario + 3 * index, 1, level!.Elevation + 1), sourceType, level, StructuralType.NonStructural);
                source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set($"T{scenario}-{index}");
                source.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Single/multiple selection regression");
                if (scenario is 4 or 5)
                {
                    XYZ point = ((LocationPoint)source.Location).Point;
                    ElementTransformUtils.RotateElement(document, source.Id, Line.CreateUnbound(point, XYZ.BasisZ), 0.37 + index * 1.71);
                    if (scenario == 5)
                        ElementTransformUtils.MirrorElements(document, new[] { source.Id }, Plane.CreateByNormalAndOrigin(XYZ.BasisX, point), false);
                }
                if (scenario is 6 or 7)
                {
                    if (!targetType!.IsActive) targetType.Activate();
                    document.Create.NewFamilyInstance(((LocationPoint)source.Location).Point, targetType, level, StructuralType.NonStructural);
                }
                if (scenario == 8) source.Pinned = true;
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
        FamilyReplacementAlignment alignment = scenario is 4 or 6 or 7 or 8
            ? FamilyReplacementAlignment.InsertionPoint : FamilyReplacementAlignment.GeometryCenter;
        FamilyReplacementPlacementService placement = new();
        FamilyReplacementPlacementSnapshot[] poses = ids.Select(id => placement.Capture(document, (FamilyInstance)document.GetElement(id), alignment)).ToArray();
        movementUpdater!.Enabled = scenario == 8;
        FamilyReplacementResult result = scenario >= 3
            ? FamilyReplacementSelectionRunner.Replace(uiDocument, sourceIds, targetType!.Id.IntegerValue,
                alignment, null, exception => throw exception, ignoreIntersectionWarnings: scenario == 7)
            : new FamilyReplacementService().Replace(document, sourceIds, targetType!.Id.IntegerValue, alignment);
        movementUpdater.Enabled = false;
        for (int index = 0; index < sourceIds.Length; index++)
        {
            FamilyReplacementItemResult item = result.Items.Single(row => row.SourceId == sourceIds[index]);
            FamilyInstance remaining = (FamilyInstance)document.GetElement(new ElementId((int)(item.NewId ?? item.SourceId)));
            placement.Validate(document, poses[index], remaining);
            if (scenario == 8 && (!remaining.Pinned || !movementUpdater.Moved))
                throw new InvalidOperationException("Post-commit movement was not exercised or pinned source was not restored.");
        }
        // Inspect rolled-back failures only after testing the original selection state.
        List<object> deletedDetails = new();
        if (scenario == 0 && result.Skipped > 0)
        {
            using Transaction probe = new(document, "Replacement deletion probe");
            probe.Start();
            ElementId[] removed = document.Delete(ids.Where(id => document.GetElement(id) is not null).ToList()).ToArray();
            probe.RollBack();
            deletedDetails.AddRange(removed.Select(id => Describe(document, id)));
        }
        results.Add(new
        {
            Scenario = ScenarioNames[scenario],
            result.Total, result.Replaced, result.Skipped, result.Items, Dependents = dependents, DeletedByProbe = deletedDetails
        });
    }

    private void TestFailurePolicy()
    {
        Document document = uiDocument!.Document;
        foreach (bool ignore in new[] { false, true })
        {
            using Transaction transaction = new(document, "Failure policy regression");
            transaction.Start();
            FamilyReplacementFailureHandler handler = new(ignore);
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(handler).SetClearAfterRollback(true));
            FailureMessage overlap = new(BuiltInFailures.OverlapFailures.DuplicateInstances);
            overlap.SetFailingElement(ids[0]);
            document.PostFailure(overlap);
            FailureMessage unrelated = new(BuiltInFailures.InaccurateFailures.InaccurateLine);
            unrelated.SetFailingElement(ids[0]);
            document.PostFailure(unrelated);
            if (transaction.Commit() != TransactionStatus.RolledBack)
                throw new InvalidOperationException("Unrelated warning incorrectly ignored.");
            results.Add(new { Scenario = ignore ? "MixedWarningsIgnored" : "MixedWarningsStrict", Total = 1, Replaced = 0, Skipped = 1 });
        }
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
