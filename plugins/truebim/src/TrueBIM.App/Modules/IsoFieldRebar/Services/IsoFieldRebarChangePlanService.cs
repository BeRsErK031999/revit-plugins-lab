using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldRebarChangePlanService
{
    public const string OwnedCommentPrefix = "TrueBIM IsoFieldRebar";
    private const int SignatureByteCount = 12;

    public IsoFieldRebarChangePlan Build(
        IReadOnlyList<IsoFieldRebarPlanItem> plannedItems,
        IReadOnlyList<IsoFieldOwnedRebarSnapshot> existingElements)
    {
        if (plannedItems is null)
        {
            throw new ArgumentNullException(nameof(plannedItems));
        }

        if (existingElements is null)
        {
            throw new ArgumentNullException(nameof(existingElements));
        }

        string existingStateFingerprint = BuildExistingStateFingerprint(existingElements);

        List<string> diagnostics = new();
        foreach (IGrouping<string, IsoFieldRebarPlanItem> duplicate in plannedItems
            .Where(item => !string.IsNullOrWhiteSpace(item.StableId))
            .GroupBy(item => item.StableId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add($"План содержит повторяющийся стабильный id: {duplicate.Key}.");
        }

        if (plannedItems.Any(item => string.IsNullOrWhiteSpace(item.StableId)
            || string.IsNullOrWhiteSpace(item.Signature)))
        {
            diagnostics.Add("У каждой плановой линии должны быть стабильный id и сигнатура.");
        }

        if (diagnostics.Count > 0)
        {
            return new IsoFieldRebarChangePlan(
                Array.Empty<IsoFieldRebarChange>(),
                diagnostics,
                existingStateFingerprint);
        }

        Dictionary<string, IsoFieldOwnedRebarSnapshot[]> existingByStableId = existingElements
            .Where(item => !string.IsNullOrWhiteSpace(item.StableId))
            .GroupBy(item => item.StableId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(item => item.ElementId).ToArray(),
                StringComparer.Ordinal);
        HashSet<string> plannedStableIds = plannedItems
            .Select(item => item.StableId)
            .ToHashSet(StringComparer.Ordinal);
        List<IsoFieldRebarChange> changes = new(plannedItems.Count + existingByStableId.Count);

        foreach (IsoFieldRebarPlanItem planned in plannedItems.OrderBy(item => item.StableId, StringComparer.Ordinal))
        {
            if (!existingByStableId.TryGetValue(planned.StableId, out IsoFieldOwnedRebarSnapshot[]? existing))
            {
                changes.Add(new IsoFieldRebarChange(
                    IsoFieldRebarChangeKind.Add,
                    planned.StableId,
                    planned,
                    Array.Empty<long>()));
                continue;
            }

            bool unchanged = existing.Length == 1
                && string.Equals(existing[0].Signature, planned.Signature, StringComparison.Ordinal);
            changes.Add(new IsoFieldRebarChange(
                unchanged ? IsoFieldRebarChangeKind.Unchanged : IsoFieldRebarChangeKind.Update,
                planned.StableId,
                planned,
                existing.Select(item => item.ElementId).ToArray()));
        }

        foreach (KeyValuePair<string, IsoFieldOwnedRebarSnapshot[]> obsoletePair in existingByStableId
            .Where(pair => !plannedStableIds.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            changes.Add(new IsoFieldRebarChange(
                IsoFieldRebarChangeKind.Delete,
                obsoletePair.Key,
                null,
                obsoletePair.Value.Select(item => item.ElementId).ToArray()));
        }

        return new IsoFieldRebarChangePlan(
            changes,
            Array.Empty<string>(),
            existingStateFingerprint);
    }

    public string BuildSignature(IsoFieldRebarPlacement placement)
    {
        if (placement is null)
        {
            throw new ArgumentNullException(nameof(placement));
        }

        if (string.IsNullOrWhiteSpace(placement.StableId) || placement.Component is null)
        {
            throw new InvalidOperationException("Для инженерной линии нужны стабильный id и компонент армирования.");
        }

        string value = string.Join(
            "|",
            placement.StableId,
            Format(placement.Start.XFeet),
            Format(placement.Start.YFeet),
            Format(placement.Start.ZFeet),
            Format(placement.End.XFeet),
            Format(placement.End.YFeet),
            Format(placement.End.ZFeet),
            Format(placement.Normal.XFeet),
            Format(placement.Normal.YFeet),
            Format(placement.Normal.ZFeet),
            Format(placement.Component.DiameterMillimeters),
            Format(placement.Component.SpacingMillimeters),
            placement.Rule.LayerRole,
            placement.Rule.Face,
            placement.Rule.PlacementDirection);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash
            .Take(SignatureByteCount)
            .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public string BuildFingerprint(IsoFieldRebarChangePlan plan)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        IEnumerable<string> fingerprintParts = new[]
        {
            "existing-state|" + (plan.ExistingStateFingerprint ?? string.Empty)
        }
            .Concat(plan.Diagnostics
                .OrderBy(item => item, StringComparer.Ordinal)
                .Select(item => "diagnostic|" + item))
            .Concat(plan.Changes
                .OrderBy(change => change.StableId, StringComparer.Ordinal)
                .ThenBy(change => change.Kind)
                .Select(change => string.Join(
                    "|",
                    change.Kind,
                    change.StableId,
                    change.PlannedItem?.Signature ?? string.Empty,
                    string.Join(",", change.ExistingElementIds.OrderBy(id => id)))));
        string value = string.Join("\n", fingerprintParts);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash
            .Take(SignatureByteCount)
            .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }

    public bool TryParseOwnedComment(
        long elementId,
        string? comment,
        out IsoFieldOwnedRebarSnapshot? snapshot)
    {
        snapshot = null;
        if (comment is null
            || string.IsNullOrWhiteSpace(comment)
            || !comment.StartsWith(OwnedCommentPrefix + ";", StringComparison.Ordinal))
        {
            return false;
        }

        string? stableId = ReadMetadataValue(comment, "id");
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return false;
        }

        snapshot = new IsoFieldOwnedRebarSnapshot(
            elementId,
            stableId!,
            ReadMetadataValue(comment, "sig"));
        return true;
    }

    private static string? ReadMetadataValue(string comment, string key)
    {
        string prefix = key + "=";
        string? metadata = comment
            .Split(';')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith(prefix, StringComparison.Ordinal));
        return metadata is null
            ? null
            : metadata.Substring(prefix.Length).Trim();
    }

    private static string Format(double value)
    {
        return value.ToString("0.#########", CultureInfo.InvariantCulture);
    }

    private static string BuildExistingStateFingerprint(
        IReadOnlyList<IsoFieldOwnedRebarSnapshot> existingElements)
    {
        string value = string.Join(
            "\n",
            existingElements
                .OrderBy(item => item.ElementId)
                .ThenBy(item => item.StableId, StringComparer.Ordinal)
                .Select(item => string.Join(
                    "|",
                    item.ElementId,
                    item.StableId,
                    item.Signature ?? string.Empty,
                    item.StateSignature ?? string.Empty)));
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return string.Concat(hash
            .Take(SignatureByteCount)
            .Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
