using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelAssemblyViewCreationService
{
    private const int ViewScale = 10;
    private const AssemblyDetailViewOrientation ViewOrientation = AssemblyDetailViewOrientation.ElevationLeft;
    private readonly ITrueBimLogger logger;
    private readonly LintelAssemblyViewAnnotationService annotationService;
    private readonly LintelTypeImageService typeImageService;

    public LintelAssemblyViewCreationService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        annotationService = new LintelAssemblyViewAnnotationService(this.logger);
        typeImageService = new LintelTypeImageService(this.logger);
    }

    public LintelAssemblyViewCreationResult CreateOne(
        Document document,
        string assemblyName,
        string viewName,
        string frameFamilyFilePath,
        long? lintelTypeId = null)
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

        if (string.IsNullOrWhiteSpace(frameFamilyFilePath))
        {
            throw new ArgumentException("Frame family file path is required.", nameof(frameFamilyFilePath));
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
        long? resolvedTypeId = ResolveLintelTypeId(lintelTypeId, assemblyName);
        if (sameNameView is not null)
        {
            bool belongsToAssembly = sameNameView.AssociatedAssemblyInstanceId == assembly.Id;
            if (belongsToAssembly && sameNameView is ViewSection existingView)
            {
                LintelAssemblyViewFormattingResult formatting = TryApplyFormatting(
                    document,
                    existingView,
                    assembly,
                    frameFamilyFilePath);
                LintelTypeImageResult typeImage = TryExportAndAssignTypeImage(
                    document,
                    existingView,
                    resolvedTypeId,
                    assemblyName);
                return CreateResult(
                    LintelAssemblyViewCreationStatus.AlreadyExists,
                    assemblyName,
                    viewName,
                    RevitElementIds.GetValue(sameNameView.Id),
                    formatting.ModelChanged || typeImage.ModelChanged
                        ? "Боковой вид уже существовал; оформление и изображение типоразмера обновлены без создания дубликата вида."
                        : "Боковой вид уже существовал; оформление или изображение не обновлены — проверьте предупреждения ниже.",
                    formatting,
                    typeImage);
            }

            return CreateResult(
                LintelAssemblyViewCreationStatus.Blocked,
                assemblyName,
                viewName,
                RevitElementIds.GetValue(sameNameView.Id),
                belongsToAssembly
                    ? "Существующий вид сборки имеет неподдерживаемый тип; базовое оформление не применялось."
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
            LintelAssemblyViewFormattingResult formatting = TryApplyFormatting(
                document,
                view,
                assembly,
                frameFamilyFilePath);
            LintelTypeImageResult typeImage = TryExportAndAssignTypeImage(
                document,
                view,
                resolvedTypeId,
                assemblyName);
            logger.Info(
                $"Lintels side assembly view created. Assembly='{assemblyName}'; View='{viewName}'; ElementId={viewId}; Orientation={ViewOrientation}; Scale=1:{ViewScale}.");
            return CreateResult(
                LintelAssemblyViewCreationStatus.Created,
                assemblyName,
                viewName,
                viewId,
                $"Ориентация: слева ({ViewOrientation}); масштаб 1:{ViewScale}; уровень детализации — высокий; стиль — скрытая линия.",
                formatting,
                typeImage);
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
        string message,
        LintelAssemblyViewFormattingResult? formatting = null,
        LintelTypeImageResult? typeImage = null)
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
            message,
            formatting,
            typeImage);
    }

    private LintelAssemblyViewFormattingResult TryApplyFormatting(
        Document document,
        ViewSection view,
        AssemblyInstance assembly,
        string frameFamilyFilePath)
    {
        try
        {
            return annotationService.Apply(
                document,
                view,
                assembly,
                frameFamilyFilePath);
        }
        catch (Exception exception)
        {
            logger.Error(
                $"Failed to format Lintels side assembly view '{view.Name}' for '{assembly.AssemblyTypeName}'.",
                exception);
            return LintelAssemblyViewFormattingResult.Failed(
                "Оформление вида отменено целиком; геометрия Assembly и сам вид сохранены, подробности записаны в лог.");
        }
    }

    private LintelTypeImageResult TryExportAndAssignTypeImage(
        Document document,
        ViewSection view,
        long? lintelTypeId,
        string assemblyName)
    {
        try
        {
            return typeImageService.ExportAndAssign(
                document,
                view,
                lintelTypeId,
                $"{assemblyName}.png");
        }
        catch (Exception exception)
        {
            logger.Error(
                $"Failed to export or assign Lintels type image for view '{view.Name}'.",
                exception);
            return LintelTypeImageResult.Failed(
                "Экспорт PNG и назначение параметра «Изображение типоразмера» завершились с ошибкой; подробности записаны в лог.");
        }
    }

    private static long? ResolveLintelTypeId(long? lintelTypeId, string assemblyName)
    {
        if (lintelTypeId is > 0)
        {
            return lintelTypeId;
        }

        return LintelArtifactNameBuilder.TryExtractTypeId(assemblyName, out long parsedTypeId)
            ? parsedTypeId
            : null;
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
