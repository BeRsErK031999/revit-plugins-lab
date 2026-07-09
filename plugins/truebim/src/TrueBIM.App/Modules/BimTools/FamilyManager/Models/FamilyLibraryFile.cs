using System.IO;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLibraryFile
{
    public string Path { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "<файл не выбран>";
            }

            try
            {
                string fileName = System.IO.Path.GetFileName(Path);
                return string.IsNullOrWhiteSpace(fileName)
                    ? Path
                    : fileName;
            }
            catch (ArgumentException)
            {
                return Path;
            }
        }
    }
}
