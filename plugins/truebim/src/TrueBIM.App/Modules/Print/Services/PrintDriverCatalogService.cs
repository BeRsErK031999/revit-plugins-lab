using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintDriverCatalogService
{
    public IReadOnlyList<PrintPrinterOption> GetInstalledPrinters()
    {
        return System.Drawing.Printing.PrinterSettings.InstalledPrinters
            .Cast<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new PrintPrinterOption(name, IsPdfDriverName(name)))
            .ToList();
    }

    public static bool IsPdfDriverName(string? printerName)
    {
        return printerName?.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
