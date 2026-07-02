using Autodesk.Revit.DB;

namespace TrueBIM.App.Services;

internal static class RevitElementIds
{
    public static long GetValue(ElementId elementId)
    {
#if REVIT2024_OR_GREATER
        return elementId.Value;
#else
        return elementId.IntegerValue;
#endif
    }

    public static ElementId Create(long value)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(value);
#else
        return new ElementId(checked((int)value));
#endif
    }
}
