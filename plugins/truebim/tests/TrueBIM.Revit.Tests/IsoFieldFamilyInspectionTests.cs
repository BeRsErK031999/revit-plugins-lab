using System.Text.Json;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using NUnit.Framework;

namespace TrueBIM.Revit.Tests;

[TestFixture]
public sealed class IsoFieldFamilyInspectionTests
{
    private Application application = null!;

    [OneTimeSetUp]
    public void SetUp(Application application)
    {
        this.application = application;
    }

    [Test]
    public void SuppliedFamilies_WriteInspectableContractReport()
    {
        string familyDirectory = ResolveFamilyDirectory();
        string[] familyPaths = Directory.GetFiles(familyDirectory, "*.rfa")
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        Assert.That(familyPaths, Is.Not.Empty, $"No .rfa files found in {familyDirectory}.");

        List<FamilyInspection> inspections = new();
        foreach (string familyPath in familyPaths)
        {
            using Document familyDocument = application.OpenDocumentFile(familyPath);
            Assert.That(familyDocument.IsFamilyDocument, Is.True, familyPath);
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

        string reportPath = ResolveReportPath();
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(inspections, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.Progress.WriteLine($"Family contract report: {reportPath}");

        FamilyInspection straightArray = inspections.Single(item =>
            item.FileName.Contains("Массив", StringComparison.CurrentCultureIgnoreCase)
            && item.FileName.EndsWith("000.rfa", StringComparison.OrdinalIgnoreCase));
        Assert.Multiple(() =>
        {
            Assert.That(straightArray.Parameters.Any(parameter => parameter.Name == "A" && parameter.IsInstance), Is.True);
            Assert.That(straightArray.Parameters.Any(parameter => parameter.Name == "Зона • Ширина" && parameter.IsInstance), Is.True);
            Assert.That(
                straightArray.Parameters.Any(parameter =>
                    parameter.Name == "• Деталь • Арматура. Диаметр"
                    && !parameter.IsInstance),
                Is.True);
            Assert.That(
                straightArray.Parameters.Any(parameter =>
                    parameter.Name == "• Деталь • Шаг элементов"
                    && !parameter.IsInstance),
                Is.True);
        });
    }

    private static string ResolveFamilyDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("TRUEBIM_ISOFIELD_FAMILY_DIR");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        throw new InvalidOperationException(
            "Set TRUEBIM_ISOFIELD_FAMILY_DIR to the directory containing the supplied reinforcement families.");
    }

    private static string ResolveReportPath()
    {
        string? configured = Environment.GetEnvironmentVariable("TRUEBIM_REVIT_TEST_REPORT_DIR");
        string directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(TestContext.CurrentContext.WorkDirectory, "revit-test-results")
            : Path.GetFullPath(configured);
        return Path.Combine(directory, "isofield-family-contract.json");
    }

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
