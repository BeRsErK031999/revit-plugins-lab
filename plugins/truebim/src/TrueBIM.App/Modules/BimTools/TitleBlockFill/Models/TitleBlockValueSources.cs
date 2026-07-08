namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public static class TitleBlockValueSources
{
    public const string StaticText = "Статический текст";

    public const string Date = "Дата";

    public const string SheetNumber = "Номер листа";

    public const string SheetName = "Имя листа";

    public const string ProjectParameter = "Параметр проекта";

    public const string SheetParameter = "Параметр листа";

    public const string Formula = "Формула";

    public static IReadOnlyList<string> All { get; } =
    [
        StaticText,
        Date,
        SheetNumber,
        SheetName,
        ProjectParameter,
        SheetParameter,
        Formula
    ];
}
