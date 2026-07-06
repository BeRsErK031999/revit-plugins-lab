namespace TrueBIM.App.Modules.BimTools.Worksets.Models;

public sealed class WorksetImportRow
{
    public WorksetImportRow(int lineNumber, string sourceValue, string worksetName)
    {
        LineNumber = lineNumber;
        SourceValue = sourceValue;
        WorksetName = worksetName;
        Status = WorksetImportStatus.Empty;
        Message = string.Empty;
    }

    public int LineNumber { get; }

    public string SourceValue { get; }

    public string WorksetName { get; }

    public WorksetImportStatus Status { get; set; }

    public string Message { get; set; }

    public bool CanCreate => Status == WorksetImportStatus.WillCreate;

    public string StatusDisplay => Status switch
    {
        WorksetImportStatus.WillCreate => "Будет создан",
        WorksetImportStatus.Existing => "Уже существует",
        WorksetImportStatus.Empty => "Пустая строка",
        WorksetImportStatus.Invalid => "Недопустимое имя",
        WorksetImportStatus.DuplicateInFile => "Дубликат в файле",
        WorksetImportStatus.Created => "Создан",
        WorksetImportStatus.Failed => "Ошибка",
        _ => Status.ToString()
    };
}
