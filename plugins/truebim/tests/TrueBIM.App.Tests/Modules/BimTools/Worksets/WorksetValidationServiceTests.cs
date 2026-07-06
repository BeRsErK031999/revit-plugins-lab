using TrueBIM.App.Modules.BimTools.Worksets.Models;
using TrueBIM.App.Modules.BimTools.Worksets.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.Worksets;

public sealed class WorksetValidationServiceTests
{
    [Fact]
    public void Validate_MarksRowsByCreationState()
    {
        List<WorksetImportRow> rows =
        [
            new(1, "АР_Стены", "АР_Стены"),
            new(2, "АР_Стены", "АР_Стены"),
            new(3, "Existing", "Existing"),
            new(4, "", ""),
            new(5, "Bad/Name", "Bad/Name")
        ];
        HashSet<string> existing = new(StringComparer.CurrentCultureIgnoreCase)
        {
            "Existing"
        };

        new WorksetValidationService().Validate(rows, existing);

        Assert.Equal(WorksetImportStatus.WillCreate, rows[0].Status);
        Assert.Equal(WorksetImportStatus.DuplicateInFile, rows[1].Status);
        Assert.Equal(WorksetImportStatus.Existing, rows[2].Status);
        Assert.Equal(WorksetImportStatus.Empty, rows[3].Status);
        Assert.Equal(WorksetImportStatus.Invalid, rows[4].Status);
    }
}
