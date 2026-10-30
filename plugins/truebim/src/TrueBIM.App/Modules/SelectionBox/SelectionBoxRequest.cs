using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace TrueBIM.App.Modules.SelectionBox;

/// <summary>Lets Revit itself create its reserved unnamed/default view, then applies the captured bounds.</summary>
public sealed class SelectionBoxRequest
{
    private static SelectionBoxRequest? pending;
    private readonly UIApplication application;
    private readonly Document document;
    private readonly IReadOnlyList<XYZ> points;
    private readonly string defaultName;
    private readonly DateTime started = DateTime.UtcNow;

    private SelectionBoxRequest(UIApplication application, Document document, IReadOnlyList<XYZ> points)
    {
        this.application = application;
        this.document = document;
        this.points = points;
        defaultName = SelectionBoxViews.DefaultName(document);
    }

    public static bool IsPending => pending != null;

    public static void Start(UIApplication application)
    {
        if (pending != null)
            throw new InvalidOperationException("Дождитесь завершения открытия 3D-вида.");
        UIDocument ui = application.ActiveUIDocument
            ?? throw new InvalidOperationException("Откройте проект и выберите элементы модели.");
        Document document = ui.Document;
        if (document.IsFamilyDocument || document.IsReadOnly)
            throw new InvalidOperationException("Команда работает в доступном для редактирования проекте Revit.");
        IReadOnlyList<XYZ> points = SelectionBoxBounds.Resolve(document, SelectionBoxService.ReadSelection(ui));
        View3D? target = SelectionBoxViews.Find(ui);
        if (target != null)
        {
            SelectionBoxService.Apply(ui, target, points, activate: true);
            return;
        }

        RevitCommandId command = RevitCommandId.LookupPostableCommandId(PostableCommand.Default3DView);
        if (!application.CanPostCommand(command))
            throw new InvalidOperationException("Revit сейчас не может создать стандартный 3D-вид. Завершите текущую операцию и повторите команду.");
        SelectionBoxRequest request = new(application, document, points);
        pending = request;
        application.Idling += request.OnIdling;
        try { application.PostCommand(command); }
        catch { request.Dispose(); throw; }
    }

    public static void CancelPending() => pending?.Dispose();

    private void Dispose()
    {
        application.Idling -= OnIdling;
        if (ReferenceEquals(pending, this)) pending = null;
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        try
        {
            UIDocument? ui = application.ActiveUIDocument;
            if (!document.IsValidObject || ui == null || !document.Equals(ui.Document))
            {
                Dispose();
                return;
            }
            if (ui.ActiveView is View3D target && target.Name.Equals(defaultName, StringComparison.OrdinalIgnoreCase)
                && !document.IsModifiable && !document.IsReadOnly)
            {
                Dispose();
                SelectionBoxService.Apply(ui, target, points, activate: false);
                return;
            }
            // Check the ready view before the timeout: loading a large default 3D view may take time.
            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(30))
            {
                Dispose();
                TaskDialog.Show("Рамка выбора", "Revit не открыл стандартный 3D-вид. Откройте нужный 3D-вид и повторите команду.");
            }
        }
        catch (Exception exception)
        {
            Dispose();
            TaskDialog.Show("Рамка выбора", "Не удалось ограничить созданный стандартный 3D-вид.\n\n" + exception.Message);
        }
    }
}
