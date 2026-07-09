using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public enum DatumExtentMode
{
    ViewSpecific,
    Model,
    Invert
}

public static class DatumExtentModes
{
    public static string GetDisplayName(DatumExtentMode mode)
    {
        return mode switch
        {
            DatumExtentMode.Model => "3D модельные экстенты",
            DatumExtentMode.Invert => "Инвертировать режим осей",
            _ => "2D на активном виде"
        };
    }

    public static DatumExtentType GetTargetType(DatumExtentMode mode, DatumExtentType currentType)
    {
        return mode switch
        {
            DatumExtentMode.Model => DatumExtentType.Model,
            DatumExtentMode.Invert => currentType == DatumExtentType.Model
                ? DatumExtentType.ViewSpecific
                : DatumExtentType.Model,
            _ => DatumExtentType.ViewSpecific
        };
    }
}
