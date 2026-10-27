using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;

namespace TrueBIM.Revit.RebarSplitHarness;

public sealed class RebarSplitHarnessApplication : IExternalApplication
{
    private static readonly Guid ParameterProbeId = new("14f24c08-785a-4c33-8521-7a65dd5f26df");
    private string? reportPath;
    private bool finished;
    private readonly List<object> results = new();
    private ParameterUpdater? updater;
    private ElementId? tagTypeId;
    private UIDocument? uiDocument;
    private RebarSplitModelAudit? modelAudit;

    public Result OnStartup(UIControlledApplication application)
    {
        reportPath = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_HARNESS_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            application.DialogBoxShowing += CloseViewerNotice;
            File.WriteAllText(reportPath + ".progress.txt", "Harness loaded; waiting for Idling.");
            updater = new ParameterUpdater(application.ActiveAddInId);
            UpdaterRegistry.RegisterUpdater(updater, true);
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementCategoryFilter(BuiltInCategory.OST_Rebar), Element.GetChangeTypeElementAddition());
            UpdaterRegistry.AddTrigger(updater.GetUpdaterId(), new ElementCategoryFilter(BuiltInCategory.OST_Rebar), Element.GetChangeTypeAny());
            string? modelPath = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_HARNESS_MODEL");
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                modelAudit = new RebarSplitModelAudit(modelPath, reportPath);
                modelAudit.Register(application);
                application.Idling += Run;
                return Result.Succeeded;
            }
            application.Idling += Run;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.DialogBoxShowing -= CloseViewerNotice;
        application.Idling -= Run;
        if (updater is not null) UpdaterRegistry.UnregisterUpdater(updater.GetUpdaterId());
        return Result.Succeeded;
    }

    private static void CloseViewerNotice(object? sender, DialogBoxShowingEventArgs args)
    {
        if (args is TaskDialogShowingEventArgs dialog && dialog.DialogId == "TaskDialog_License_Current_Status_Demo_Mode")
            dialog.OverrideResult((int)TaskDialogResult.Close);
    }

    private void Run(object? sender, IdlingEventArgs args)
    {
        if (finished || sender is not UIApplication application) return;
        if (modelAudit is not null)
        {
            finished = modelAudit.Tick(application);
            if (!finished) args.SetRaiseWithoutDelay();
            return;
        }
        string? fatal = null;
        try
        {
            if (uiDocument is null)
            {
                uiDocument = application.OpenAndActivateDocument(@"C:\ProgramData\Autodesk\RVT 2022\Templates\English\DefaultMetric.rte");
                args.SetRaiseWithoutDelay();
                return;
            }
            Document document = uiDocument.Document;
            ViewPlan plan = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(view => !view.IsTemplate && view.GenLevel is not null).OrderBy(view => view.GenLevel.Elevation).First();
            if (uiDocument.ActiveView.Id != plan.Id)
            {
                uiDocument.RequestViewChange(plan);
                args.SetRaiseWithoutDelay();
                return;
            }
            finished = true;
            if (Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO") == "SharedGroupsProbe")
            {
                results.AddRange(RebarSplitProtectionAudit.Run(document, "Grouped"));
                CreateTagFamily(application, document);
                foreach (string scenario in new[] { "GroupedTagOn", "GroupedTagOff", "RepeatedGroupedTagOn", "RepeatedGroupedTagOff" })
                {
                    try { Test(document, scenario); }
                    catch (Exception exception) { results.Add(new { Scenario = scenario, Passed = false, Error = exception.ToString() }); }
                }
                File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = fatal, Scenarios = results }));
                return;
            }
            if (Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO") == "GroupExtraction")
            {
                results.AddRange(RebarSplitExtractionAudit.Run(document));
                File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = fatal, Scenarios = results }));
                return;
            }
            results.AddRange(RebarSplitProtectionAudit.Run(document));
            if (Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_ONLY") == "1")
            {
                foreach (string protection in string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO"))
                    || Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_PROTECTION_SCENARIO") == "ParameterIssues"
                    ? new[] { "", "Pinned", "Grouped", "RepeatedGrouped" } : Array.Empty<string>())
                foreach (string action in new[] { "CancelAll", "SkipProblem", "ForceProblem" })
                {
                    string scenario = protection + action;
                    try { Test(document, scenario); }
                    catch (Exception exception) { results.Add(new { Scenario = scenario, Passed = false, Error = exception.ToString() }); }
                }
                File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = fatal, Scenarios = results }));
                return;
            }
            CreateTagFamily(application, document);
            foreach (string scenario in new[] { "Uniform", "Bent", "Hooks", "ExcludedMoved", "Varying", "TagOn", "TagOff", "MultiAnnotation", "LinearDimension", "CancelAll", "SkipProblem", "ForceProblem" })
            {
                File.AppendAllText(reportPath + ".progress.txt", "\n" + scenario);
                try { Test(document, scenario); }
                catch (Exception exception) { results.Add(new { Scenario = scenario, Passed = false, Error = exception.ToString() }); }
            }
            if (Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_COMPREHENSIVE") == "1")
            {
                foreach (string prefix in new[] { "Grouped", "RepeatedGrouped" })
                foreach (string feature in new[] { "TagOn", "TagOff", "MultiAnnotation", "LinearDimension", "Varying", "ExcludedMoved", "Hooks", "Bent" })
                    RunFeature(prefix + feature);
                RunFeature("RepeatedGroupedHostDimension");
                foreach (string prefix in new[] { "Pinned", "Grouped", "RepeatedGrouped" })
                foreach (string feature in new[] { "CancelAll", "SkipProblem", "ForceProblem" })
                    RunFeature(prefix + feature);
                results.AddRange(RebarSplitExtractionAudit.Run(document));
            }
            void RunFeature(string scenario)
            {
                File.AppendAllText(reportPath + ".progress.txt", "\n" + scenario);
                try { Test(document, scenario); }
                catch (Exception exception) { results.Add(new { Scenario = scenario, Passed = false, Error = exception.ToString() }); }
            }
            // Активный временный документ закрывается вместе с принадлежащим скрипту процессом, без сохранения.
        }
        catch (Exception exception) { finished = true; fatal = exception.ToString(); }
        File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = fatal, Scenarios = results }));
    }

    private void Test(Document document, string scenario)
    {
        using RebarSplitNativeFailureCapture native = new(document, allowWarnings: true);
        string reportedScenario = scenario;
        bool repeatedGrouped = scenario.StartsWith("RepeatedGrouped", StringComparison.Ordinal);
        bool pinned = scenario.StartsWith("Pinned", StringComparison.Ordinal);
        bool grouped = repeatedGrouped || scenario.StartsWith("Grouped", StringComparison.Ordinal);
        if (pinned) scenario = scenario.Substring("Pinned".Length);
        if (grouped) scenario = scenario.Substring((repeatedGrouped ? "RepeatedGrouped" : "Grouped").Length);
        using TransactionGroup fixture = new(document, "Rebar split test fixture");
        fixture.Start();
        List<ElementId> sourceIds = new();
        List<ElementId> additionalSources = new();
        Dictionary<ElementId, List<IList<Curve>>> geometries = new();
        bool parameterTest = scenario is "CancelAll" or "SkipProblem" or "ForceProblem";
        bool varying = scenario == "Varying";
        ElementId? annotationId = null;
        using (Transaction transaction = new(document, "Create test rebars"))
        {
            transaction.Start();
            if (parameterTest) CreateParameterProbe(document);
            Level level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>().OrderBy(item => item.Elevation).First();
            WallType wallType = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>().First(item => item.Kind == WallKind.Basic);
            wallType.SetCompoundStructure(CompoundStructure.CreateSimpleCompoundStructure(new[]
                { new CompoundStructureLayer(3, MaterialFunctionAssignment.Structure, ElementId.InvalidElementId) }));
            XYZ[] profilePoints = { new(0, 0, level.Elevation), new(40, 0, level.Elevation),
                new(40, 0, level.Elevation + 14), new(0, 0, level.Elevation + 6) };
            Wall host = varying ? Wall.Create(document, Enumerable.Range(0, 4).Select(index => (Curve)Line.CreateBound(profilePoints[index], profilePoints[(index + 1) % 4])).ToList(), wallType.Id, level.Id, true)
                : Wall.Create(document, Line.CreateBound(new XYZ(0, 0, level.Elevation), new XYZ(40, 0, level.Elevation)), wallType.Id, level.Id, 12, 0, false, true);
            RebarBarType barType = RebarBarType.Create(document);
            barType.BarModelDiameter = 12.0 / 304.8;
            barType.BarNominalDiameter = 12.0 / 304.8;
            RebarHookType? hook = scenario == "Hooks" ? RebarHookType.Create(document, Math.PI / 2, 10) : null;
            document.Regenerate();
            for (int i = 0; i < (parameterTest ? 2 : 1); i++)
            {
                double start = 2 + i * 12;
                XYZ a = new(start, 0, level.Elevation + (varying ? 0.2 : 3));
                XYZ b = varying ? new(start, 0, level.Elevation + 6.2) : new(start + 6, 0, level.Elevation + 3);
                List<Curve> curves = new() { Line.CreateBound(a, b) };
                if (scenario == "Bent") curves.Add(Line.CreateBound(b, b + new XYZ(0, 0, 2)));
                Rebar source = Rebar.CreateFromCurves(document, RebarStyle.Standard, barType, hook, hook, host,
                    varying ? XYZ.BasisX : XYZ.BasisY, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                source.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(4, varying ? 20 : 0.4, true, scenario != "ExcludedMoved", true);
                if (varying)
                {
                    source.DistributionType = DistributionType.VaryingLength;
                    document.Regenerate();
                    RebarConstraintsManager manager = source.GetRebarConstraintsManager();
                    RebarConstrainedHandle end = manager.GetAllHandles().First(handle => handle.GetHandleType() == RebarHandleType.EndOfBar);
                    RebarConstraint constraint = manager.GetConstraintCandidatesForHandle(end).First(candidate => candidate.IsToHostFaceOrCover()
                        && host.GetGeometryObjectFromReference(candidate.GetTargetHostFaceReference()) is PlanarFace face
                        && face.FaceNormal.Z > 0.5 && Math.Abs(face.FaceNormal.X) > 0.01);
                    manager.SetPreferredConstraintForHandle(end, constraint);
                    document.Regenerate();
                }
                source.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(parameterTest && i == 0 ? "PROBLEM" : "OK");
                source.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Inherited — комментарий");
                if (parameterTest) source.get_Parameter(ParameterProbeId).Set("Inherited probe value");
                source.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.Set("Test partition");
                if (scenario == "ExcludedMoved") source.MoveBarInSet(2, Transform.CreateTranslation(new XYZ(0.2, 0.05, 0.1)));
                sourceIds.Add(source.Id);
                if (scenario is "TagOn" or "TagOff" or "MultiAnnotation" or "LinearDimension" or "HostDimension")
                {
                    ViewPlan view = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                        .First(item => !item.IsTemplate && item.GenLevel?.Id == level.Id);
                    if (scenario == "HostDimension")
                    {
                        document.Regenerate();
                        List<PlanarFace> faces = host.get_Geometry(new Options { ComputeReferences = true, View = view })
                            .OfType<Solid>().SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
                            .Where(face => Math.Abs(face.FaceNormal.X) > 0.99).OrderBy(face => face.Origin.X).ToList();
                        Assert(faces.Count >= 2, "Fixture has no host end-face references.");
                        ReferenceArray references = new();
                        references.Append(faces.First().Reference);
                        references.Append(faces.Last().Reference);
                        Dimension dimension = document.Create.NewDimension(view,
                            Line.CreateBound(new XYZ(0, 4, level.Elevation), new XYZ(40, 4, level.Elevation)), references);
                        dimension.Above = "Host dimension";
                        annotationId = dimension.Id;
                    }
                    else if (scenario == "LinearDimension")
                    {
                        view.DetailLevel = ViewDetailLevel.Fine;
                        source.SetUnobscuredInView(view, true);
                        document.Regenerate();
                        GeometryElement geometry = source.get_Geometry(new Options { View = view, ComputeReferences = true, IncludeNonVisibleObjects = true });
                        List<Line> lines = geometry.OfType<Line>()
                            .Where(line => line.Reference is not null && Math.Abs(line.Direction.X) > 0.99)
                            .OrderBy(line => line.GetEndPoint(0).Y).ToList();
                        Assert(lines.Count >= 2, "Fixture has no parallel rebar references. Geometry: " + string.Join(", ", geometry.Select(item => item.GetType().Name)));
                        ReferenceArray dimensionReferences = new();
                        dimensionReferences.Append(lines.First().Reference);
                        dimensionReferences.Append(lines.Last().Reference);
                        annotationId = document.Create.NewDimension(view, Line.CreateBound(new XYZ(start + 3, -1, level.Elevation), new XYZ(start + 3, 2, level.Elevation)), dimensionReferences).Id;
                    }
                    else if (scenario == "MultiAnnotation")
                    {
                        MultiReferenceAnnotationType type = new FilteredElementCollector(document).OfClass(typeof(MultiReferenceAnnotationType))
                            .Cast<MultiReferenceAnnotationType>().FirstOrDefault() ?? MultiReferenceAnnotationType.CreateDefault(document);
                        type.TagTypeId = tagTypeId;
                        MultiReferenceAnnotationOptions options = new(type)
                        {
                            DimensionLineOrigin = new XYZ(start + 2, 0, level.Elevation + 3),
                            DimensionLineDirection = XYZ.BasisY,
                            DimensionPlaneNormal = view.ViewDirection,
                            TagHeadPosition = new XYZ(start + 4, 3, level.Elevation + 3),
                            TagHasLeader = false
                        };
                        options.SetElementsToDimension(new[] { source.Id });
                        annotationId = MultiReferenceAnnotation.Create(document, view.Id, options).Id;
                    }
                    else annotationId = IndependentTag.Create(document, tagTypeId, view.Id, new Reference(source), false, TagOrientation.Horizontal,
                        new XYZ(start + 4, 3, level.Elevation + 3)).Id;
                }
            }
            if (pinned) document.GetElement(sourceIds[0]).Pinned = true;
            if (grouped)
            {
                document.Regenerate();
                Group group = document.Create.NewGroup(repeatedGrouped ? new[] { host.Id, sourceIds[0] } : new[] { sourceIds[0] });
                if (repeatedGrouped)
                {
                    document.Regenerate();
                    Group copy = document.Create.PlaceGroup(new XYZ(0, 30, level.Elevation), group.GroupType);
                    additionalSources.AddRange(copy.GetMemberIds().Where(id => document.GetElement(id) is Rebar));
                }
            }
            transaction.Commit();
        }
        List<ElementId> expandedSourceIds = sourceIds.Concat(additionalSources).ToList();
        int originalTagCount = new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).GetElementCount();
        double? expectedDimensionValue = annotationId is not null && document.GetElement(annotationId) is Dimension initialDimension
            ? initialDimension.Value : null;
        foreach (ElementId id in expandedSourceIds)
        {
            Rebar source = (Rebar)document.GetElement(id);
            geometries[id] = RebarSplitGeometry.ExistingPositions(source).Select(index => RebarSplitGeometry.Curves(source, index)).ToList();
        }
        RebarSplitService service = new(text => File.AppendAllText(reportPath + ".diagnostics.txt", scenario + ": " + text + "\n"));
        Assert(service.FindCandidates(document, sourceIds, RebarSplitScope.Selection).Count == sourceIds.Count, "Selection collection failed.");
        Assert(service.FindCandidates(document, sourceIds, RebarSplitScope.VaryingSetsInProject).Count == (varying ? expandedSourceIds.Count : 0), "Actual-length collection failed.");
        int callbackCount = 0;
        updater!.Enabled = parameterTest;
        updater.GeometryFilter = repeatedGrouped ? geometries[sourceIds[0]] : null;
        RebarSplitResult result;
        try
        {
            result = service.Execute(document, sourceIds, scenario is "TagOn" or "MultiAnnotation" or "LinearDimension" or "HostDimension", issues =>
            {
                callbackCount++;
                Assert(issues.Count == 1 && issues[0].SourceId == sourceIds[0].IntegerValue, "Issue IDs do not identify problem source.");
                return scenario switch
                {
                    "CancelAll" => RebarSplitParameterAction.Cancel,
                    "SkipProblem" => RebarSplitParameterAction.Skip,
                    "ForceProblem" => RebarSplitParameterAction.SplitAnyway,
                    _ => throw new InvalidOperationException("Unexpected parameter issues: " + string.Join("; ", issues.SelectMany(item => item.Parameters)))
                };
            }, _ => RebarSplitProtectionAction.PreserveStatus, _ => RebarSplitGroupAction.Extract);
        }
        finally { updater.Enabled = false; }
        Assert(callbackCount == (parameterTest ? 1 : 0), $"Parameter dialog count differs: {callbackCount}, cancelled={result.Cancelled}. "
            + string.Join("; ", native.Failures.Select(failure => failure.Description)));
        if (scenario == "CancelAll")
        {
            Assert(result.Cancelled, "Cancellation not reported.");
            Assert(expandedSourceIds.All(id => document.GetElement(id) is Rebar), "Cancel did not restore all sources.");
            Assert(new FilteredElementCollector(document).OfClass(typeof(Rebar)).GetElementCount() == expandedSourceIds.Count, "Cancel left new bars.");
        }
        else
        {
            int expectedSets = scenario == "SkipProblem" ? 1 : expandedSourceIds.Count;
            int expectedBars = (scenario == "ExcludedMoved" ? 3 : 4) * expectedSets;
            Assert(result.SetCount == expectedSets && result.CreatedIds.Count == expectedBars, "Wrong result count.");
            Assert(expandedSourceIds.All(id => (document.GetElement(id) is not null)
                == (scenario == "SkipProblem" && (id == sourceIds[0] || additionalSources.Contains(id)))), "Original deletion/skip mismatch.");
            Assert(result.SkippedCount == (scenario == "SkipProblem" ? 1 + additionalSources.Count : 0), "Skipped group counterparts differ.");
            List<IList<Curve>> expected = expandedSourceIds.Where(id => scenario != "SkipProblem"
                || (id != sourceIds[0] && !additionalSources.Contains(id))).SelectMany(id => geometries[id]).ToList();
            foreach (ElementId id in result.CreatedIds)
            {
                Rebar bar = (Rebar)document.GetElement(id);
                Assert(bar.Quantity == 1 && bar.LayoutRule == RebarLayoutRule.Single, "New element is not a single rebar.");
                int index = expected.FindIndex(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0)));
                Assert(index >= 0, "New bar has unexpected geometry.");
                expected.RemoveAt(index);
                string comments = bar.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString();
                Assert(comments == "Inherited — комментарий", "Instance comment was not retained.");
                if (parameterTest)
                    Assert(bar.get_Parameter(ParameterProbeId).AsString() == (scenario == "ForceProblem"
                    && bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() == "PROBLEM"
                    && (updater.GeometryFilter is null || updater.GeometryFilter.Any(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0))))
                    ? "Updater changed value" : "Inherited probe value"), "Instance probe value differs.");
            }
            Assert(expected.Count == 0, "Missing geometry.");
            if (scenario == "TagOn")
            {
                List<IndependentTag> tags = new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
                Assert(tags.Count == 1 && !tags[0].IsOrphaned && tags[0].Id != annotationId, $"Tag was not recreated (old {annotationId}). "
                    + string.Join("; ", tags.Select(tag => $"ID {tag.Id}, orphan {tag.IsOrphaned}, group {tag.GroupId}, refs "
                        + string.Join(",", tag.GetTaggedReferences().Select(reference => reference.ElementId)))));
                Assert(tags[0].GetTaggedReferences().All(reference => result.CreatedIds.Contains(reference.ElementId)), "Tag points outside replacement bars.");
            }
            if (scenario == "TagOff")
            {
                List<IndependentTag> tags = new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
                // При смене типа Revit сам переносит марки. Выключенная опция не требует их удаления.
                Assert(grouped ? tags.Count <= originalTagCount : !tags.Any(tag => !tag.IsOrphaned
                    && tag.GetTaggedReferences().Any(reference => result.CreatedIds.Contains(reference.ElementId))),
                    $"Disabled option recreated a tag (old {annotationId}): " + string.Join("; ", tags.Select(tag => $"{tag.Id}: "
                        + string.Join(",", tag.GetTaggedReferences().Select(reference => reference.ElementId)))));
            }
            if (scenario == "MultiAnnotation")
            {
                List<MultiReferenceAnnotation> annotations = new FilteredElementCollector(document).OfClass(typeof(MultiReferenceAnnotation)).Cast<MultiReferenceAnnotation>().ToList();
                Assert(annotations.Count == 1 && annotations[0].Id != annotationId, "Multi-reference annotation was not recreated.");
                Dimension dimension = (Dimension)document.GetElement(annotations[0].DimensionId);
                Assert(dimension.AreReferencesAvailable, "Multi-reference dimension has invalid references.");
            }
            if (scenario is "LinearDimension" or "HostDimension")
            {
                List<Dimension> dimensions = new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>().ToList();
                Assert(dimensions.Count == 1 && dimensions[0].Id != annotationId && dimensions[0].AreReferencesAvailable, "Linear dimension was not recreated.");
                Assert(expectedDimensionValue.HasValue && Math.Abs(dimensions[0].Value!.Value - expectedDimensionValue.Value) < 1e-6,
                    $"Dimension measurement changed: expected {expectedDimensionValue}, actual {dimensions[0].Value}.");
                if (scenario == "HostDimension")
                    Assert(dimensions[0].Above == "Host dimension", "Host dimension formatting changed.");
            }
        }
        Assert(native.Failures.All(failure => failure.Severity == "Warning"), "Native error during parameter regression.");
        results.Add(new { Scenario = reportedScenario, Passed = true, result.SetCount, Bars = result.CreatedIds.Count, callbackCount, result.Warnings, NativeFailures = native.Failures });
        fixture.RollBack();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private void CreateTagFamily(UIApplication application, Document document)
    {
        using Document family = application.Application.NewFamilyDocument(@"C:\ProgramData\Autodesk\RVT 2022\Family Templates\English\Annotations\Metric Generic Tag.rft");
        using (Transaction transaction = new(family, "Rebar tag test family"))
        {
            transaction.Start();
            family.OwnerFamily.FamilyCategory = family.Settings.Categories.get_Item(BuiltInCategory.OST_RebarTags);
            family.FamilyManager.NewType("Rebar split test tag");
            transaction.Commit();
        }
        Family loaded = family.LoadFamily(document);
        tagTypeId = loaded.GetFamilySymbolIds().First();
        family.Close(false);
    }

    private void CreateParameterProbe(Document document)
    {
        Autodesk.Revit.ApplicationServices.Application application = document.Application;
        string previous = application.SharedParametersFilename;
        string path = reportPath + ".sharedparameters.txt";
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            application.SharedParametersFilename = path;
            DefinitionFile file = application.OpenSharedParameterFile();
            DefinitionGroup definitions = file.Groups.get_Item("TrueBIMHarness") ?? file.Groups.Create("TrueBIMHarness");
            ExternalDefinition definition = (ExternalDefinition)(definitions.Definitions.get_Item("TrueBIM parameter transfer probe")
                ?? definitions.Definitions.Create(new ExternalDefinitionCreationOptions("TrueBIM parameter transfer probe", SpecTypeId.String.Text)
                    { GUID = ParameterProbeId }));
            CategorySet categories = application.Create.NewCategorySet();
            categories.Insert(document.Settings.Categories.get_Item(BuiltInCategory.OST_Rebar));
            Assert(document.ParameterBindings.Insert(definition, application.Create.NewInstanceBinding(categories), BuiltInParameterGroup.PG_DATA),
                "Parameter probe binding failed.");
            document.Regenerate();
            // Потеря значения в одном экземпляре должна быть допустима для Revit,
            // чтобы тест дошёл до диалога параметров, а не до ошибки изменения группы.
            SharedParameterElement.Lookup(document, ParameterProbeId).GetDefinition().SetAllowVaryBetweenGroups(document, true);
        }
        finally { application.SharedParametersFilename = previous; }
    }

    private sealed class ParameterUpdater : IUpdater
    {
        private readonly UpdaterId id;
        public ParameterUpdater(AddInId addInId) => id = new(addInId, new Guid("62CB234D-0015-4B73-BA35-2A00B7086CAD"));
        public bool Enabled { get; set; }
        public IReadOnlyList<IList<Curve>>? GeometryFilter { get; set; }
        public void Execute(UpdaterData data)
        {
            if (!Enabled) return;
            foreach (ElementId added in data.GetAddedElementIds().Concat(data.GetModifiedElementIds()).Distinct())
                if (data.GetDocument().GetElement(added) is Rebar bar && bar.LayoutRule == RebarLayoutRule.Single
                    && (GeometryFilter is null || GeometryFilter.Any(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0))))
                    && bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).AsString() == "PROBLEM")
                    bar.get_Parameter(ParameterProbeId).Set("Updater changed value");
        }
        public string GetAdditionalInformation() => "Isolated parameter transfer regression";
        public ChangePriority GetChangePriority() => ChangePriority.Rebar;
        public UpdaterId GetUpdaterId() => id;
        public string GetUpdaterName() => "TrueBIM rebar split regression";
    }
}
