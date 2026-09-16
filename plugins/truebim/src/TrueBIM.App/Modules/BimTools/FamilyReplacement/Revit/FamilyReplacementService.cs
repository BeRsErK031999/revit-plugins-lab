using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

public sealed class FamilyReplacementService
{
    public FamilyReplacementResult Replace(Document document, IReadOnlyList<long> sourceIds, long targetTypeId,
        FamilyReplacementAlignment alignment, long? targetTagTypeId = null, bool ignoreIntersectionWarnings = false)
    {
        if (document.IsFamilyDocument || document.IsReadOnly || document.IsModifiable)
            throw new InvalidOperationException("Замена выполняется в доступном для редактирования проекте без открытой транзакции.");
        if (document.GetElement(RevitElementIds.Create(targetTypeId)) is not FamilySymbol symbol)
            throw new InvalidOperationException("Выбранный тип семейства больше не доступен в проекте.");
        List<FamilyReplacementItemResult> results = new();
        if (sourceIds.Count == 0)
            return new FamilyReplacementResult(results);

        FamilyReplacementPlacementService placement = new();
        FamilyReplacementParameterService parameters = new();
        FamilyReplacementAnnotationService annotations = new(targetTagTypeId.HasValue ? RevitElementIds.Create(targetTagTypeId.Value) : null);
        Dictionary<long, FamilyReplacementPlacementSnapshot> originalPlacements = new();
        // Freeze positions before any replacement can trigger joins or dependent updates.
        foreach (long id in sourceIds.Distinct())
        {
            try
            {
                if (document.GetElement(RevitElementIds.Create(id)) is not FamilyInstance source)
                    throw new InvalidOperationException("Исходный экземпляр больше не существует.");
                originalPlacements.Add(id, placement.Capture(document, source, alignment));
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                results.Add(new FamilyReplacementItemResult(id, null, false, exception.Message));
            }
        }
        Dictionary<long, FamilyReplacementPlacementSnapshot> completedPlacements = new();
        using TransactionGroup group = new(document, "TrueBIM: замена семейств");
        if (group.Start() != TransactionStatus.Started)
            throw new InvalidOperationException("Не удалось начать замену семейств.");
        foreach (long sourceId in sourceIds.Distinct())
        {
            if (!originalPlacements.TryGetValue(sourceId, out FamilyReplacementPlacementSnapshot? originalPlacement))
                continue;
            using TransactionGroup itemGroup = new(document, $"Проверка замены {sourceId}");
            using Transaction transaction = new(document, $"Замена экземпляра {sourceId}");
            FamilyReplacementFailureHandler failures = new(ignoreIntersectionWarnings);
            try
            {
                if (document.GetElement(RevitElementIds.Create(sourceId)) is not FamilyInstance source)
                    throw new InvalidOperationException("Исходный экземпляр больше не существует.");
                CheckSource(source, symbol);
                FamilyReplacementParameterSnapshot values = parameters.Capture(source);
                FamilyReplacementAnnotationSnapshot annotationSnapshot = annotations.Capture(document, source);
                HashSet<long> allowedDeleted = CollectOwnedInstances(document, source);
                if (itemGroup.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException("Не удалось начать проверяемую замену экземпляра.");
                if (transaction.Start() != TransactionStatus.Started)
                    throw new InvalidOperationException("Не удалось начать транзакцию замены.");
                transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
                    .SetFailuresPreprocessor(failures).SetClearAfterRollback(true));
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                FamilyInstance target = placement.Create(document, source, symbol);
                CopyContext(source, target);
                parameters.Apply(target, values);
                document.Regenerate();
                placement.Align(document, originalPlacement, target);
                parameters.Validate(target, values);
                IReadOnlyCollection<ElementId> oldAnnotations = annotations.Restore(document, source, target, annotationSnapshot);
                foreach (ElementId id in oldAnnotations)
                    allowedDeleted.Add(RevitElementIds.GetValue(id));

                bool wasPinned = source.Pinned;
                long newId = RevitElementIds.GetValue(target.Id);
                List<ElementId> toDelete = oldAnnotations.Concat(new[] { source.Id }).Distinct().ToList();
                source.Pinned = false;
                ICollection<ElementId> removed = document.Delete(toDelete);
                List<long> unexpected = removed.Select(RevitElementIds.GetValue).Where(id => !allowedDeleted.Contains(id)).ToList();
                if (unexpected.Count > 0)
                    throw new InvalidOperationException("Замена затрагивает дополнительные элементы: " + string.Join(", ", unexpected.Take(8)) + ". Исходный экземпляр сохранён.");
                document.Regenerate();
                if (!target.IsValidObject)
                    throw new InvalidOperationException("Revit удалил новый экземпляр вместе с исходным.");
                parameters.Validate(target, values);
                annotations.ValidateRestored(document, annotationSnapshot);
                placement.Validate(document, originalPlacement, target);
                target.Pinned = wasPinned;

                TransactionStatus status = transaction.Commit();
                if (status != TransactionStatus.Committed)
                    throw new InvalidOperationException(failures.Description.Length > 0 ? failures.Description : "Revit отменил замену экземпляра.");
                // Commit runs auto-join and updaters after the last explicit Regenerate.
                // Keep a group open so a failed final validation can undo that commit.
                placement.Validate(document, originalPlacement, target);
                parameters.Validate(target, values);
                annotations.ValidateRestored(document, annotationSnapshot);
                foreach (KeyValuePair<long, FamilyReplacementPlacementSnapshot> completed in completedPlacements)
                {
                    if (document.GetElement(RevitElementIds.Create(completed.Key)) is not FamilyInstance previous)
                        throw new InvalidOperationException("Пересчёт удалил ранее заменённый экземпляр.");
                    placement.Validate(document, completed.Value, previous);
                }
                if (itemGroup.Assimilate() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Не удалось завершить проверенную замену экземпляра.");
                completedPlacements.Add(newId, originalPlacement);
                string message = "Заменён; положение, параметры и поддерживаемые аннотации сохранены.";
                if (failures.IgnoredDescription.Length > 0)
                    message += " Игнорированы предупреждения о пересечениях: " + failures.IgnoredDescription;
                results.Add(new FamilyReplacementItemResult(sourceId, newId, true, message));
            }
            catch (Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                // The document is unusable until rollback; do not continue with another source.
                if (transaction.GetStatus() == TransactionStatus.Started)
                    transaction.RollBack();
                if (itemGroup.GetStatus() == TransactionStatus.Started)
                    itemGroup.RollBack();
                group.RollBack();
                throw;
            }
            catch (Exception exception)
            {
                if (transaction.GetStatus() == TransactionStatus.Started)
                    transaction.RollBack();
                if (itemGroup.GetStatus() == TransactionStatus.Started)
                    itemGroup.RollBack();
                results.Add(new FamilyReplacementItemResult(sourceId, null, false, exception.Message));
            }
        }

        if (results.Any(item => item.Replaced))
        {
            if (group.Assimilate() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit не смог завершить группу замены. Проверьте состояние документа.");
        }
        else
            group.RollBack();
        return new FamilyReplacementResult(results);
    }

    private static void CheckSource(FamilyInstance source, FamilySymbol symbol)
    {
        if (source.Symbol.Id == symbol.Id)
            throw new InvalidOperationException("У экземпляра уже выбран целевой тип.");
        if (source.GroupId != ElementId.InvalidElementId || source.AssemblyInstanceId != ElementId.InvalidElementId || source.SuperComponent is not null)
            throw new InvalidOperationException("Экземпляр входит в группу, сборку или другое семейство. Замена отдельно от владельца не выполняется.");
        if (source.GetSubComponentIds().Count > 0)
            throw new InvalidOperationException("Семейство содержит общие вложенные экземпляры. Перенос их параметров и связей требует отдельного сопоставления.");
        if (source.Location is not LocationPoint)
            throw new InvalidOperationException("Поддерживаются экземпляры с точкой размещения.");
        MEPModel? mep = source.MEPModel;
        if (mep is null)
            return;
#if REVIT2021_OR_GREATER
        if (mep.GetElectricalSystems()?.Count > 0)
#else
        if (mep.ElectricalSystems?.Size > 0)
#endif
            throw new InvalidOperationException("Экземпляр подключён к электрической системе. Автоматический перенос подключений пока не поддерживается.");
        if (mep.ConnectorManager?.Connectors.Cast<Connector>().Any(connector => connector.ConnectorType != ConnectorType.Logical && connector.IsConnected) == true)
            throw new InvalidOperationException("У экземпляра есть подключённые коннекторы. Замена без сохранения подключений не выполняется.");
    }

    private static void CopyContext(FamilyInstance source, FamilyInstance target)
    {
        if (source.HasPhases())
        {
            if (target.CreatedPhaseId != source.CreatedPhaseId)
                target.CreatedPhaseId = source.CreatedPhaseId;
            if (target.DemolishedPhaseId != source.DemolishedPhaseId)
                target.DemolishedPhaseId = source.DemolishedPhaseId;
        }
        if (source.Document.IsWorkshared && source.WorksetId != target.WorksetId)
        {
            Parameter? workset = target.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (workset is null || workset.IsReadOnly || !workset.Set(source.WorksetId.IntegerValue))
                throw new InvalidOperationException("Не удалось сохранить рабочий набор экземпляра.");
        }
        if (source.DesignOption?.Id != target.DesignOption?.Id)
            throw new InvalidOperationException("Новый экземпляр попал в другой вариант конструкции. Активируйте исходный вариант.");
    }

    private static HashSet<long> CollectOwnedInstances(Document document, FamilyInstance source)
    {
        HashSet<long> ids = new() { RevitElementIds.GetValue(source.Id) };
        foreach (ElementId id in source.GetSubComponentIds())
        {
            if (document.GetElement(id) is FamilyInstance child)
                ids.UnionWith(CollectOwnedInstances(document, child));
        }
        return ids;
    }

}
