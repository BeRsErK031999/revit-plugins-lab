using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using TrueBIM.App.Commands;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Modules.RebarSplit.Revit;

namespace TrueBIM.Revit.RebarSplitHarness;

internal sealed class RebarSplitModelAudit
{
    private readonly string modelPath;
    private readonly string reportPath;
    private UIDocument? uiDocument;
    private int stage;
    private DateTime stageStarted;
    private List<ElementId> sets = new();
    private readonly List<object> scenarios = new();
    private ElementId? annotationViewId;

    public RebarSplitModelAudit(string modelPath, string reportPath)
    {
        this.modelPath = modelPath;
        this.reportPath = reportPath;
        ModelAuditAvailability.ModelPath = modelPath;
    }

    public void Register(UIControlledApplication application)
    {
        RibbonPanel panel = application.CreateRibbonPanel("TrueBIM — проверка арматуры");
        PushButtonData data = new("TrueBIM_RebarSplit_AvailabilityProbe", "Проверка\nарматуры",
            typeof(ModelAuditCommand).Assembly.Location, typeof(ModelAuditCommand).FullName!)
        {
            AvailabilityClassName = typeof(ModelAuditAvailability).FullName
        };
        panel.AddItem(data);
    }

    public bool Tick(UIApplication application)
    {
        try
        {
            if (uiDocument is null)
            {
                uiDocument = application.OpenAndActivateDocument(modelPath);
                Document document = uiDocument.Document;
                sets = new FilteredElementCollector(document).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Where(RebarSplitGeometry.IsSupportedSet).Select(bar => bar.Id).ToList();
                uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
                stageStarted = DateTime.UtcNow;
                Progress($"Model opened. Supported sets: {sets.Count}. Awaiting availability with no selection.");
                return false;
            }
            bool checkAvailability = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_SPLIT_SKIP_AVAILABILITY") != "1";
            if (checkAvailability && stage == 0)
            {
                if (!ModelAuditAvailability.Observations.Any(item => item.SelectedCount == 0))
                {
                    RequireCallback();
                    return false;
                }
                if (sets.Count == 0) throw new InvalidOperationException("The customer model contains no supported rebar sets.");
                uiDocument.Selection.SetElementIds(new[] { sets[0] });
                stage = 1;
                stageStarted = DateTime.UtcNow;
                Progress("Awaiting availability with a selected rebar set.");
                return false;
            }
            if (checkAvailability && !ModelAuditAvailability.Observations.Any(item => item.SelectedCount > 0))
            {
                RequireCallback();
                return false;
            }

            Document model = uiDocument.Document;
            if (annotationViewId is not null && uiDocument.ActiveView.Id != annotationViewId)
            {
                RequireCallback();
                return false;
            }
            uiDocument.Selection.SetElementIds(Array.Empty<ElementId>());
            InspectDimensionReferences(model);
            var inventory = new FilteredElementCollector(model).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Select(bar => new
                {
                    Id = bar.Id.IntegerValue, bar.Quantity, Layout = bar.LayoutRule.ToString(),
                    bar.Pinned, GroupId = bar.GroupId.IntegerValue, HostId = bar.GetHostId().IntegerValue,
                    Distribution = bar.DistributionType.ToString(), Supported = RebarSplitGeometry.IsSupportedSet(bar)
                }).ToList();
            var groups = new FilteredElementCollector(model).OfClass(typeof(Group)).Cast<Group>()
                .Where(group => group.GetMemberIds().Any(id => sets.Contains(id))).Select(group => new
                {
                    Id = group.Id.IntegerValue, TypeId = group.GetTypeId().IntegerValue, Name = group.GroupType.Name,
                    Point = (group.Location as LocationPoint)?.Point.ToString(),
                    Members = group.GetMemberIds().Select(model.GetElement).Select(member => new
                    {
                        Id = member.Id.IntegerValue, Type = member.GetType().Name, GroupId = member.GroupId.IntegerValue,
                        HostId = member is Rebar rebar ? rebar.GetHostId().IntegerValue : -1,
                        Min = member.get_BoundingBox(null)?.Min.ToString(), Max = member.get_BoundingBox(null)?.Max.ToString()
                    }).ToList()
                }).ToList();
            File.WriteAllText(reportPath + ".inventory.json", new JavaScriptSerializer().Serialize(new { Rebars = inventory, Groups = groups }));
            foreach (RebarSplitScope scope in new[] { RebarSplitScope.Selection, RebarSplitScope.VaryingSetsInProject })
            {
                foreach (bool preserveAnnotations in new[] { true, false })
                foreach (RebarSplitProtectionAction action in Enum.GetValues(typeof(RebarSplitProtectionAction)))
                {
                    Progress($"Testing {scope}; action={action}; preserve annotations={preserveAnnotations}.");
                    scenarios.Add(Test(model, scope, preserveAnnotations, action: action));
                    File.WriteAllText(reportPath + ".cases.json", new JavaScriptSerializer().Serialize(scenarios));
                }
            }
            foreach (bool preserveAnnotations in new[] { true, false })
            {
                Progress($"Testing unprotected selection; preserve annotations={preserveAnnotations}.");
                scenarios.Add(Test(model, RebarSplitScope.Selection, preserveAnnotations, unprotectedOnly: true));
                File.WriteAllText(reportPath + ".cases.json", new JavaScriptSerializer().Serialize(scenarios));
            }
            if (annotationViewId is null)
            {
                annotationViewId = new FilteredElementCollector(model).OfClass(typeof(Dimension)).Cast<Dimension>()
                    .FirstOrDefault(dimension => dimension.References.Cast<Reference>().Any(reference => sets.Contains(reference.ElementId)))
                    ?.OwnerViewId ?? uiDocument.ActiveView.Id;
                uiDocument.RequestViewChange((View)model.GetElement(annotationViewId));
                stageStarted = DateTime.UtcNow;
                Progress("Requesting annotation owner view for reference-resolution verification.");
                return false;
            }
            WriteReport(new
            {
                FatalError = (string?)null, Model = Path.GetFileName(modelPath),
                Availability = ModelAuditAvailability.Observations, Inventory = inventory, Scenarios = scenarios,
                OriginalWarnings = model.GetWarnings().Select(warning => new { Description = warning.GetDescriptionText(),
                    Elements = warning.GetFailingElements().Select(id => id.IntegerValue).ToArray() }).ToList()
            });
            return true;
        }
        catch (Exception exception)
        {
            WriteReport(new { FatalError = exception.ToString(), Availability = ModelAuditAvailability.Observations });
            return true;
        }
    }

