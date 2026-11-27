using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Объединяет сообщения для чтения, сохраняя исходные замечания для выбора элементов и записи.</summary>
internal sealed class FinishReportWarningGroups
{
    private readonly HashSet<string> replaced = new(StringComparer.Ordinal);
    private readonly HashSet<long> skippedAssignmentIds = [];
    private readonly List<string> lines = [];
    private readonly List<string> information = [];

    public IReadOnlyList<string> Lines => lines;
    public IReadOnlyList<string> Information => information;
    public bool Replaces(string message) => replaced.Contains(message);

    public FinishReportWarningGroups(FinishSchedulePreviewResult? preview, IEnumerable<FinishWriteIssue>? issues = null)
    {
        if (preview is null) return;
        Dictionary<FinishOwnershipFinding, List<long>> idsByFinding = [];
        foreach (var element in preview.GeometryWarnings.Where(w => w.OwnershipFinding.HasValue && w.ElementId.HasValue)
                     .GroupBy(w => w.ElementId!.Value))
        {
            // An empty ownership parameter is a consequence when geometry already explains the omission.
            var finding = element.FirstOrDefault(w => w.OwnershipFinding != FinishOwnershipFinding.EmptyParameter)
                ?.OwnershipFinding ?? FinishOwnershipFinding.EmptyParameter;
            if (!idsByFinding.TryGetValue(finding, out var ids)) idsByFinding.Add(finding, ids = []);
            ids.Add(element.Key);
            if (finding is FinishOwnershipFinding.NoContact or FinishOwnershipFinding.OutsideSearch)
                skippedAssignmentIds.Add(element.Key);
            foreach (var warning in element) replaced.Add(warning.Message);
        }
        foreach (var group in idsByFinding.OrderBy(pair => pair.Key))
        {
            string explanation = group.Key switch
            {
                FinishOwnershipFinding.NoContact => "Элементы проверены, но контакт с помещением не найден. Проверьте контур, уровень элемента и наличие помещений.",
                FinishOwnershipFinding.OutsideSearch => "Элементы вне области поиска помещений. Проверьте их расположение и привязку к уровню, если ожидаете отделку в этих помещениях.",
                FinishOwnershipFinding.OutsideLevelRoomSearch => "Элементы привязаны к выбранному уровню и находятся вне области поиска его помещений. В площадь выбранных помещений они не вошли.",
                FinishOwnershipFinding.OutsideFloorProjection => "Полы вне проекции подходящих помещений. В ведомости учтено 0 м².",
                FinishOwnershipFinding.EmptyParameter => "Принадлежность помещению не заполнена. Площадь не учтена.",
                _ => throw new ArgumentOutOfRangeException()
            };
            var destination = group.Key is FinishOwnershipFinding.OutsideLevelRoomSearch or FinishOwnershipFinding.OutsideFloorProjection
                ? information : lines;
            destination.Add($"{explanation} ID элементов ({group.Value.Count}): {string.Join(", ", group.Value.OrderBy(id => id))}.");
        }

        var missingDescriptions = (preview.Aggregation?.Details ?? [])
            .Where(w => w.Code == FinishAggregationWarningCode.MissingDescription && w.TypeId is > 0).ToArray();
        if (missingDescriptions.Length > 0)
        {
            var types = missingDescriptions.Select(w => w.TypeId!.Value).Distinct().OrderBy(id => id).ToArray();
            lines.Add($"Заполните описание отделки в свойствах типов. ID типоразмеров ({types.Length}): {string.Join(", ", types)}. До заполнения в ведомости выводится «{FinishDescriptionNormalizer.MissingDescriptionDisplay}».");
            foreach (var warning in missingDescriptions) replaced.Add(warning.Message);
        }
        foreach (var issue in issues ?? [])
            if (Replaces(issue)) replaced.Add(issue.Message);
    }

    public bool Replaces(FinishWriteIssue issue) => issue.ElementId is long id && skippedAssignmentIds.Contains(id)
        && issue.Code is FinishWriteIssueCode.UnknownOwnership or FinishWriteIssueCode.UnassignedOwnership;
}
