using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

internal sealed record RebarSplitBar(int Position, ElementId Id, IList<Curve> ExpectedCurves);
internal sealed record RebarSplitSet(ElementId SourceId, Rebar Source, ElementId TypeId, ElementId HostId,
    RebarSplitParameters Parameters, IReadOnlyList<RebarSplitBar> Bars);
public sealed record RebarSplitResult(bool Cancelled, int SetCount, int SkippedCount, IReadOnlyList<ElementId> CreatedIds);

public sealed class RebarSplitService
{
    private readonly Action<string>? diagnostic;

    public RebarSplitService(Action<string>? diagnostic = null) => this.diagnostic = diagnostic;

    public IReadOnlyList<ElementId> FindCandidates(Document document, ICollection<ElementId> selection, RebarSplitScope scope)
    {
        IEnumerable<Element> elements = scope == RebarSplitScope.Selection
            ? selection.Select(document.GetElement)
            : new FilteredElementCollector(document).OfClass(typeof(Rebar)).ToElements();
        List<ElementId> result = new();
        foreach (Rebar rebar in elements.Where(RebarSplitGeometry.IsSupportedSet).Cast<Rebar>())
        {
            if (scope == RebarSplitScope.VaryingSetsInProject
                && !RebarSplitPolicy.HasDifferentLengths(RebarSplitGeometry.ExistingPositions(rebar)
                    .Select(index => RebarSplitGeometry.Curves(rebar, index).Sum(curve => curve.Length)))) continue;
            result.Add(rebar.Id);
        }
        return result;
    }

    public RebarSplitResult Execute(Document document, IReadOnlyList<ElementId> sourceIds, bool preserveAnnotations,
        Func<IReadOnlyList<RebarSplitParameterIssue>, RebarSplitParameterAction> resolveParameterIssues)
    {
        HashSet<ElementId> skipped = new();
        HashSet<ElementId> forced = new();
        // Повтор после Commit нужен, если updater изменил параметры. Каждый повтор добавляет ID в skipped.
        while (true)
        {
            using TransactionGroup group = new(document, "TrueBIM: разбить арматуру");
            group.Start();
            List<RebarSplitSet> replacements = new();
            using (Transaction transaction = new(document, "Разбить наборы арматуры"))
            {
                transaction.Start();
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetForcedModalHandling(true));
                List<RebarSplitParameterIssue> issues = new();
                List<ElementId> pending = new();
                foreach (ElementId id in sourceIds.Distinct().Where(id => !skipped.Contains(id)))
                {
                    if (document.GetElement(id) is not Rebar source || !RebarSplitGeometry.IsSupportedSet(source)) continue;
                    using SubTransaction attempt = new(document);
                    attempt.Start();
                    RebarSplitSet replacement = CreateSet(document, source, out IReadOnlyList<string> errors);
                    if (errors.Count > 0 && !forced.Contains(id))
                    {
                        issues.Add(new(RevitElementIds.GetValue(id), errors));
                        pending.Add(id);
                        attempt.RollBack();
                    }
                    else
                    {
                        attempt.Commit();
                        replacements.Add(replacement);
                    }
                }

                if (issues.Count > 0)
                {
                    RebarSplitParameterAction action = resolveParameterIssues(issues);
                    if (action == RebarSplitParameterAction.Cancel)
                    {
                        transaction.RollBack();
                        group.RollBack();
                        return Cancelled();
                    }
                    if (action == RebarSplitParameterAction.Skip)
                        skipped.UnionWith(pending);
                    else
                    {
                        forced.UnionWith(pending);
                        foreach (ElementId id in pending)
                            replacements.Add(CreateSet(document, (Rebar)document.GetElement(id), out _));
                    }
                }

                if (replacements.Count == 0)
                {
                    transaction.RollBack();
                    group.RollBack();
                    return new(false, 0, skipped.Count, Array.Empty<ElementId>());
                }

                if (preserveAnnotations) new RebarSplitAnnotations(diagnostic).Restore(document, replacements);
                foreach (RebarSplitSet replacement in replacements) document.Delete(replacement.SourceId);
                document.Regenerate();
                ValidateGeometry(document, replacements);
                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return Cancelled();
                }
            }

            ValidateGeometry(document, replacements);
            List<RebarSplitParameterIssue> changed = replacements.Where(set => !forced.Contains(set.SourceId))
                .Select(set => new RebarSplitParameterIssue(RevitElementIds.GetValue(set.SourceId),
                    set.Bars.SelectMany(bar => set.Parameters.Validate(document.GetElement(bar.Id))).Distinct().ToList()))
                .Where(issue => issue.Parameters.Count > 0).ToList();
            if (changed.Count > 0)
            {
                RebarSplitParameterAction action = resolveParameterIssues(changed);
                if (action == RebarSplitParameterAction.Cancel)
                {
                    group.RollBack();
                    return Cancelled();
                }
                if (action == RebarSplitParameterAction.Skip)
                {
                    skipped.UnionWith(changed.Select(issue => RevitElementIds.Create(issue.SourceId)));
                    group.RollBack();
                    continue;
                }
            }
            group.Assimilate();
            return new(false, replacements.Count, skipped.Count, replacements.SelectMany(set => set.Bars.Select(bar => bar.Id)).ToList());
        }
    }

    private static RebarSplitSet CreateSet(Document document, Rebar source, out IReadOnlyList<string> errors)
    {
        HashSet<ElementId> shapeParameters = RebarSplitGeometry.ShapeParameters(source);
        RebarSplitParameters parameters = RebarSplitParameters.Capture(source, shapeParameters);
        List<RebarSplitBar> bars = new();
        List<string> parameterErrors = new();
        foreach (int index in RebarSplitGeometry.ExistingPositions(source))
        {
            IList<Curve> expected = RebarSplitGeometry.Curves(source, index);
            Rebar target = RebarSplitGeometry.CreateSingle(document, source, index, shapeParameters);
            parameterErrors.AddRange(parameters.Apply(target));
            bars.Add(new(index, target.Id, expected));
        }
        document.Regenerate();
        foreach (RebarSplitBar bar in bars)
        {
            Rebar target = (Rebar)document.GetElement(bar.Id);
            RebarSplitGeometry.RestorePosition(document, target, bar.ExpectedCurves);
            RebarSplitGeometry.Validate(target, bar.ExpectedCurves, source.GetTypeId(), source.GetHostId());
            parameterErrors.AddRange(parameters.Validate(target));
        }
        errors = parameterErrors.Distinct().ToList();
        return new(source.Id, source, source.GetTypeId(), source.GetHostId(), parameters, bars);
    }

    private static void ValidateGeometry(Document document, IEnumerable<RebarSplitSet> replacements)
    {
        foreach (RebarSplitSet set in replacements)
        foreach (RebarSplitBar bar in set.Bars)
        {
            if (document.GetElement(bar.Id) is not Rebar target)
                throw new InvalidOperationException($"Набор {set.SourceId}: новый стержень исчез после удаления исходника. Изменения отменены.");
            RebarSplitGeometry.Validate(target, bar.ExpectedCurves, set.TypeId, set.HostId);
        }
    }

    private static RebarSplitResult Cancelled() => new(true, 0, 0, Array.Empty<ElementId>());
}
