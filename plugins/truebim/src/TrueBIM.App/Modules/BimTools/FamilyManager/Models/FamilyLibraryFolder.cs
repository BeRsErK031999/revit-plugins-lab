using System.IO;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLibraryFolder
{
    public string Path { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "<папка не выбрана>";
            }

            try
            {
                DirectoryInfo directory = new(Path);
                return string.IsNullOrWhiteSpace(directory.Name)
                    ? Path
                    : directory.Name;
            }
            catch (ArgumentException)
            {
                return Path;
            }
        }
    }
}
