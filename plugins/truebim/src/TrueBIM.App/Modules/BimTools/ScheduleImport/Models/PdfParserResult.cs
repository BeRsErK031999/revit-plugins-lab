namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record PdfParserResult(
    IReadOnlyList<ParsedTable> Tables,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0 && Tables.Count > 0;

    public static PdfParserResult FromError(string error)
    {
        return new PdfParserResult(
            Array.Empty<ParsedTable>(),
            Array.Empty<string>(),
            [error]);
    }
}
