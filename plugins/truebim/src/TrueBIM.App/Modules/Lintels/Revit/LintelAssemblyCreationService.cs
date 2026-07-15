using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Lintels.Revit;

public sealed class LintelAssemblyCreationService
{
    private readonly LintelAssemblyPreflightService preflightService;
    private readonly ITrueBimLogger logger;

    public LintelAssemblyCreationService(
        LintelAssemblyPreflightService preflightService,
        ITrueBimLogger logger)
    {
        this.preflightService = preflightService ?? throw new ArgumentNullException(nameof(preflightService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public LintelAssemblyCreationResult CreateOne(
        Document document,
        LintelTypeDiagnostic selectedType)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (selectedType is null)
        {
            throw new ArgumentNullException(nameof(selectedType));
        }

        LintelAssemblyPreflightItem preflight = preflightService
            .Inspect(document, [selectedType])
            .Items
            .Single();
        if (preflight.Status == LintelAssemblyPreflightStatus.AlreadyExists)
        {
            return new LintelAssemblyCreationResult(
                LintelAssemblyCreationStatus.AlreadyExists,
                preflight.AssemblyName,
                FindExistingAssemblyId(document, preflight.AssemblyName),
                "Повторный запуск ничего не изменил в модели.");
        }

        if (preflight.Status != LintelAssemblyPreflightStatus.Ready
            || preflight.NamingCategoryId is null)
        {
            return new LintelAssemblyCreationResult(
                LintelAssemblyCreationStatus.Blocked,
                preflight.AssemblyName,
                null,
                preflight.Message);
        }

        if (document.IsReadOnly)
        {
            return new LintelAssemblyCreationResult(
                LintelAssemblyCreationStatus.Blocked,
                preflight.AssemblyName,
                null,
                "Документ Revit доступен только для чтения; сборка не создавалась.");
        }

        using TransactionGroup group = new(document, "TrueBIM: перемычка — одна сборка");
        bool groupStarted = false;
        try
        {
            EnsureStatus(
                group.Start(),
                TransactionStatus.Started,
                "Revit не начал группу транзакций создания сборки.");
            groupStarted = true;

            AssemblyInstance assembly = CreateAssembly(document, preflight);
            AssignAssemblyName(document, assembly, preflight.AssemblyName);

            EnsureStatus(
                group.Assimilate(),
                TransactionStatus.Committed,
                "Revit не объединил транзакции создания и именования сборки.");
            groupStarted = false;

            long assemblyId = RevitElementIds.GetValue(assembly.Id);
            logger.Info(
                $"Lintels assembly created. Assembly='{preflight.AssemblyName}'; ElementId={assemblyId}; TypeId={selectedType.TypeId}; Members={preflight.MemberCount}.");
            return new LintelAssemblyCreationResult(
                LintelAssemblyCreationStatus.Created,
                preflight.AssemblyName,
                assemblyId,
                $"В сборку включено компонентов: {preflight.MemberCount}. Вид и оформление ещё не создавались.");
        }
        catch (Exception exception)
        {
            if (groupStarted && group.GetStatus() == TransactionStatus.Started)
            {
                try
                {
                    group.RollBack();
                }
                catch (Exception rollbackException)
                {
                    logger.Error("Failed to roll back Lintels assembly transaction group.", rollbackException);
                }
            }

            logger.Error(
                $"Failed to create Lintels assembly '{preflight.AssemblyName}'.",
                exception);
            return new LintelAssemblyCreationResult(
                LintelAssemblyCreationStatus.Failed,
                preflight.AssemblyName,
                null,
                "Создание сборки отменено целиком; подробности записаны в лог.");
        }
    }

    private static AssemblyInstance CreateAssembly(
        Document document,
        LintelAssemblyPreflightItem preflight)
    {
        using Transaction transaction = new(document, "TrueBIM: создать сборку перемычки");
        EnsureStatus(
            transaction.Start(),
            TransactionStatus.Started,
            "Revit не начал транзакцию создания сборки.");
        try
        {
            List<ElementId> memberIds = preflight.MemberElementIds
                .Select(RevitElementIds.Create)
                .ToList();
            AssemblyInstance assembly = AssemblyInstance.Create(
                document,
                memberIds,
                RevitElementIds.Create(preflight.NamingCategoryId!.Value));
            EnsureStatus(
                transaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил транзакцию создания сборки.");
            return assembly;
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }

    private static void AssignAssemblyName(
        Document document,
        AssemblyInstance assembly,
        string assemblyName)
    {
        using Transaction transaction = new(document, "TrueBIM: назвать сборку перемычки");
        EnsureStatus(
            transaction.Start(),
            TransactionStatus.Started,
            "Revit не начал транзакцию именования сборки.");
        try
        {
            assembly.AssemblyTypeName = assemblyName;
            EnsureStatus(
                transaction.Commit(),
                TransactionStatus.Committed,
                "Revit откатил транзакцию именования сборки.");
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }

    private static long? FindExistingAssemblyId(Document document, string assemblyName)
    {
        AssemblyInstance? assembly = new FilteredElementCollector(document)
            .OfClass(typeof(AssemblyInstance))
            .Cast<AssemblyInstance>()
            .FirstOrDefault(item => string.Equals(
                item.AssemblyTypeName,
                assemblyName,
                StringComparison.CurrentCultureIgnoreCase));
        return assembly is null
            ? null
            : RevitElementIds.GetValue(assembly.Id);
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
