namespace TrueBIM.App.Modules.Print.Services;

public enum PrintFolderPdfRoute
{
    Driver,
    NativeExport,
    Unsupported
}

public static class PrintFolderPdfPolicy
{
    // Other PDF drivers can return PostScript to the redirected output file.
    // Only these two have a supported direct-PDF/conversion path.
    public static PrintFolderPdfRoute GetRoute(string? printer, bool automaticPaper, bool nativePdfAvailable)
    {
        if (string.Equals(printer?.Trim(), "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) ||
            printer?.IndexOf("PDF24", StringComparison.OrdinalIgnoreCase) >= 0)
            return PrintFolderPdfRoute.Driver;

        return automaticPaper && nativePdfAvailable && PrintDriverCatalogService.IsPdfDriverName(printer)
            ? PrintFolderPdfRoute.NativeExport
            : PrintFolderPdfRoute.Unsupported;
    }

    public static bool NativePdfAvailable
    {
        get
        {
#if REVIT2022_OR_GREATER
            return true;
#else
            return false;
#endif
        }
    }

    public static string GetMessage(PrintFolderPdfRoute route) => route switch
    {
        PrintFolderPdfRoute.NativeExport =>
            "PDF средствами Revit: формат листа, 100%, выбранные цвета и качество. Текущие параметры печати Revit обновляются; сохранённые настройки не перезаписываются.",
        PrintFolderPdfRoute.Unsupported => NativePdfAvailable
            ? "Этот драйвер не поддерживает запись PDF в папку. Выберите «Авто по листу» для PDF средствами Revit, режим «Экспорт в файлы», Microsoft Print to PDF или PDF24."
            : "Этот драйвер не поддерживает запись PDF в папку. Выберите Microsoft Print to PDF или PDF24 либо снимите флажок «PDF» для печати через диалог драйвера.",
        _ => "Драйвер печатает в выбранную папку. Снимите «Один PDF» для отдельных файлов."
    };
}
