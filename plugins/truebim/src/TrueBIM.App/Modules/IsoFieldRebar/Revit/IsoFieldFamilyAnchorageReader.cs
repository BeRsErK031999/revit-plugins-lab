using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.IsoFieldRebar.Services;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldFamilyAnchorageReader
{
    public bool TryGetBaseAnchorageMillimeters(
        FamilySymbol verifiedTemplate,
        double diameterMillimeters,
        int concreteClass,
        out double anchorageMillimeters,
        out string diagnostic)
    {
        anchorageMillimeters = 0;
        diagnostic = string.Empty;
        Parameter? codeParameter = verifiedTemplate.LookupParameter("Деталь • Код проката");
        double? rollCode = codeParameter?.StorageType == StorageType.Double ? codeParameter.AsDouble() : null;
        if (!IsoFieldArrayFamilyTypeRules.HasVerifiedSteelGrade(
            verifiedTemplate.LookupParameter("• Наименование")?.AsString(), rollCode))
        {
            diagnostic = "Тип не подтверждает A500С вычисляемым наименованием и кодом проката.";
            return false;
        }

        string? tableName = verifiedTemplate.LookupParameter("Таблица выбора")?.AsString();
        if (string.IsNullOrWhiteSpace(tableName))
        {
            diagnostic = "Не задан параметр «Таблица выбора» семейства.";
            return false;
        }

        using FamilySizeTableManager manager = FamilySizeTableManager.GetFamilySizeTableManager(
            verifiedTemplate.Document, verifiedTemplate.Family.Id);
        if (manager is null || !manager.HasSizeTable(tableName))
        {
            diagnostic = $"В семействе отсутствует таблица «{tableName}».";
            return false;
        }

        using FamilySizeTable table = manager.GetSizeTable(tableName);
        int anchorageColumn = Enumerable.Range(0, table.NumberOfColumns)
            .Where(index => string.Equals(table.GetColumnHeader(index).Name, "l0an", StringComparison.Ordinal))
            .DefaultIfEmpty(-1)
            .First();
        if (anchorageColumn < 4 || table.NumberOfColumns < 5)
        {
            diagnostic = $"В таблице «{tableName}» не найден столбец l0an после трёх ключей lookup.";
            return false;
        }

        // The supplied family formula is size_lookup(table, "l0an", 0 mm, roll code, diameter, concrete class).
        // Lookup keys are columns 1..3; column 0 is the row's product name.
        List<double> matchingLengths = new();
        for (int row = 0; row < table.NumberOfRows; row++)
        {
            if (!TryReadNumber(table, row, 1, out double candidateCode)
                || Math.Abs(candidateCode - rollCode!.Value) > 1e-7
                || !TryReadLengthMillimeters(table, row, 2, out double candidateDiameter)
                || Math.Abs(candidateDiameter - diameterMillimeters) > 0.01
                || !TryReadNumber(table, row, 3, out double candidateConcrete)
                || Math.Abs(candidateConcrete - concreteClass) > 1e-7)
            {
                continue;
            }

            if (!TryReadLengthMillimeters(table, row, anchorageColumn, out double candidateLength) || candidateLength <= 0)
            {
                diagnostic = $"В строке {row} таблицы «{tableName}» значение l0an отсутствует или не является положительной длиной.";
                return false;
            }

            if (!IsoFieldArrayFamilyTypeRules.HasVerifiedSteelGrade(table.AsValueString(row, 0), candidateCode))
            {
                diagnostic = $"Строка {row} таблицы «{tableName}» не подтверждает A500С в наименовании для требуемого диаметра.";
                return false;
            }

            matchingLengths.Add(candidateLength);
        }

        if (matchingLengths.Count == 0 || matchingLengths.Any(length => Math.Abs(length - matchingLengths[0]) > 0.01))
        {
            diagnostic = $"Таблица «{tableName}» не даёт однозначного l0an для кода {rollCode}, Ø{diameterMillimeters} и B{concreteClass}.";
            return false;
        }

        anchorageMillimeters = matchingLengths[0];
        diagnostic = $"Family={verifiedTemplate.FamilyName}; Type={verifiedTemplate.Name}; Table={tableName}; "
            + $"RollCode={rollCode}; DiameterMm={diameterMillimeters}; Concrete=B{concreteClass}; Rows={matchingLengths.Count}; l0anMm={anchorageMillimeters}.";
        return true;
    }

    private static bool TryReadNumber(FamilySizeTable table, int row, int column, out double value)
    {
        string raw = table.AsValueString(row, column).Trim();
        return (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            && !double.IsNaN(value)
            && !double.IsInfinity(value);
    }

    private static bool TryReadLengthMillimeters(FamilySizeTable table, int row, int column, out double millimeters)
    {
        millimeters = 0;
        if (!TryReadNumber(table, row, column, out double value))
        {
            return false;
        }

        using FamilySizeTableColumn header = table.GetColumnHeader(column);
#if REVIT2022_OR_GREATER
        if (header.GetSpecTypeId() != SpecTypeId.Length)
        {
            return false;
        }

        millimeters = UnitUtils.ConvertToInternalUnits(value, header.GetUnitTypeId()) * 304.8;
#else
        if (header.UnitType != UnitType.UT_Length)
        {
            return false;
        }

        millimeters = UnitUtils.ConvertToInternalUnits(value, header.DisplayUnitType) * 304.8;
#endif
        return !double.IsNaN(millimeters) && !double.IsInfinity(millimeters);
    }
}
