using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Vernadskogo.App;

public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        const string tab = "Вернадского";
        try { application.CreateRibbonTab(tab); }
        catch (Autodesk.Revit.Exceptions.ArgumentException) { /* The product tab already exists. */ }
        RibbonPanel panel = application.GetRibbonPanels(tab).FirstOrDefault(p => p.Name == "АР")
            ?? application.CreateRibbonPanel(tab, "АР");
        var data = new PushButtonData("Vernadskogo.MarkPanels", "Маркировка\nпанелей",
            Assembly.GetExecutingAssembly().Location, typeof(MarkPanelsCommand).FullName)
        {
            AvailabilityClassName = typeof(ProjectAvailability).FullName,
            ToolTip = "Заполнить секцию, этаж и марки фасадных стеновых панелей.",
            LongDescription = "Нумерация по этажам против часовой стрелки с выбором начального направления и номеров материалов.",
            LargeImage = CreateIcon()
        };
        panel.AddItem(data);
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

    private static ImageSource CreateIcon()
    {
        var drawing = new DrawingGroup();
        using (DrawingContext context = drawing.Open())
        {
            var outline = new Pen(Brushes.White, 1);
            var fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 105, 144));
            context.DrawRoundedRectangle(fill, null, new Rect(0, 0, 32, 32), 4, 4);
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    context.DrawRectangle(y == 2 && x == 0 ? Brushes.Gold : fill, outline,
                        new Rect(5 + x * 7, 5 + y * 7, 7, 7));
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}

public sealed class ProjectAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) =>
        applicationData.ActiveUIDocument?.Document.IsFamilyDocument == false;
}
