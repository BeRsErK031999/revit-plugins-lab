using Autodesk.Revit.DB;

namespace TrueBIM.App.Services;

internal static class RevitElementIds
{
    public static long GetValue(ElementId elementId)
    {
#if NET48
        return elementId.IntegerValue;
#else
        return elementId.Value;
#endif
    }

    public static ElementId Create(long value)
    {
#if NET48
        return new ElementId(checked((int)value));
#else
        return new ElementId(value);
#endif
    }
}
