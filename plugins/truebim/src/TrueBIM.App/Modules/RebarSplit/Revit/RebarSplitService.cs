using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

internal sealed record RebarSplitBar(int Position, ElementId Id, IList<Curve> ExpectedCurves);
internal sealed record RebarSplitSet(ElementId SourceId, Rebar Source, ElementId TypeId, ElementId HostId,
    RebarSplitParameters Parameters, IReadOnlyList<RebarSplitBar> Bars, bool Pinned, ElementId GroupId);
public sealed record RebarSplitResult(bool Cancelled, int SetCount, int SkippedCount, IReadOnlyList<ElementId> CreatedIds)
{
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    internal RebarSplitElementMap ElementMap { get; init; } = new();
}

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
        Func<IReadOnlyList<RebarSplitParameterIssue>, RebarSplitParameterAction> resolveParameterIssues,
        Func<IReadOnlyList<RebarSplitProtectionIssue>, RebarSplitProtectionAction>? resolveProtectionIssues = null,
        Func<IReadOnlyList<RebarSplitProtectionIssue>, RebarSplitGroupAction>? resolveGroupIssues = null)
    {
        RebarSplitGroupScope scope = new(document, sourceIds);
        sourceIds = scope.SourceIds;
        HashSet<ElementId> skipped = new();
        HashSet<ElementId> forced = new();
        List<RebarSplitProtectionIssue> statuses = sourceIds.Distinct().Select(document.GetElement)
            .Where(RebarSplitGeometry.IsSupportedSet)
            .Where(source => source.Pinned || source.GroupId != ElementId.InvalidElementId)
            .Select(source => new RebarSplitProtectionIssue(RevitElementIds.GetValue(source.Id), source.Pinned,
                source.GroupId == ElementId.InvalidElementId ? null : RevitElementIds.GetValue(source.GroupId),
                (document.GetElement(source.GroupId) as Group)?.GroupType.Name)).ToList();
        List<RebarSplitProtectionIssue> groupIssues = statuses.Where(issue => issue.GroupId.HasValue).ToList();
        if (groupIssues.Count > 0)
        {
            RebarSplitGroupAction action = resolveGroupIssues?.Invoke(groupIssues) ?? RebarSplitGroupAction.Cancel;
            if (action == RebarSplitGroupAction.Cancel) return Cancelled();
            if (action == RebarSplitGroupAction.Skip)
                skipped.UnionWith(groupIssues.Select(issue => RevitElementIds.Create(issue.SourceId)));
        }
        List<RebarSplitProtectionIssue> protectionIssues = statuses.Where(issue => issue.Pinned
            && !skipped.Contains(RevitElementIds.Create(issue.SourceId))).ToList();
        RebarSplitProtectionAction protectionAction = protectionIssues.Count == 0 ? RebarSplitProtectionAction.PreserveStatus
            : resolveProtectionIssues?.Invoke(protectionIssues) ?? RebarSplitProtectionAction.Cancel;
        if (protectionAction == RebarSplitProtectionAction.Cancel) return Cancelled();
        if (protectionAction == RebarSplitProtectionAction.Skip)
            skipped.UnionWith(scope.Expand(protectionIssues.Select(issue => RevitElementIds.Create(issue.SourceId))));
        bool preserveStatus = protectionAction != RebarSplitProtectionAction.RemoveStatus;
        List<string> limitations = new();
        // Повтор после Commit нужен, если updater изменил параметры. Каждый повтор добавляет ID в skipped.
        while (true)
        {
            using TransactionGroup group = new(document, "TrueBIM: разбить арматуру");
            group.Start();
            List<RebarSplitSet> replacements = new();
            List<string> warnings = new(limitations);
            RebarSplitGroupExtraction? groups = null;
            RebarSplitElementMap elementMap = new();
            RebarSplitAnnotationSnapshot? annotations = null;
            IReadOnlyDictionary<ElementId, string> modelBefore = sourceIds.Any(id => !skipped.Contains(id)
                    && document.GetElement(id)?.GroupId != ElementId.InvalidElementId)
                ? RebarSplitGroupExtraction.CaptureModel(document) : new Dictionary<ElementId, string>();
            try
            {
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
                    IReadOnlyList<string> errors = Array.Empty<string>();
                    RebarSplitSet replacement = CreateSet(document, source, out errors);
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
                    {
                        skipped.UnionWith(scope.Expand(pending));
                        transaction.RollBack();
                        group.RollBack();
                        continue;
                    }
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
                    return new(false, 0, skipped.Count, Array.Empty<ElementId>()) { Warnings = warnings.Distinct().ToList() };
                }

                foreach (RebarSplitSet set in replacements)
                    if (set.Source.Pinned != set.Pinned || set.Source.GroupId != set.GroupId)
                        throw new InvalidOperationException($"Закрепление или группа набора {set.SourceId} изменились во время обработки. "
                            + "Операция отменена. Повторите разбиение с актуальными статусами.");
                groups = RebarSplitGroupExtraction.Capture(document, replacements, modelBefore, warnings);
                HashSet<ElementId> affected = new(groups.AffectedIds.Concat(replacements.Select(set => set.SourceId)));
                annotations = preserveAnnotations
                    ? RebarSplitAnnotationSnapshot.Capture(document, affected, warnings) : null;
                annotations?.RemoveCaptured(document);
                elementMap = groups.Extract(document, replacements);
                foreach (RebarSplitSet replacement in replacements)
                {
                    if (document.GetElement(replacement.SourceId) is Element source)
                    {
                        if (source.Pinned) source.Pinned = false;
                        document.Delete(source.Id);
                    }
                }
                document.Regenerate();
                foreach (RebarSplitSet replacement in replacements)
                foreach (RebarSplitBar bar in replacement.Bars)
                {
                    Element target = document.GetElement(bar.Id);
                    bool pinned = preserveStatus && replacement.Pinned;
                    if (target.Pinned != pinned) target.Pinned = pinned;
                }
                annotations?.Restore(document, elementMap);
                document.Regenerate();
                ValidateGeometry(document, replacements);
                groups.Validate(document, allowPendingGroupDeletion: true);
                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    group.RollBack();
                    return Cancelled();
                }
            }

            ValidateGeometry(document, replacements);
            groups?.Validate(document);
            annotations?.ReportLosses(document, elementMap);
            foreach (RebarSplitSet set in replacements)
            foreach (RebarSplitBar bar in set.Bars)
            {
                Element element = document.GetElement(bar.Id);
                bool preserve = protectionAction != RebarSplitProtectionAction.RemoveStatus;
                if (element.Pinned != (preserve && set.Pinned)
                    || element.GroupId != ElementId.InvalidElementId)
                    throw new InvalidOperationException($"Не удалось сохранить выбранное закрепление или принадлежность группе "
                        + $"для набора {set.SourceId}. Изменения операции отменены.");
            }
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
                    skipped.UnionWith(scope.Expand(changed.Select(issue => RevitElementIds.Create(issue.SourceId))));
                    group.RollBack();
                    continue;
                }
            }
            ValidateGeometry(document, replacements);
            groups?.Validate(document);
            group.Assimilate();
            foreach (string warning in warnings.Distinct()) diagnostic?.Invoke(warning);
            return new(false, replacements.Count, skipped.Count, replacements.SelectMany(set => set.Bars.Select(bar => bar.Id)).ToList())
                { Warnings = warnings.Distinct().ToList(), ElementMap = elementMap };
            }
            catch (RebarSplitGroupException exception)
            {
                group.RollBack();
                List<ElementId> unsupported = sourceIds.Where(id => !skipped.Contains(id)
                    && document.GetElement(id)?.GroupId != ElementId.InvalidElementId).ToList();
                if (unsupported.Count == 0) throw;
                skipped.UnionWith(scope.Expand(unsupported));
                limitations.Add($"Не выполнено извлечение из групп и разбиение наборов ID {string.Join(", ", unsupported)}: "
                    + exception.Message + " Эти наборы и их группы оставлены без изменений; остальные наборы обработаны отдельно.");
                diagnostic?.Invoke(exception.ToString());
            }
        }
    }

    internal static RebarSplitSet CreateSet(Document document, Rebar source, out IReadOnlyList<string> errors)
    {
        bool pinned = source.Pinned;
        ElementId groupId = source.GroupId;
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
        return new(source.Id, source, source.GetTypeId(), source.GetHostId(), parameters, bars, pinned, groupId);
    }

    private void ValidateGeometry(Document document, IEnumerable<RebarSplitSet> replacements)
    {
        foreach (RebarSplitSet set in replacements)
        foreach (RebarSplitBar bar in set.Bars)
        {
            if (document.GetElement(bar.Id) is not Rebar target)
                throw new InvalidOperationException($"Набор {set.SourceId}: новый стержень исчез после удаления исходника. Изменения отменены.");
            try { RebarSplitGeometry.Validate(target, bar.ExpectedCurves, set.TypeId, set.HostId); }
            catch (InvalidOperationException)
            {
                diagnostic?.Invoke($"Набор {set.SourceId}, позиция {bar.Position}, стержень {target.Id}, IsModifiable={document.IsModifiable}; "
                    + $"тип {set.TypeId}/{target.GetTypeId()}, основа {set.HostId}/{target.GetHostId()}; "
                    + $"ожидалось: {Describe(bar.ExpectedCurves)}; получено: {Describe(RebarSplitGeometry.Curves(target, 0))}");
                throw;
            }
        }
        static string Describe(IList<Curve> curves) => string.Join(" | ", curves.Select(curve =>
            $"{curve.GetType().Name}: {curve.GetEndPoint(0)} -> {curve.GetEndPoint(1)}, длина={curve.Length:R}"));
    }

    private static RebarSplitResult Cancelled() => new(true, 0, 0, Array.Empty<ElementId>());
}
