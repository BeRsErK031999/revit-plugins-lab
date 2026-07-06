using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Services;

internal static class ScheduleActiveViewTracker
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ElementId> LastActivatedViewIdsByDocument = new(StringComparer.OrdinalIgnoreCase);

    public static void CaptureActivatedView(View? view)
    {
        if (view is null || view.IsTemplate)
        {
            return;
        }

        string documentKey = CreateDocumentKey(view.Document);
        lock (Sync)
        {
            LastActivatedViewIdsByDocument[documentKey] = view.Id;
        }
    }

    public static ViewSchedule? GetLastActivatedSchedule(UIDocument uiDocument)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));

        Document document = uiDocument.Document;
        string documentKey = CreateDocumentKey(document);
        ElementId? viewId;
        lock (Sync)
        {
            if (!LastActivatedViewIdsByDocument.TryGetValue(documentKey, out viewId))
            {
                return null;
            }
        }

        ViewSchedule? schedule = document.GetElement(viewId) as ViewSchedule;
        if (schedule is not null && !schedule.IsTemplate)
        {
            return schedule;
        }

        lock (Sync)
        {
            LastActivatedViewIdsByDocument.Remove(documentKey);
        }

        return null;
    }

    public static void Clear()
    {
        lock (Sync)
        {
            LastActivatedViewIdsByDocument.Clear();
        }
    }

    private static string CreateDocumentKey(Document document)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                return document.PathName;
            }
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException)
        {
        }

        return $"{document.Title}:{document.GetHashCode()}";
    }
}
