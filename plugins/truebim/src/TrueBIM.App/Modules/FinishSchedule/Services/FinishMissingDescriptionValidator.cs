using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishMissingDescriptionValidator
{
    public static IReadOnlyList<long> FindTypeIds(FinishElementCollection collection, FinishScheduleSettings settings)
    {
        return new FinishClassificationService().Classify(collection, settings).Elements
            .Select(element => element.Element.TypeId).Distinct()
            .Where(id => !collection.Types.TryGetValue(id, out FinishTypeSnapshot? type)
                || !type.HasDescriptionParameter
                || (string.IsNullOrWhiteSpace(type.DescriptionValue?.RawValue)
                    && string.IsNullOrWhiteSpace(type.DescriptionValue?.DisplayValue)))
            .OrderBy(id => id).ToArray();
    }
}
