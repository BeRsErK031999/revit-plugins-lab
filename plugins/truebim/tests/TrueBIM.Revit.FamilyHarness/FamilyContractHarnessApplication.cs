using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace TrueBIM.Revit.FamilyHarness;

public sealed class FamilyContractHarnessApplication : IExternalApplication
{
    private const string FamilyDirectoryVariable = "TRUEBIM_ISOFIELD_FAMILY_DIR";
    private const string ReportPathVariable = "TRUEBIM_REVIT_HARNESS_REPORT_PATH";
    private const string RunIdVariable = "TRUEBIM_REVIT_HARNESS_RUN_ID";

    private UIControlledApplication? controlledApplication;
    private HarnessSettings? settings;
    private int hasRun;

    public Result OnStartup(UIControlledApplication application)
    {
        if (!TryReadSettings(out HarnessSettings? resolvedSettings))
        {
            return Result.Succeeded;
        }

        controlledApplication = application;
        settings = resolvedSettings;
        application.Idling += OnIdling;
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdling;
        return Result.Succeeded;
    }

    private void OnIdling(object? sender, IdlingEventArgs eventArgs)
    {
        if (Interlocked.Exchange(ref hasRun, 1) != 0)
        {
            return;
        }

        if (controlledApplication is not null)
        {
            controlledApplication.Idling -= OnIdling;
        }

        HarnessSettings currentSettings = settings
            ?? throw new InvalidOperationException("The family harness settings were not initialized.");

        HarnessReport report;
        try
        {
            if (sender is not UIApplication uiApplication)
            {
                throw new InvalidOperationException(
                    $"Expected {nameof(UIApplication)} as the Revit Idling sender, but received "
                    + $"{sender?.GetType().FullName ?? "null"}.");
            }

            report = InspectFamilies(uiApplication.Application, currentSettings);
        }
        catch (Exception exception)
        {
            report = new HarnessReport(
                currentSettings.RunId,
                false,
                null,
                null,
                currentSettings.FamilyDirectory,
                [],
                [],
                exception.ToString());
        }

        WriteReport(currentSettings.ReportPath, report);
    }

    private static HarnessReport InspectFamilies(
        Application application,
        HarnessSettings settings)
    {
        string[] familyPaths = Directory.GetFiles(settings.FamilyDirectory, "*.rfa")
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        if (familyPaths.Length == 0)
        {
            throw new InvalidOperationException(
                $"No .rfa files were found in '{settings.FamilyDirectory}'.");
        }

        List<FamilyInspection> inspections = [];
        foreach (string familyPath in familyPaths)
        {
            Document? familyDocument = null;
            try
            {
                familyDocument = application.OpenDocumentFile(familyPath);
                if (!familyDocument.IsFamilyDocument)
                {
                    throw new InvalidOperationException(
                        $"'{familyPath}' is not a Revit family document.");
                }

                FamilyManager manager = familyDocument.FamilyManager;
                FamilyParameterInspection[] parameters = manager.Parameters
                    .Cast<FamilyParameter>()
                    .Select(parameter => new FamilyParameterInspection(
                        parameter.Definition.Name,
                        parameter.IsInstance,
                        parameter.IsReporting,
                        parameter.StorageType.ToString(),
                        parameter.Formula))
                    .OrderBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                string[] typeNames = manager.Types
                    .Cast<FamilyType>()
                    .Select(type => type.Name)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

                inspections.Add(new FamilyInspection(
                    Path.GetFileName(familyPath),
                    familyDocument.Title,
                    typeNames,
                    parameters));
            }
            finally
            {
                familyDocument?.Close(false);
            }
        }

        string[] contractErrors = ValidateStraightArrayContract(inspections);
        return new HarnessReport(
            settings.RunId,
            contractErrors.Length == 0,
            application.VersionNumber,
            application.VersionBuild,
            settings.FamilyDirectory,
            inspections,
            contractErrors,
            null);
    }

    private static string[] ValidateStraightArrayContract(
        IReadOnlyCollection<FamilyInspection> inspections)
    {
        List<string> errors = [];
        FamilyInspection[] candidates = inspections
            .Where(item =>
                item.FileName.Contains("Массив", StringComparison.CurrentCultureIgnoreCase)
                && item.FileName.EndsWith("000.rfa", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1)
        {
            errors.Add(
                "Expected exactly one straight-array family whose file name contains "
                + "'Массив' and ends with '000.rfa'.");
            return errors.ToArray();
        }

        FamilyInspection straightArray = candidates[0];
        RequireParameter(straightArray, "A", true, errors);
        RequireParameter(straightArray, "Зона • Ширина", true, errors);
        RequireParameter(straightArray, "• Деталь • Арматура. Диаметр", false, errors);
        RequireParameter(straightArray, "• Деталь • Шаг элементов", false, errors);
        return errors.ToArray();
    }

    private static void RequireParameter(
        FamilyInspection family,
        string parameterName,
        bool expectedInstance,
        ICollection<string> errors)
    {
        FamilyParameterInspection? parameter = family.Parameters.FirstOrDefault(item =>
            string.Equals(item.Name, parameterName, StringComparison.Ordinal));
        if (parameter is null)
        {
            errors.Add($"Family '{family.FileName}' has no parameter '{parameterName}'.");
            return;
        }

        if (parameter.IsInstance != expectedInstance)
        {
            string expectedScope = expectedInstance ? "instance" : "type";
            errors.Add(
                $"Parameter '{parameterName}' in family '{family.FileName}' must be an "
                + $"{expectedScope} parameter.");
        }
    }

    private static bool TryReadSettings(out HarnessSettings? settings)
    {
        string? familyDirectory = Environment.GetEnvironmentVariable(FamilyDirectoryVariable);
        string? reportPath = Environment.GetEnvironmentVariable(ReportPathVariable);
        string? runId = Environment.GetEnvironmentVariable(RunIdVariable);
        if (string.IsNullOrWhiteSpace(familyDirectory)
            || string.IsNullOrWhiteSpace(reportPath)
            || string.IsNullOrWhiteSpace(runId))
        {
            settings = null;
            return false;
        }

        settings = new HarnessSettings(
            Path.GetFullPath(familyDirectory),
            Path.GetFullPath(reportPath),
            runId);
        return true;
    }

    private static void WriteReport(string reportPath, HarnessReport report)
    {
        string? reportDirectory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(reportDirectory))
        {
            throw new InvalidOperationException($"Invalid report path '{reportPath}'.");
        }

        Directory.CreateDirectory(reportDirectory);
        string temporaryPath = reportPath + $".{report.RunId}.tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, reportPath, true);
    }

    private sealed record HarnessSettings(
        string FamilyDirectory,
        string ReportPath,
        string RunId);

    private sealed record HarnessReport(
        string RunId,
        bool Succeeded,
        string? RevitVersion,
        string? RevitBuild,
        string FamilyDirectory,
        IReadOnlyList<FamilyInspection> Families,
        IReadOnlyList<string> ContractErrors,
        string? FatalError);

    private sealed record FamilyInspection(
        string FileName,
        string FamilyName,
        IReadOnlyList<string> TypeNames,
        IReadOnlyList<FamilyParameterInspection> Parameters);

    private sealed record FamilyParameterInspection(
        string Name,
        bool IsInstance,
        bool IsReporting,
        string StorageType,
        string? Formula);
}
