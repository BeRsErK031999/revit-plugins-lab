using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using TrueBIM.App.Modules.Tep;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Tep;

public sealed class TepTests
{
    private static TepItem Room(string key, double area, string group = "1") => new()
    {
        Key = key, ElementId = key.GetHashCode(), Document = "B1", Kind = "Помещение", Part = TepParts.B1,
        Role = TepRoles.Apartment, Area = area, Group = group, RoomsInApartment = 2, Level = "Этаж 03", Living = true
    };
    private static TepCalculation Calculate(params TepItem[] items) => new TepCalculator().Calculate(
        new TepSnapshot { AvailableParts = TepParts.All.ToList(), Items = items.ToList() },
        new TepOptions { ClassificationReviewed = true });
    private static TepValue Value(TepCalculation result, int row, string part = TepParts.B1) => result.Rows.Single(r => r.Metric.Row == row).Values[part];

    [Fact]
    public void AreasKeepPrecisionAndApartmentCountsUseGroup()
    {
        TepCalculation result = Calculate(Room("a", 12.044), Room("b", 5.044));
        Assert.Equal(17.088, Value(result, 14).Number!.Value, 10);
        Assert.Equal(1, Value(result, 17).Number);
        Assert.Equal(1, Value(result, 19).Number);
        Assert.Equal(17.088, Value(result, 14, "Итог").Number!.Value, 10);
    }

    [Fact]
    public void ColdRoomsUseTheirActualCoefficients()
    {
        TepItem loggia = Room("b", 8); loggia.Cold = true; loggia.Coefficient = 0.5;
        TepItem balcony = Room("c", 10); balcony.Cold = true; balcony.Coefficient = 0.3;
        TepCalculation result = Calculate(Room("a", 30), loggia, balcony);
        Assert.Equal(30, Value(result, 14).Number);
        Assert.Equal(37, Value(result, 15).Number);
        Assert.Equal(48, Value(result, 16).Number);
    }

    [Fact]
    public void RepeatedSourceIsNotSummedTwice()
    {
        TepCalculation result = Calculate(Room("same", 12), Room("same", 12));
        Assert.Equal(12, Value(result, 14).Number);
        Assert.Contains(result.Issues, s => s.Contains("Повтор источника"));
    }

