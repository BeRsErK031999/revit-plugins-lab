using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class ScheduleRegisterTemplateRestoreService
{
    private readonly ScheduleRegisterTemplateInspector inspector;
    private readonly ITrueBimLogger logger;

    public ScheduleRegisterTemplateRestoreService(
        ScheduleRegisterTemplateInspector inspector,
        ITrueBimLogger logger)
    {
        this.inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ViewSchedule Restore(Application application, Document targetDocument, string sourcePath)
    {
        Guard.NotNull(application, nameof(application));
        Guard.NotNull(targetDocument, nameof(targetDocument));
        string normalizedPath = Path.GetFullPath(sourcePath ?? string.Empty);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("Файл эталонного шаблона не найден.", normalizedPath);
        }

        Document? sourceDocument = null;
        bool ownsSourceDocument = false;
        try
        {
            sourceDocument = application.Documents
                .Cast<Document>()
                .FirstOrDefault(document =>
                    !string.IsNullOrWhiteSpace(document.PathName)
                    && string.Equals(
                        Path.GetFullPath(document.PathName),
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase));
            if (ReferenceEquals(sourceDocument, targetDocument))
            {
                throw new InvalidOperationException(
                    "Текущий проект нельзя использовать как эталон для восстановления собственной шаблонной спецификации.");
            }

            if (sourceDocument is null)
            {
                sourceDocument = application.OpenDocumentFile(normalizedPath);
                ownsSourceDocument = true;
            }

            ViewSchedule? sourceSchedule = inspector.FindTemplate(sourceDocument);
            if (sourceSchedule is null)
            {
                throw new InvalidOperationException(
                    $"В файле «{normalizedPath}» не найдена спецификация «{ScheduleRegisterConstants.TemplateScheduleName}».");
            }

            ScheduleRegisterTemplateValidation sourceValidation = inspector.Validate(sourceSchedule);
            if (!sourceValidation.IsValid)
            {
                throw new InvalidOperationException(
                    "Эталонная спецификация в выбранном файле повреждена: "
                    + string.Join(" ", sourceValidation.Issues));
            }

            using Transaction transaction = new(targetDocument, "TrueBIM: восстановить шаблон ведомости спецификаций");
            transaction.Start();
            ViewSchedule? existing = inspector.FindTemplate(targetDocument);
            if (existing is not null)
            {
                targetDocument.Delete(existing.Id);
            }

            CopyPasteOptions options = new();
            options.SetDuplicateTypeNamesHandler(new UseDestinationTypesHandler());
            ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElements(
                sourceDocument,
                [sourceSchedule.Id],
                targetDocument,
                Transform.Identity,
                options);
            ViewSchedule? restored = copiedIds
                .Select(targetDocument.GetElement)
                .OfType<ViewSchedule>()
                .FirstOrDefault();
            if (restored is null)
            {
                transaction.RollBack();
                throw new InvalidOperationException("Revit не вернул скопированную спецификацию.");
            }

            restored.Name = ScheduleRegisterConstants.TemplateScheduleName;
            ScheduleRegisterTemplateValidation restoredValidation = inspector.Validate(restored);
            if (!restoredValidation.IsValid)
            {
                transaction.RollBack();
                throw new InvalidOperationException(
                    "Скопированная спецификация не прошла контрольную проверку: "
                    + string.Join(" ", restoredValidation.Issues));
            }

            transaction.Commit();
            logger.Info(
                $"Schedule Register restored template schedule from '{normalizedPath}' into '{targetDocument.Title}'.");
            return restored;
        }
        finally
        {
            if (ownsSourceDocument && sourceDocument is not null && sourceDocument.IsValidObject)
            {
                try
                {
                    sourceDocument.Close(false);
                }
                catch (Exception exception)
                {
                    logger.Warning(
                        $"Failed to close Schedule Register source template '{normalizedPath}': {exception.Message}");
                }
            }
        }
    }

    private sealed class UseDestinationTypesHandler : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
        {
            return DuplicateTypeAction.UseDestinationTypes;
        }
    }
}
