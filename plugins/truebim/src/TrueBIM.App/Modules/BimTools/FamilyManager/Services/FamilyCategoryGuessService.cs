using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyCategoryGuessService
{
    private static readonly IReadOnlyList<(string Category, string[] Tokens)> Rules =
    [
        ("Двери", ["door", "doors", "двер"]),
        ("Окна", ["window", "windows", "окн"]),
        ("Витражи", ["curtain", "витраж"]),
        ("Штампы", ["titleblock", "title block", "штамп", "основная надпись"]),
        ("Аннотации", ["annotation", "tag", "label", "марка", "аннотац"]),
        ("Мебель", ["furniture", "chair", "table", "мебел", "стул", "стол"]),
        ("Сантехника", ["plumbing", "pipe", "fixture", "сантех", "труб"]),
        ("ОВиК", ["mechanical", "duct", "air", "hvac", "воздух", "ов"]),
        ("ЭОМ", ["electrical", "light", "lighting", "power", "электр", "свет"]),
        ("Конструкции", ["structural", "beam", "column", "foundation", "балк", "колон", "фундамент"]),
        ("Обобщённые модели", ["generic", "model", "обобщ"])
    ];

    public string Guess(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return FamilyManagerDefaults.UnknownCategory;
        }

        string value = filePath.ToLowerInvariant();
        foreach ((string category, string[] tokens) in Rules)
        {
            if (tokens.Any(token => value.Contains(token)))
            {
                return category;
            }
        }

        return FamilyManagerDefaults.UnknownCategory;
    }
}