    private object Test(Document document, RebarSplitScope scope, bool preserveAnnotations, bool unprotectedOnly = false,
        RebarSplitProtectionAction action = RebarSplitProtectionAction.Cancel)
    {
        string scenario = (unprotectedOnly ? "UnprotectedSelection" : scope + "_" + action) +
            (preserveAnnotations ? "_AnnotationsOn" : "_AnnotationsOff") +
            (annotationViewId is null ? "_InitialView" : "_OwnerView");
        using TransactionGroup fixture = new(document, "Customer model audit — rollback");
        fixture.Start();
        using RebarSplitNativeFailureCapture native = new(document, allowWarnings: true);
        try
        {
            RebarSplitService service = new(text => File.AppendAllText(reportPath + ".diagnostics.txt", text + "\n"));
            // Test-only selection of ordinary sets; no source is unpinned or removed from a group.
            List<ElementId> selection = unprotectedOnly
                ? sets.Where(id => !document.GetElement(id).Pinned && document.GetElement(id).GroupId == ElementId.InvalidElementId).ToList()
                : sets;
            IReadOnlyList<ElementId> candidates = service.FindCandidates(document, selection, scope);
            if (unprotectedOnly && candidates.Count == 0)
                throw new InvalidOperationException("The model has no unprotected sets for the annotation regression.");
            if (action == RebarSplitProtectionAction.Cancel && candidates.Select(document.GetElement)
                .Any(source => source.Pinned || source.GroupId != ElementId.InvalidElementId))
                return TestProtectedModel(document, scope, preserveAnnotations, service, candidates);
            List<ElementId> protectedIds = candidates.Where(id => document.GetElement(id).Pinned
                || document.GetElement(id).GroupId != ElementId.InvalidElementId).ToList();
            List<ElementId> processed = candidates.Where(id => action != RebarSplitProtectionAction.Skip || !protectedIds.Contains(id)).ToList();
            Dictionary<ElementId, Guid> candidateVersions = candidates.ToDictionary(id => id, id => document.GetElement(id).VersionGuid);
            Dictionary<ElementId, Guid> skippedVersions = candidates.Except(processed).ToDictionary(id => id, id => candidateVersions[id]);
            var before = processed.Select(document.GetElement).Cast<Rebar>().ToDictionary(bar => bar.Id,
                bar => new { bar.Pinned, Grouped = bar.GroupId != ElementId.InvalidElementId, Host = bar.GetHostId(),
                    Curves = RebarSplitGeometry.ExistingPositions(bar).Select(index => RebarSplitGeometry.Curves(bar, index)).ToList() });
            List<IList<Curve>> expected = processed.Select(id => (Rebar)document.GetElement(id))
                .SelectMany(bar => RebarSplitGeometry.ExistingPositions(bar).Select(index => RebarSplitGeometry.Curves(bar, index))).ToList();
            int expectedCount = expected.Count;
            var annotationsBefore = AnnotationCounts(document);
            List<RebarSplitDimensionAudit> dimensions = RebarSplitDimensionAudit.Capture(document, processed);
            List<RebarSplitParameterIssue> parameterIssues = new();
            RebarSplitResult result = service.Execute(document, candidates, preserveAnnotations, issues =>
            {
                parameterIssues.AddRange(issues);
                return RebarSplitParameterAction.Cancel;
            }, _ => action, _ => action switch
            {
                RebarSplitProtectionAction.Cancel => RebarSplitGroupAction.Cancel,
                RebarSplitProtectionAction.Skip => RebarSplitGroupAction.Skip,
                _ => RebarSplitGroupAction.Extract
            });
            bool automaticGroupSkip = result.Warnings.Any(warning => warning.StartsWith("Не выполнено извлечение из групп", StringComparison.Ordinal));
            if (automaticGroupSkip)
            {
                List<ElementId> untouchedGroups = processed.Where(id => before[id].Grouped).ToList();
                foreach (ElementId id in untouchedGroups) skippedVersions[id] = candidateVersions[id];
                processed.RemoveAll(untouchedGroups.Contains);
                expected = processed.SelectMany(id => before[id].Curves).ToList();
                expectedCount = expected.Count;
                if (untouchedGroups.Any(id => !result.Warnings.Any(warning => warning.Contains(id.ToString()))))
                    throw new InvalidOperationException("Skipped grouped source is missing from the result notice.");
            }
            bool geometryMatches = !result.Cancelled && result.CreatedIds.Count == expectedCount;
            foreach (ElementId id in result.CreatedIds)
            {
                Rebar bar = (Rebar)document.GetElement(id);
                int index = expected.FindIndex(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0)));
                geometryMatches &= index >= 0 && bar.Quantity == 1 && bar.LayoutRule == RebarLayoutRule.Single;
                if (index >= 0) expected.RemoveAt(index);
            }
            bool sourcesDeleted = processed.All(id => document.GetElement(id) is null);
            bool skippedUnchanged = skippedVersions.All(pair => document.GetElement(pair.Key)?.VersionGuid == pair.Value);
            bool statusesMatch = result.CreatedIds.Select(document.GetElement).OfType<Rebar>().All(bar =>
            {
                var source = before.Values.FirstOrDefault(item => result.ElementMap.Single(item.Host) == bar.GetHostId()
                    && item.Curves.Any(curves => RebarSplitGeometry.SameCurves(curves, RebarSplitGeometry.Curves(bar, 0))));
                bool preserve = action != RebarSplitProtectionAction.RemoveStatus;
                return source is not null && bar.Pinned == (preserve && source.Pinned)
                    && bar.GroupId == ElementId.InvalidElementId;
            });
            List<DimensionAuditResult> dimensionResults = preserveAnnotations
                ? dimensions.Select(dimension => dimension.Verify(document, result.CreatedIds, annotationViewId is not null,
                    skippedVersions.Keys.ToList())).ToList()
                : new();
            return new
            {
                Scenario = scenario,
                Passed = !result.Cancelled && geometryMatches && sourcesDeleted && expected.Count == 0 && statusesMatch && skippedUnchanged
                    && result.SkippedCount == candidates.Count - processed.Count &&
                    dimensionResults.All(dimension => dimension.Passed) && native.Failures.All(failure => failure.Severity == "Warning"),
                Candidates = candidates.Select(id => id.IntegerValue).ToArray(), result.SetCount, AutomaticGroupSkip = automaticGroupSkip,
                Bars = result.CreatedIds.Count, ExpectedBars = expectedCount, GeometryMatches = geometryMatches,
                SourcesDeleted = sourcesDeleted, StatusesMatch = statusesMatch, SkippedUnchanged = skippedUnchanged,
                ParameterIssues = parameterIssues, NativeFailures = native.Failures,
                result.Warnings,
                AnnotationsBefore = annotationsBefore, AnnotationsAfter = AnnotationCounts(document),
                OrdinaryDimensions = dimensionResults
            };
        }
        catch (Exception exception)
        {
            return new { Scenario = scenario, Passed = false, Error = exception.ToString() };
        }
        finally
        {
            fixture.RollBack();
        }
    }

    private object TestProtectedModel(Document document, RebarSplitScope scope, bool preserveAnnotations,
        RebarSplitService service, IReadOnlyList<ElementId> candidates)
    {
        Dictionary<ElementId, Guid> before = DocumentVersions(document);
        var annotationsBefore = AnnotationCounts(document);
        var protectedSources = candidates.Select(document.GetElement)
            .Where(source => source.Pinned || source.GroupId != ElementId.InvalidElementId)
            .Select(source => new { Id = source.Id.IntegerValue, source.Pinned, GroupId = source.GroupId.IntegerValue }).ToList();
        int parameterDialogs = 0;
        using RebarSplitNativeFailureCapture native = new(document);
        int protectionDialogs = 0;
        RebarSplitResult result = service.Execute(document, candidates, preserveAnnotations, _ =>
        {
            parameterDialogs++;
            return RebarSplitParameterAction.SplitAnyway;
        }, _ => { protectionDialogs++; return RebarSplitProtectionAction.Cancel; },
            _ => { protectionDialogs++; return RebarSplitGroupAction.Cancel; });
        bool blocked = result.Cancelled;
        Dictionary<ElementId, Guid> after = DocumentVersions(document);
        bool unchanged = before.Count == after.Count && before.All(pair =>
            after.TryGetValue(pair.Key, out Guid version) && version == pair.Value);
        return new
        {
            Scenario = scope + "_Cancel" + (preserveAnnotations ? "_AnnotationsOn" : "_AnnotationsOff") +
                (annotationViewId is null ? "_InitialView" : "_OwnerView"),
            Passed = blocked && unchanged && parameterDialogs == 0 && protectionDialogs == 1 && native.Failures.Count == 0,
            Blocked = blocked, EntireDocumentUnchanged = unchanged, ElementsChecked = before.Count,
            ProtectionDialogs = protectionDialogs, NativeFailures = native.Failures,
            ProtectedSources = protectedSources, Candidates = candidates.Select(id => id.IntegerValue).ToArray(),
            SetCount = 0, Bars = 0, AnnotationsBefore = annotationsBefore, AnnotationsAfter = AnnotationCounts(document)
        };
    }

    private static Dictionary<ElementId, Guid> DocumentVersions(Document document) =>
        new FilteredElementCollector(document).WhereElementIsNotElementType().ToElements()
            .Concat(new FilteredElementCollector(document).WhereElementIsElementType().ToElements())
            .ToDictionary(element => element.Id, element => element.VersionGuid);

    private static object AnnotationCounts(Document document) => new
    {
        Tags = new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).GetElementCount(),
        Dimensions = new FilteredElementCollector(document).OfClass(typeof(Dimension)).GetElementCount(),
        MultiReferenceAnnotations = new FilteredElementCollector(document).OfClass(typeof(MultiReferenceAnnotation)).GetElementCount()
    };

    private void InspectDimensionReferences(Document document)
    {
        List<string> lines = new();
        foreach (Dimension dimension in new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            List<Reference> references = dimension.References.Cast<Reference>().ToList();
            if (!references.Any(reference => sets.Contains(reference.ElementId))) continue;
            lines.Add($"Dimension {dimension.Id}; view={dimension.OwnerViewId}; shape={dimension.DimensionShape}; segments={dimension.NumberOfSegments}; available={dimension.AreReferencesAvailable}");
            lines.Add($"  type={dimension.DimensionType.Name}; equal={dimension.AreSegmentsEqual}; " +
                $"display={dimension.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger()}; " +
                $"leader={dimension.get_Parameter(BuiltInParameter.DIM_LEADER)?.AsInteger()}");
            foreach (DimensionSegment segment in dimension.Segments)
                lines.Add($"  segment value={segment.Value}; adjustable={segment.IsTextPositionAdjustable()}; " +
                    $"text={(segment.IsTextPositionAdjustable() ? segment.TextPosition?.ToString() : "not applicable")}");
            foreach (Reference reference in references)
            {
                Element element = document.GetElement(reference.ElementId);
                string stable = reference.ConvertToStableRepresentation(document);
                GeometryObject? geometry = element.GetGeometryObjectFromReference(reference);
                lines.Add($"  {stable}; type={reference.ElementReferenceType}; geometry={geometry?.GetType().Name ?? "null"}");
                if (element is not Rebar rebar) continue;
                lines.Add($"  barIndex={rebar.GetBarIndexFromReference(reference)}");
            }
        }
        File.WriteAllLines(reportPath + ".references.txt", lines);
    }

    private void RequireCallback()
    {
        if (DateTime.UtcNow - stageStarted > TimeSpan.FromSeconds(90))
            throw new TimeoutException("Revit did not call the ribbon availability probe in the expected selection state.");
    }

    private void Progress(string text) => File.AppendAllText(reportPath + ".progress.txt", "\n" + text);

    private void WriteReport(object report) => File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(report));
}

