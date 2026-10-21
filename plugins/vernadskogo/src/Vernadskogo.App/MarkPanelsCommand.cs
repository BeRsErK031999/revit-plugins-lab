using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Vernadskogo.App.Core;
using Vernadskogo.App.Revit;
using Vernadskogo.App.UI;

namespace Vernadskogo.App;

[Transaction(TransactionMode.Manual)]
public sealed class MarkPanelsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document == null || document.IsFamilyDocument) return Result.Cancelled;
        try
        {
            string building = PanelNumbering.ParseBuilding(document.Title);
            PanelData data = PanelReader.Read(document);
            if (data.Panels.Count == 0)
            {
                TaskDialog.Show("Вернадского", "В модели не найдены панели категории «Стены» с параметрами типа\n"
                    + "«Модель» = «Фасад» и «Описание» = «Стеновая панель».");
                return Result.Cancelled;
            }
            var window = new MarkingWindow(data, building, SettingsStorage.Read(document));
            new WindowInteropHelper(window) { Owner = commandData.Application.MainWindowHandle };
            if (window.ShowDialog() != true) return Result.Cancelled;
            var marks = PanelNumbering.CreateMarks(data.Panels, building, window.Numbers, window.Direction);
            PanelWriter.Write(document, marks, window.Numbers, window.Direction);
            TaskDialog.Show("Вернадского", "Маркировка завершена.\nКорпус: К" + building
                + "\nПанелей: " + marks.Count + "\nЭтажей: " + data.Panels.Select(p => p.Floor).Distinct().Count());
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            message = "Маркировка не выполнена. Изменения этого запуска не сохранены.\n\n" + exception.Message;
            return Result.Failed;
        }
    }
}
