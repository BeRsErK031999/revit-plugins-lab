using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;

public static class FamilyReplacementParameterPolicy
{
    public static FamilyReplacementParameterMatch Match(
        FamilyReplacementParameterDefinition source,
        IReadOnlyList<FamilyReplacementParameterDefinition> targets)
    {
        if (source.SharedGuid is Guid sharedGuid)
        {
            return MatchIdentity(source, targets, target => target.SharedGuid == sharedGuid);
        }

        if (source.BuiltInId is int builtInId)
        {
            return MatchIdentity(source, targets, target => target.SharedGuid is null && target.BuiltInId == builtInId);
        }

        if (source.ProjectDefinitionId is long definitionId)
        {
            FamilyReplacementParameterMatch projectMatch = MatchIdentity(
                source,
                targets,
                target => target.SharedGuid is null && target.BuiltInId is null && target.ProjectDefinitionId == definitionId);
            if (projectMatch.Succeeded || targets.Any(target => target.ProjectDefinitionId == definitionId))
            {
                return projectMatch;
            }
        }

        if (string.IsNullOrEmpty(source.DataType))
        {
            return new FamilyReplacementParameterMatch(null, "невозможно подтвердить тип данных для сопоставления по имени");
        }

        return MatchIdentity(
            source,
            targets,
            target => target.SharedGuid is null
                && target.BuiltInId is null
                && string.Equals(source.Name, target.Name, StringComparison.Ordinal)
                && Compatible(source, target));
    }

    public static bool ValuesEqual(FamilyReplacementParameterValue expected, FamilyReplacementParameterValue actual)
    {
        if (expected.Storage != actual.Storage)
        {
            return false;
        }

        return expected.Storage switch
        {
            FamilyReplacementParameterStorage.String => string.Equals(expected.Text, actual.Text, StringComparison.Ordinal),
            FamilyReplacementParameterStorage.Integer or FamilyReplacementParameterStorage.ElementId => expected.Integer == actual.Integer,
            FamilyReplacementParameterStorage.Double => Math.Abs(expected.Number - actual.Number)
                <= Math.Max(1e-9, Math.Max(Math.Abs(expected.Number), Math.Abs(actual.Number)) * 1e-12),
            _ => false
        };
    }

    private static FamilyReplacementParameterMatch MatchIdentity(
        FamilyReplacementParameterDefinition source,
        IReadOnlyList<FamilyReplacementParameterDefinition> targets,
        Func<FamilyReplacementParameterDefinition, bool> matches)
    {
        List<int> indexes = Enumerable.Range(0, targets.Count).Where(index => matches(targets[index])).ToList();
        if (indexes.Count == 0)
        {
            return new FamilyReplacementParameterMatch(null, "у нового экземпляра нет соответствующего параметра");
        }

        if (indexes.Count != 1)
        {
            return new FamilyReplacementParameterMatch(null, "найдено несколько подходящих параметров; однозначное сопоставление невозможно");
        }

        int index = indexes[0];
        return Compatible(source, targets[index])
            ? new FamilyReplacementParameterMatch(index, string.Empty)
            : new FamilyReplacementParameterMatch(null, "не совпадают тип хранения или физическая величина параметра");
    }

    private static bool Compatible(FamilyReplacementParameterDefinition source, FamilyReplacementParameterDefinition target)
    {
        return source.Storage == target.Storage && string.Equals(source.DataType, target.DataType, StringComparison.Ordinal);
    }
}
