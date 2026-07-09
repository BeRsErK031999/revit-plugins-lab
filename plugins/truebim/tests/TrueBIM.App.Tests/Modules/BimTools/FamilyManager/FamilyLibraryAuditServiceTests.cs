using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyLibraryAuditServiceTests
{
    [Fact]
    public void Audit_ReportsMissingStaleAndIncompleteCache()
    {
        using TempDirectory temp = new();
        string existingFamilyPath = Path.Combine(temp.Path, "Door.rfa");
        string missingFamilyPath = Path.Combine(temp.Path, "Missing.rfa");
        File.WriteAllText(existingFamilyPath, "not a real rfa");
        DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(existingFamilyPath);

        IReadOnlyList<FamilyLibraryAuditIssue> issues = new FamilyLibraryAuditService().Audit(
            [
                new FamilyFileItem
                {
                    FilePath = existingFamilyPath,
                    Name = "Door",
                    Category = FamilyManagerDefaults.UnknownCategory,
                    MetadataUpdatedAtUtc = new DateTimeOffset(lastWriteTimeUtc.AddMinutes(-10), TimeSpan.Zero),
                    LastWriteTimeUtc = new DateTimeOffset(lastWriteTimeUtc, TimeSpan.Zero),
                    SizeBytes = new FileInfo(existingFamilyPath).Length
                },
                new FamilyFileItem
                {
                    FilePath = missingFamilyPath,
                    Name = "Missing",
                    Category = "Двери"
                }
            ],
            [new FamilyLibraryFolder { Path = temp.Path, IsEnabled = true }]);

        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.MissingFile && issue.Severity == FamilyLibraryAuditSeverity.Error);
        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.StaleMetadata && issue.FamilyName == "Door");
        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.EmptyCategory && issue.FamilyName == "Door");
        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.MissingTypes && issue.FamilyName == "Door");
    }

    [Fact]
    public void Audit_ReportsDuplicateNamesSignaturesAndRelativePaths()
    {
        using TempDirectory temp = new();
        string rootA = Path.Combine(temp.Path, "RootA");
        string rootB = Path.Combine(temp.Path, "RootB");
        string relativePath = Path.Combine("Doors", "Door.rfa");
        string familyPathA = Path.Combine(rootA, relativePath);
        string familyPathB = Path.Combine(rootB, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(familyPathA)!);
        Directory.CreateDirectory(Path.GetDirectoryName(familyPathB)!);
        File.WriteAllText(familyPathA, "same family");
        File.WriteAllText(familyPathB, "same family");
        DateTimeOffset lastWriteTimeUtc = new(2026, 7, 9, 8, 0, 0, TimeSpan.Zero);
        long sizeBytes = new FileInfo(familyPathA).Length;

        IReadOnlyList<FamilyLibraryAuditIssue> issues = new FamilyLibraryAuditService().Audit(
            [
                CreateCachedFamily(familyPathA, lastWriteTimeUtc, sizeBytes),
                CreateCachedFamily(familyPathB, lastWriteTimeUtc, sizeBytes)
            ],
            [
                new FamilyLibraryFolder { Path = rootA, IsEnabled = true },
                new FamilyLibraryFolder { Path = rootB, IsEnabled = true }
            ]);

        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.DuplicateName && issue.RelatedCount == 2);
        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.DuplicateSignature && issue.RelatedCount == 2);
        Assert.Contains(issues, issue => issue.Kind == FamilyLibraryAuditIssueKind.DuplicateRelativePath && issue.RelatedCount == 2);
    }

    private static FamilyFileItem CreateCachedFamily(string filePath, DateTimeOffset lastWriteTimeUtc, long sizeBytes)
    {
        return new FamilyFileItem
        {
            FilePath = filePath,
            Name = "Door",
            Category = "Двери",
            SizeBytes = sizeBytes,
            LastWriteTimeUtc = lastWriteTimeUtc,
            MetadataUpdatedAtUtc = lastWriteTimeUtc.AddMinutes(5),
            CachedTypes = [new FamilyTypeInfo(0, "Default")]
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-audit-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
