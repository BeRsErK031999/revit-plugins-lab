namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;

public enum FamilyReplacementParameterStorage
{
    String,
    Integer,
    Double,
    ElementId
}

/// <summary>Идентификаторы и семантический тип параметра экземпляра без зависимости от Revit API.</summary>
public sealed record FamilyReplacementParameterDefinition(
    string Name,
    Guid? SharedGuid,
    int? BuiltInId,
    long? ProjectDefinitionId,
    FamilyReplacementParameterStorage Storage,
    string DataType);

public sealed record FamilyReplacementParameterMatch(int? TargetIndex, string Reason)
{
    public bool Succeeded => TargetIndex.HasValue;
}

/// <summary>Значение в единицах хранения Revit; нулевые числа остаются значимыми значениями.</summary>
public sealed record FamilyReplacementParameterValue(
    FamilyReplacementParameterStorage Storage,
    string Text = "",
    long Integer = 0,
    double Number = 0)
{
    public bool IsMeaningful => Storage switch
    {
        FamilyReplacementParameterStorage.String => !string.IsNullOrEmpty(Text),
        FamilyReplacementParameterStorage.ElementId => Integer != -1,
        _ => true
    };
}