    [Fact]
    public void MissingDocumentAndNonZeroInvalidRoomAreNotReportedAsZero()
    {
        TepCalculation missing = new TepCalculator().Calculate(new TepSnapshot(), new TepOptions());
        Assert.Null(Value(missing, 14).Number);
        Assert.Null(Value(missing, 14, "Итог").Number);
        Assert.Null(Value(Calculate(Room("a", double.NaN)), 14).Number);
        Assert.Null(Value(Calculate(Room("a", -1)), 14).Number);
        TepItem missingArea = Room("a", 1); missingArea.Area = null;
        Assert.Null(Value(Calculate(missingArea), 14).Number);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZeroAreaRoomIsSkippedAndPartialWarningSurvivesTotalAndReview(bool reviewed)
    {
        TepItem zero = Room("zero", 0); zero.Name = "Тамбур"; zero.Problem = "Нет положительной площади";
        TepSnapshot snapshot = new() { AvailableParts = TepParts.All.ToList(), Items = [Room("valid", 12.044), zero] };
        TepCalculation result = new TepCalculator().Calculate(snapshot, new TepOptions { ClassificationReviewed = reviewed });
        foreach (string part in new[] { TepParts.B1, "Итог" })
        {
            TepValue value = Value(result, 14, part);
            Assert.Equal(12.044, value.Number);
            Assert.True(value.IsPartial);
            Assert.Equal("Неполный расчёт", value.Status);
            Assert.Contains("Тамбур", value.Reason);
            Assert.Contains(zero.ElementId.ToString(), value.Reason);
            Assert.Equal(["valid"], value.Sources);
        }
        Assert.False(Value(result, 14, TepParts.B2).IsPartial);
        Assert.Equal("12,0 *", result.Rows.Single(r => r.Metric.Row == 14).B1);
        Assert.Equal("12,0 *", result.Rows.Single(r => r.Metric.Row == 14).Total);
        Assert.Contains(result.Issues, s => s.Contains("Пропущено помещение") && s.Contains("Тамбур"));
        Assert.Equal(0, zero.Area); // Input remains available to diagnostics and profile editing.
        Assert.True(zero.Included);
    }

    [Fact]
    public void AllZeroRoomsYieldExplicitlyPartialZeroInsteadOfCompleteZero()
    {
        TepItem zero = Room("zero", 0);
        zero.RoomTypeKnown = false; zero.Coefficient = double.NaN; zero.RoomsInApartment = null; zero.Group = "";
        TepCalculation result = Calculate(zero);
        foreach (int row in Enumerable.Range(13, 9))
        {
            Assert.Equal(0, Value(result, row).Number);
            Assert.True(Value(result, row).IsPartial);
            Assert.True(Value(result, row, "Итог").IsPartial);
        }
    }

    [Fact]
    public void ZeroRoomsDoNotInventGroupsAndDoNotWarnUnrelatedMetrics()
    {
        TepItem office = Room("office", 60, "Офис 1"); office.Role = TepRoles.Office; office.Level = "Этаж 02";
        TepItem zero = Room("zero", 0, "Офис 2"); zero.Role = TepRoles.Office; zero.Level = "Этаж 01";
        TepCalculation result = Calculate(office, zero);
        Assert.Equal(60, Value(result, 24).Number);
        Assert.True(Value(result, 24).IsPartial);
        Assert.Equal(0, Value(result, 25).Number);
        Assert.True(Value(result, 25).IsPartial);
        Assert.Equal(60, Value(result, 26).Number);
        Assert.False(Value(result, 26).IsPartial);
        Assert.Equal(1, Value(result, 30).Number);
        Assert.True(Value(result, 30).IsPartial);
        Assert.False(Value(result, 31).IsPartial);
        Assert.False(Value(result, 36).IsPartial);
        zero.Included = false;
        Assert.False(Value(Calculate(office, zero), 24).IsPartial);
    }

    [Fact]
    public void PopulationDerivedFromPartialAreaRetainsWarning()
    {
        TepItem office = Room("office", 60); office.Role = TepRoles.Office; office.EmployeeArea = true;
        TepItem zeroOffice = Room("zeroOffice", 0); zeroOffice.Role = TepRoles.Office; zeroOffice.EmployeeArea = true;
        TepSnapshot snapshot = new()
        {
            AvailableParts = TepParts.All.ToList(), Items = [Room("room", 80), Room("zeroRoom", 0), office, zeroOffice]
        };
        TepCalculation result = new TepCalculator().Calculate(snapshot,
            new TepOptions { ClassificationReviewed = true, PopulationBasisConfirmed = true, PopulationBasis = "Тестовое основание" });
        Assert.Equal(2, Value(result, 37).Number);
        Assert.Equal(10, Value(result, 38).Number);
        Assert.True(Value(result, 37, "Итог").IsPartial);
        Assert.True(Value(result, 38, "Итог").IsPartial);
    }

    [Fact]
    public void ZeroZoneAndPositiveRoomWithOtherErrorStillBlock()
    {
        TepItem zone = Room("zone", 0); zone.Kind = "Зона"; zone.Role = TepRoles.Footprint;
        Assert.Null(Value(Calculate(zone), 2).Number);
        TepItem room = Room("room", 20); room.Problem = "Помещение в варианте конструкции";
        Assert.Null(Value(Calculate(room, Room("zero", 0)), 14).Number);
    }

    [Fact]
    public void UnassignedZeroRoomWarnsTotalWithoutBlockingKnownParts()
    {
        TepItem zero = Room("zero", 0); zero.Part = "";
        TepCalculation result = Calculate(Room("room", 20), zero);
        Assert.Equal(20, Value(result, 14).Number);
        Assert.False(Value(result, 14).IsPartial);
        Assert.Equal(20, Value(result, 14, "Итог").Number);
        Assert.True(Value(result, 14, "Итог").IsPartial);
    }

    [Fact]
    public void UnknownPartBlocksTotalInsteadOfDroppingRoom()
    {
        TepItem item = Room("a", 20); item.Part = "";
        Assert.Null(Value(Calculate(item), 14, "Итог").Number);
    }

    [Fact]
    public void InconsistentApartmentTypeBlocksBreakdown()
    {
        TepItem second = Room("b", 10); second.RoomsInApartment = 3;
        Assert.Null(Value(Calculate(Room("a", 20), second), 19).Number);
    }

    [Fact]
    public void ExcludedInvalidRoomDoesNotBlockResults()
    {
        TepItem invalid = Room("b", 0); invalid.Included = false;
        Assert.Equal(20, Value(Calculate(Room("a", 20), invalid), 14).Number);
    }

    [Fact]
    public void OpenTerraceContributesAreaButNotVolumeOrAdditionalFloor()
    {
        TepItem enclosed = Room("a", 100); enclosed.Kind = "Зона"; enclosed.Role = TepRoles.BuildingAbove; enclosed.Height = 3.3;
        TepItem terrace = Room("b", 20); terrace.Kind = "Зона"; terrace.Role = TepRoles.BuildingAbove; terrace.IncludeVolume = false;
        TepCalculation result = Calculate(enclosed, terrace);
        Assert.Equal(120, Value(result, 6).Number);
        Assert.Equal(330, Value(result, 10).Number);
        Assert.Equal(1, Value(result, 11).Number);
        enclosed.Height = null;
        Assert.Null(Value(Calculate(enclosed, terrace), 10).Number);
    }

    [Fact]
    public void BasementAndTechnicalFloorFlagsAreIndependent()
    {
        TepItem above = Room("a", 100); above.Kind = "Зона"; above.Role = TepRoles.BuildingAbove;
        TepItem below = Room("b", 100); below.Kind = "Зона"; below.Role = TepRoles.BuildingBelow;
        below.Level = "Подвал"; below.CountAsStorey = false;
        TepCalculation result = Calculate(above, below);
        Assert.Equal(1, Value(result, 11).Number);
        Assert.Equal(2, Value(result, 12).Number);
    }

    [Fact]
    public void PopulationRequiresExplicitBasis()
    {
        Assert.Null(Value(Calculate(Room("a", 80)), 37).Number);
        TepCalculation result = new TepCalculator().Calculate(new TepSnapshot
        {
            AvailableParts = TepParts.All.ToList(), Items = [Room("a", 81)]
        }, new TepOptions { PopulationBasisConfirmed = true, PopulationBasis = "Проектное основание", ClassificationReviewed = true });
        Assert.Equal(2.025, Value(result, 37).Number);
    }

    [Fact]
    public void ForeignSectionAndLegacyApartmentAreExcluded()
    {
        TepItem item = Room("a", 10);
        TepClassifier.Room(item, "2", "2К", 1, 1, false);
        Assert.False(item.Included);
        Assert.Equal(TepRoles.Apartment, item.Role);
    }

    [Fact]
    public void RoofHeightIsNotSilentlyRequiredForEnclosedVolume()
    {
        TepItem roof = new() { Name = "Кровля Б2", Elevation = 70.1 };
        TepClassifier.Area(roof, true, false, false);
        Assert.Equal(TepRoles.Roof, roof.Role);
        Assert.False(roof.IncludeVolume);
        Assert.False(roof.CountAsFloor);
    }

    [Fact]
    public void SharedRoomUniqueIdUsesTowerSourceInsteadOfLegacyHostCopy()
    {
        TepItem tower = Room("tower|same-uid", 25);
        TepItem legacy = Room("host|same-uid", 2030); legacy.Part = TepParts.Stylobate;
        TepSnapshot snapshot = new() { Items = [tower, legacy] };
        TepClassifier.ResolveRoomCopies(snapshot);
        Assert.True(tower.Included);
        Assert.False(legacy.Included);
        Assert.Single(snapshot.Issues);
    }

    [Fact]
    public void UnknownRoomTypeBlocksLivingAreaButNotApartmentCountOrGrossArea()
    {
        TepItem cabinet = Room("a", 20);
        TepClassifier.Room(cabinet, "1", "4К", 0, 1, true);
        TepCalculation result = Calculate(cabinet);
        Assert.Null(Value(result, 13).Number);
        Assert.Null(Value(result, 14).Number);
        Assert.Equal(20, Value(result, 16).Number);
        Assert.Equal(1, Value(result, 17).Number);
        Assert.Equal(1, Value(result, 21).Number);
    }

    [Fact]
    public void OfficeCommonAreasParticipateInUsefulAndCalculatedAreasAccordingToFlags()
    {
        TepItem office = Room("a", 60); office.Role = TepRoles.Office;
        TepItem corridor = Room("b", 10); corridor.Name = "Коридор офисов";
        TepClassifier.Room(corridor, "", "Офис", 0, 1, true);
        TepItem toilet = Room("c", 5); toilet.Name = "С/У";
        TepClassifier.Room(toilet, "", "Офис", 0, 1, true);
        TepCalculation result = Calculate(office, corridor, toilet);
        Assert.Equal(65, Value(result, 28).Number);
        Assert.Equal(75, Value(result, 29).Number);
    }

    [Fact]
    public void ExcelPreservesOriginalAndWritesFullPrecisionAndDiagnostics()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tep-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.xlsx"), output = Path.Combine(directory, "result.xlsx");
            CreateTemplate(original);
            byte[] before = File.ReadAllBytes(original);
            TepSnapshot snapshot = new() { Project = "test", AvailableParts = TepParts.All.ToList(), Items = [Room("a", 12.044), Room("b", 5.044), Room("zero", 0)] };
            TepOptions options = new();
            new TepExcel().Export(original, output, snapshot, options, new TepCalculator().Calculate(snapshot, options));
            Assert.Equal(before, File.ReadAllBytes(original));
            using ZipArchive zip = ZipFile.OpenRead(output);
            using ZipArchive source = ZipFile.OpenRead(original);
            Assert.Equal(Read(source, "xl/worksheets/sheet1.xml"), Read(zip, "xl/worksheets/sheet1.xml"));
            Assert.Contains("Расчёт ТЭП", Read(zip, "xl/workbook.xml"));
            Assert.Contains("Источники", Read(zip, "xl/workbook.xml"));
            string all = string.Join("", zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/tep")).Select(e => Read(zip, e.FullName)));
            Assert.Contains("17.088", all);
            Assert.Contains("предварительно", all);
            Assert.Contains("F34 отличается", all);
            Assert.Contains("Неполный расчёт", all);
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument calculated = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/tep"))
                .Select(e => XDocument.Parse(Read(zip, e.FullName)))
                .Single(d => d.Descendants(s + "c").Any(c => (string?)c.Attribute("r") == "B1" && c.Value.Contains("ПРЕДВАРИТЕЛЬНЫЙ")));
            XDocument styles = XDocument.Parse(Read(zip, "xl/styles.xml"));
            foreach (string address in new[] { "C14", "F14" })
            {
                XElement cell = calculated.Descendants(s + "c").Single(c => (string?)c.Attribute("r") == address);
                Assert.Null(cell.Attribute("t")); // Numeric cell, not a string with a warning appended.
                Assert.Equal("17.088", cell.Element(s + "v")!.Value);
                XElement style = styles.Root!.Element(s + "cellXfs")!.Elements().ElementAt((int)cell.Attribute("s")!);
                string format = styles.Root.Element(s + "numFmts")!.Elements()
                    .Single(f => (string?)f.Attribute("numFmtId") == (string?)style.Attribute("numFmtId")).Attribute("formatCode")!.Value;
                Assert.Equal("0.0\" *\"", format);
                Assert.Equal("FFFFE699", styles.Root.Element(s + "fills")!.Elements()
                    .ElementAt((int)style.Attribute("fillId")!).Descendants(s + "fgColor").Single().Attribute("rgb")!.Value);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExcelRejectsWrongRowsAndNeverOverwritesInput()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tep-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.xlsx"), output = Path.Combine(directory, "result.xlsx");
            CreateTemplate(original, wrongLabel: true);
            TepExcel excel = new();
            Assert.Throws<InvalidOperationException>(() => excel.Export(original, original, new(), new(), Calculate()));
            Assert.Throws<InvalidDataException>(() => excel.Export(original, output, new(), new(), Calculate()));
            Assert.False(File.Exists(output));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ParkingCountsOnlyContainedInstancesAndDoesNotHideMissingGeometry()
    {
        TepItem Inside(string key) => new() { Key = key, Kind = "Машиноместо", Part = TepParts.Stylobate, Role = TepRoles.ParkingPlace };
        TepItem first = Inside("1"), outside = Inside("2"), unknown = Inside("3");
        TepClassifier.ParkingScope(first, true, "100");
        TepClassifier.ParkingScope(outside, false, "");
        Assert.Equal(1, Value(Calculate(first, outside), 36, TepParts.Stylobate).Number);
        TepClassifier.ParkingScope(unknown, null, "");
        Assert.Null(Value(Calculate(first, unknown), 36, TepParts.Stylobate).Number);
    }

    private static string Read(ZipArchive zip, string path)
    {
        using StreamReader reader = new(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }
    private static void CreateTemplate(string path, bool wrongLabel = false)
    {
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XElement TextCell(string address, string value) => new(s + "c", new XAttribute("r", address), new XAttribute("t", "inlineStr"), new XElement(s + "is", new XElement(s + "t", value)));
        XElement data = new(s + "sheetData", new XElement(s + "row", new XAttribute("r", 1),
            new[] { "Б1", "Стилобат", "Б2", "Итог" }.Select((v, i) => TextCell(((char)('C' + i)) + "1", v))));
        foreach (TepMetric metric in TepMetrics.All)
            data.Add(new XElement(s + "row", new XAttribute("r", metric.Row), TextCell("B" + metric.Row, wrongLabel ? "Неверная строка" : metric.Name),
                new[] { "C", "D", "E", "F" }.Select(col => new XElement(s + "c", new XAttribute("r", col + metric.Row), new XElement(s + "v", col == "F" && metric.Row == 34 ? "11.4" : "0")))));
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Write(string entry, XDocument xml) { using Stream stream = zip.CreateEntry(entry).Open(); xml.Save(stream); }
        Write("xl/worksheets/sheet1.xml", new XDocument(new XElement(s + "worksheet", data)));
        Write("xl/workbook.xml", new XDocument(new XElement(s + "workbook", new XElement(s + "sheets", new XElement(s + "sheet",
            new XAttribute("name", "Лист1"), new XAttribute("sheetId", 1), new XAttribute(r + "id", "rId1"))))));
        XNamespace p = "http://schemas.openxmlformats.org/package/2006/relationships";
        Write("xl/_rels/workbook.xml.rels", new XDocument(new XElement(p + "Relationships", new XElement(p + "Relationship",
            new XAttribute("Id", "rId1"), new XAttribute("Target", "worksheets/sheet1.xml")))));
        Write("[Content_Types].xml", new XDocument(new XElement(XName.Get("Types", "http://schemas.openxmlformats.org/package/2006/content-types"))));
        Write("xl/styles.xml", new XDocument(new XElement(s + "styleSheet",
            new XElement(s + "fonts", new XAttribute("count", 1), new XElement(s + "font")),
            new XElement(s + "fills", new XAttribute("count", 2),
                new XElement(s + "fill", new XElement(s + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(s + "fill", new XElement(s + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(s + "borders", new XAttribute("count", 1), new XElement(s + "border")),
            new XElement(s + "cellStyleXfs", new XAttribute("count", 1), new XElement(s + "xf")),
            new XElement(s + "cellXfs", new XAttribute("count", 1), new XElement(s + "xf")))));
    }
}
