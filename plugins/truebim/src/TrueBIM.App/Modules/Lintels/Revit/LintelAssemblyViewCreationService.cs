using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelAssemblyViewCreationService
{
    private const int ViewScale = 10;
    private const AssemblyDetailViewOrientation ViewOrientation = AssemblyDetailViewOrientation.ElevationLeft;
    private readonly ITrueBimLogger logger;

    public LintelAssemblyViewCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelAssemblyViewCreationResult CreateOne(
        Document document,
        string assemblyName,
        string viewName)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            throw new ArgumentException("Assembly name is required.", nameof(assemblyName));
        }

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name is required.", nameof(viewName));
        }

        AssemblyInstance? assembly = FindAssembly(document, assemblyName);
        if (assembly is null)
        {
            return CreateResult(
                LintelAssemblyViewCreationStatus.Blocked,
                assemblyName,
                viewName,
                null,
                "Сборка с указанным TrueBIM-именем не найдена. Сначала создайте одну сборку перемычки.");
        }

        View? sameNameView = FindViewByName(document, viewName);
        if (sameNameView is not null)
        {
            bool belongsToAssembly = sameNameView.AssociatedAssemblyInstanceId == assembly.Id;
            return CreateResult(
                belongsToAssembly
                    ? LintelAssemblyViewCreationStatus.AlreadyExists
                    : LintelAssemblyViewCreationStatus.Blocked,
                assemblyName,
                viewName,
                RevitElementIds.GetValue(sameNameView.Id),
                belongsToAssembly
                    ? "Повторный запуск не изменил модель: этот боковой вид уже принадлежит выбранной сборке."
                    : "В проекте уже есть вид с таким именем, но он принадлежит другой сборке.");
        }

        if (document.IsReadOnly)
        {
            return CreateResult(
                LintelAssemblyViewCreationStatus.Blocked,
                assemblyName,
                viewName,
                null,
                "Документ Revit доступен только для чтения; боковой вид не создавался.");
        }

        if (!assembly.AllowsAssemblyViewCreation())
        {
            return CreateResult(
                LintelAssemblyViewCreationStatus.Blocked,
                assemblyName,
                viewName,
                null,
                "Revit не разрешает создать вид для этой сборки: проверьте существующие виды других экземпляров того же типа Assembly.");
        }

        using Transaction transaction = new(document, "TrueBIM: боковой вид перемычки 1:10");
        try
        {
            EnsureStatus(
                transaction.Start(),
                TransactionStatus.Started,
                "Revit не начал транзакцию создания бокового вида.");

            ViewSection view = AssemblyViewUtils.CreateDetailSection(
                document,
                assembly.Id,
                ViewOrientation);
            document.Regenerate();
            view.Name = viewName;
            view.Scale = ViewScale;
            view.DetailLevel = ViewDetailLevel.Fine;
            view.DisplayStyle = DisplayStyle.HLR;
            view.CropBoxActive = true;
            view.CropBoxVisible = false;

            EnsureStatus(
                transaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил транзакцию создания бокового вида.");

            long viewId = RevitElementIds.GetValue(view.Id);
            logger.Info(
                $"Lintels side assembly view created. Assembly='{assemblyName}'; View='{viewName}'; ElementId={viewId}; Orientation={ViewOrientation}; Scale=1:{ViewScale}.");
            return CreateResult(
                LintelAssemblyViewCreationStatus.Created,
                assemblyName,
                viewName,
                viewId,
                $"Ориентация: слева ({ViewOrientation}); масштаб 1:{ViewScale}; уровень детализации — высокий; стиль — скрытая линия.");
        }
        catch (Exception exception)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                try
                {
                    transaction.RollBack();
                }
                catch (Exception rollbackException)
                {
                    logger.Error("Failed to roll back Lintels side view transaction.", rollbackException);
                }
            }

            logger.Error(
                $"Failed to create Lintels side assembly view '{viewName}' for '{assemblyName}'.",
                exception);
            return CreateResult(
                LintelAssemblyViewCreationStatus.Failed,
                assemblyName,
                viewName,
                null,
                "Создание бокового вида отменено целиком; подробности записаны в лог.");
        }
    }

    private static AssemblyInstance? FindAssembly(Document document, string assemblyName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(AssemblyInstance))
            .Cast<AssemblyInstance>()
            .FirstOrDefault(assembly => string.Equals(
                assembly.AssemblyTypeName,
                assemblyName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private static View? FindViewByName(Document document, string viewName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(view => !view.IsTemplate && string.Equals(
                view.Name,
                viewName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private LintelAssemblyViewCreationResult CreateResult(
        LintelAssemblyViewCreationStatus status,
        string assemblyName,
        string viewName,
        long? viewElementId,
        string message)
    {
        if (status is LintelAssemblyViewCreationStatus.Blocked or LintelAssemblyViewCreationStatus.Failed)
        {
            logger.Warning(
                $"Lintels side assembly view not created. Status={status}; Assembly='{assemblyName}'; View='{viewName}'; Reason='{message}'.");
        }

        return new LintelAssemblyViewCreationResult(
            status,
            assemblyName,
            viewName,
            viewElementId,
            message);
    }

    private static void EnsureStatus(
        TransactionStatus actual,
        TransactionStatus expected,
        string message)
    {
        if (actual != expected)
        {
            throw new InvalidOperationException($"{message} Status={actual}.");
        }
    }
}
