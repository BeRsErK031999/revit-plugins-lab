using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TrueBIM.App.Modules.SelectionBox;

public static class SelectionBoxService
{
    public static IList<Reference> ReadSelection(UIDocument uiDocument)
    {
#if REVIT2023_OR_GREATER
        // GetElementIds loses the selected element inside a link, including mixed selections.
        return uiDocument.Selection.GetReferences();
#else
        throw new InvalidOperationException("Предварительный выбор элементов внутри связей доступен этой команде начиная с Revit 2023. В Revit 2019–2022 API не передаёт такой выбор плагину.");
#endif
    }

    public static View3D Execute(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        if (document.IsFamilyDocument || document.IsReadOnly)
            throw new InvalidOperationException("Команда работает в доступном для редактирования проекте Revit.");

        // Resolve the complete selection before changing views or starting a transaction.
        IReadOnlyList<XYZ> points = SelectionBoxBounds.Resolve(document, ReadSelection(uiDocument));
        View3D target = SelectionBoxViews.Find(uiDocument)
            ?? throw new InvalidOperationException("Стандартный 3D-вид ещё не создан. Запустите кнопку «Рамка выбора».");
        return Apply(uiDocument, target, points, activate: true);
    }

    public static View3D Apply(UIDocument uiDocument, View3D target, IReadOnlyList<XYZ> points, bool activate)
    {
        Document document = uiDocument.Document;
        using TransactionGroup group = new(document, "TrueBIM: рамка выбора");
        group.Start();
        using (Transaction transaction = new(document, "Ограничить 3D-вид выбранными элементами"))
        {
            transaction.Start();
            Transform orientation = target.GetSectionBox().Transform;
            target.IsSectionBoxActive = true;
            target.SetSectionBox(SelectionBoxBounds.Enclose(points, orientation));
            document.Regenerate();
            if (!target.IsSectionBoxActive)
                throw new InvalidOperationException("Не удалось включить рамку: проверьте управление параметром «Рамка 3D-вида» в шаблоне вида.");
            if (transaction.Commit() != TransactionStatus.Committed)
                throw new InvalidOperationException("Revit не смог изменить рамку 3D-вида. Проверьте доступность вида для редактирования и его шаблон.");
        }

        // During Idling the native default-view command has already activated the target.
        // Synchronous activation is only used from an external command.
        if (activate && uiDocument.ActiveView.Id != target.Id) uiDocument.ActiveView = target;
        uiDocument.RefreshActiveView();
        if (group.Assimilate() != TransactionStatus.Committed)
            throw new InvalidOperationException("Revit отменил изменение рамки 3D-вида.");
        SelectionBoxViews.Record(target);
        return target;
    }
}
