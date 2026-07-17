using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class RoomFinishWriteValueBuilder
{
    public FinishParameterTargetBuildResult Build(
        FinishScheduleSettings settings,
        FinishAggregationResult aggregation)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (aggregation is null)
        {
            throw new ArgumentNullException(nameof(aggregation));
        }

        List<FinishParameterTargetValue> targets = [];
        List<FinishWriteIssue> issues = [];
        foreach (KeyValuePair<long, FinishRoomGroupOutput> pair in aggregation.RoomOutputs
                     .OrderBy(item => item.Key))
        {
            long roomId = pair.Key;
            FinishRoomGroupOutput output = pair.Value;
            AddTarget(
                targets,
                issues,
                roomId,
                settings.RoomListOutputParameter,
                "Список помещений",
                output.RoomList);
            AddCategoryTargets(
                targets,
                issues,
                roomId,
                FinishPreviewCategory.Walls,
                "Стены",
                settings.Walls,
                output.Walls);
            AddCategoryTargets(
                targets,
                issues,
                roomId,
                FinishPreviewCategory.Floors,
                "Полы",
                settings.Floors,
                output.Floors);
            AddCategoryTargets(
                targets,
                issues,
                roomId,
                FinishPreviewCategory.Ceilings,
                "Потолки",
                settings.Ceilings,
                output.Ceilings);
        }

        return new FinishParameterTargetBuildResult(
            aggregation.RoomOutputs.Count,
            targets,
            issues);
    }

    private static void AddCategoryTargets(
        List<FinishParameterTargetValue> targets,
        List<FinishWriteIssue> issues,
        long roomId,
        FinishPreviewCategory category,
        string categoryName,
        FinishCategorySettings settings,
        FinishFormattedCategoryOutput? output)
    {
        if (!settings.IsEnabled)
        {
            return;
        }

        if (output is null)
        {
            issues.Add(ConfigurationIssue(
                $"{categoryName}: агрегированный output отсутствует.",
                roomId,
                categoryName));
            return;
        }

        AddTarget(
            targets,
            issues,
            roomId,
            settings.OutputDescriptionParameter,
            $"{categoryName}: описание",
            output.DescriptionText,
            category);
        AddTarget(
            targets,
            issues,
            roomId,
            settings.OutputAreaParameter,
            $"{categoryName}: площадь",
            output.AreaText,
            category);
    }

    private static void AddTarget(
        List<FinishParameterTargetValue> targets,
        List<FinishWriteIssue> issues,
        long roomId,
        ParameterReference? reference,
        string role,
        string value,
        FinishPreviewCategory? category = null)
    {
        if (reference is null)
        {
            issues.Add(ConfigurationIssue(
                $"{role}: не выбран обязательный выходной параметр.",
                roomId,
                role));
            return;
        }

        targets.Add(new FinishParameterTargetValue(
            roomId,
            reference,
            role,
            value,
            isRequired: true,
            category));
    }

    private static FinishWriteIssue ConfigurationIssue(
        string message,
        long roomId,
        string role)
    {
        return new FinishWriteIssue(
            FinishWriteIssueCode.OutputConfigurationInvalid,
            FinishWriteIssueSeverity.Critical,
            message,
            roomId,
            role);
    }
}
