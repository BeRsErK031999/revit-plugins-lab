using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyMetadataBatchSelectorTests
{
    [Fact]
    public void SelectFolderScope_ReturnsFamiliesUnderSelectedFolder()
    {
        string library = Path.Combine(Path.GetTempPath(), "TrueBimFamilyBatch", "Library");
        string doors = Path.Combine(library, "Doors");
        string other = Path.Combine(Path.GetTempPath(), "TrueBimFamilyBatch", "Other");
        FamilyFileItem door = CreateFamily(Path.Combine(doors, "Door.rfa"), "Door", "Двери");
        FamilyFileItem window = CreateFamily(Path.Combine(library, "Windows", "Window.rfa"), "Window", "Окна");
        FamilyFileItem chair = CreateFamily(Path.Combine(other, "Chair.rfa"), "Chair", "Мебель");

        IReadOnlyList<FamilyFileItem> selected = new FamilyMetadataBatchSelector().SelectFolderScope(
            [chair, door, window],
            library);

        Assert.Equal(["Door", "Window"], selected.Select(family => family.Name));
    }

    [Fact]
    public void SelectFolderScope_DeduplicatesByNormalizedPath()
    {
        string library = Path.Combine(Path.GetTempPath(), "TrueBimFamilyBatch", "Library");
        string familyPath = Path.Combine(library, "Door.rfa");

        IReadOnlyList<FamilyFileItem> selected = new FamilyMetadataBatchSelector().SelectFolderScope(
            [
                CreateFamily(familyPath, "Door", "Двери"),
                CreateFamily(familyPath.ToUpperInvariant(), "Door duplicate", "Двери")
            ],
            library);

        Assert.Single(selected);
        Assert.Equal("Door", selected[0].Name);
    }

    private static FamilyFileItem CreateFamily(string path, string name, string category)
    {
        return new FamilyFileItem
        {
            FilePath = path,
            DirectoryPath = Path.GetDirectoryName(path) ?? string.Empty,
            Name = name,
            Category = category
        };
    }
}
