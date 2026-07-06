using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public static class ParameterStorageTypeFormatter
{
    public static string Format(StorageType storageType)
    {
        return storageType switch
        {
            StorageType.String => "Текст",
            StorageType.Integer => "Целое / Да-Нет",
            StorageType.Double => "Число",
            StorageType.ElementId => "ElementId",
            StorageType.None => "Нет значения",
            _ => storageType.ToString()
        };
    }
}
