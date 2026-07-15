using TrueBIM.App.Modules.Lintels.Models;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelAssemblyCreationGate
{
    public static bool CanStart(IReadOnlyCollection<long> currentTypeIds)
    {
        if (currentTypeIds is null)
        {
            throw new ArgumentNullException(nameof(currentTypeIds));
        }

        return currentTypeIds.Count == 1;
    }

    public static bool IsCurrentSelection(
        IReadOnlyCollection<long> checkedTypeIds,
        IReadOnlyCollection<long> currentTypeIds)
    {
        if (checkedTypeIds is null)
        {
            throw new ArgumentNullException(nameof(checkedTypeIds));
        }

        if (currentTypeIds is null)
        {
            throw new ArgumentNullException(nameof(currentTypeIds));
        }

        return checkedTypeIds.Count == currentTypeIds.Count
            && checkedTypeIds.OrderBy(typeId => typeId)
                .SequenceEqual(currentTypeIds.OrderBy(typeId => typeId));
    }

    public static bool CanCreate(
        long? approvedTypeId,
        LintelAssemblyPreflightStatus? approvedStatus,
        IReadOnlyCollection<long> currentTypeIds)
    {
        if (currentTypeIds is null)
        {
            throw new ArgumentNullException(nameof(currentTypeIds));
        }

        return approvedTypeId is not null
            && approvedStatus == LintelAssemblyPreflightStatus.Ready
            && currentTypeIds.Count == 1
            && currentTypeIds.Single() == approvedTypeId.Value;
    }
}
