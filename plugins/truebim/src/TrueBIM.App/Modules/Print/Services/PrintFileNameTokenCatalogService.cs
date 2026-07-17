using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintFileNameTokenCatalogService
{
    public IReadOnlyList<PrintFileNameTokenOption> BuildOptions(
        IEnumerable<PrintParameterCatalog> catalogs)
    {
        Guard.NotNull(catalogs, nameof(catalogs));

        List<PrintFileNameTokenOption> options =
        [
            new("Системные", "Номер листа", "{Номер листа}"),
            new("Системные", "Имя листа", "{Имя листа}"),
            new("Системные", "Имя документа", "{Имя документа}"),
            new("Системные", "Номер проекта", "{Номер проекта}"),
            new("Системные", "Имя проекта", "{Имя проекта}"),
            new("Системные", "Дата сегодня", "{Дата:yyyy-MM-dd}"),
            new("Системные", "Счётчик 001", "{Счётчик:000}")
        ];

        IReadOnlyList<PrintParameterCatalog> sourceCatalogs = catalogs.ToList();
        AddParameterOptions(
            options,
            "Параметры листа",
            "Параметр листа",
            sourceCatalogs.SelectMany(catalog => catalog.SheetParameterNames));
        AddParameterOptions(
            options,
            "Основная надпись",
            "Параметр основной надписи",
            sourceCatalogs.SelectMany(catalog => catalog.TitleBlockParameterNames));
        AddParameterOptions(
            options,
            "Сведения о проекте",
            "Параметр проекта",
            sourceCatalogs.SelectMany(catalog => catalog.ProjectParameterNames));

        return options;
    }

    public PrintFileNameTokenInsertion InsertAtCaret(
        string? text,
        int caretIndex,
        string token)
    {
        Guard.NotNullOrWhiteSpace(token, nameof(token));

        string sourceText = text ?? string.Empty;
        int safeCaretIndex = Math.Max(0, Math.Min(caretIndex, sourceText.Length));
        string result = sourceText.Insert(safeCaretIndex, token);
        return new PrintFileNameTokenInsertion(result, safeCaretIndex + token.Length);
    }

    private static void AddParameterOptions(
        List<PrintFileNameTokenOption> options,
        string category,
        string tokenPrefix,
        IEnumerable<string> parameterNames)
    {
        foreach (string parameterName in parameterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            options.Add(new PrintFileNameTokenOption(
                category,
                parameterName,
                $"{{{tokenPrefix}:{parameterName}}}"));
        }
    }
}