public sealed class ModelAuditAvailability : IExternalCommandAvailability
{
    internal static string? ModelPath { get; set; }
    internal static List<AvailabilityObservation> Observations { get; } = new();

    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        bool available = new RebarSplitCommandAvailability().IsCommandAvailable(applicationData, selectedCategories);
        UIDocument? uiDocument = applicationData.ActiveUIDocument;
        if (uiDocument is not null && string.Equals(uiDocument.Document.PathName, ModelPath, StringComparison.OrdinalIgnoreCase))
        {
            int selected = uiDocument.Selection.GetElementIds().Count;
            bool readOnly = uiDocument.Document.IsReadOnly;
            if (!Observations.Any(item => item.SelectedCount == selected && item.IsReadOnly == readOnly))
                Observations.Add(new AvailabilityObservation
                {
                    SelectedCount = selected, IsReadOnly = readOnly,
                    PreviousCondition = !uiDocument.Document.IsFamilyDocument && !readOnly,
                    FixedCondition = available
                });
        }
        return available;
    }
}

internal sealed class AvailabilityObservation
{
    public int SelectedCount { get; set; }
    public bool IsReadOnly { get; set; }
    public bool PreviousCondition { get; set; }
    public bool FixedCondition { get; set; }
}

[Transaction(TransactionMode.Manual)]
public sealed class ModelAuditCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) => Result.Succeeded;
}
