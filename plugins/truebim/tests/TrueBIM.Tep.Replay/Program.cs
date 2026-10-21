using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.Tep;

if (args.Length != 4)
    throw new ArgumentException("Arguments: snapshot.json parking.json template.xlsx output-stem");
JsonNode node = JsonNode.Parse(File.ReadAllText(args[0]))!;
string? collected = node["CollectedAt"]?.GetValue<string>();
if (collected is not null && collected.StartsWith("/Date("))
{
    long milliseconds = long.Parse(Regex.Match(collected, @"-?\d+").Value);
    node["CollectedAt"] = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime.ToString("O");
}
TepSnapshot snapshot = node.Deserialize<TepSnapshot>() ?? throw new InvalidDataException("Empty snapshot.");
using JsonDocument parkingEvidence = JsonDocument.Parse(File.ReadAllText(args[1]));
Dictionary<long, long[]> containment = parkingEvidence.RootElement.EnumerateArray()
    .ToDictionary(e => e.GetProperty("Id").GetInt64(), e => e.GetProperty("Rooms").EnumerateArray().Select(x => x.GetInt64()).ToArray());
HashSet<long> parkingRoomIds = snapshot.Items.Where(i => i.Included && i.Role == TepRoles.Parking && i.Area > 0)
    .Select(i => i.ElementId).ToHashSet();
foreach (TepItem parking in snapshot.Items.Where(i => i.Kind == "Машиноместо"))
{
    if (!containment.TryGetValue(parking.ElementId, out long[]? roomIds))
        throw new InvalidDataException("No Revit containment evidence for " + parking.ElementId);
    if (roomIds.Any(id => !parkingRoomIds.Contains(id)))
        throw new InvalidDataException("Parking containment refers to an unselected room.");
    TepClassifier.ParkingScope(parking, roomIds.Length > 0, string.Join(", ", roomIds));
}
snapshot.Issues.Add("Отчёт сформирован повторным расчётом сохранённого снимка Revit. Геометрическая принадлежность машиномест взята из проверки Room.IsPointInRoom в Revit 2024.");
TepOptions options = new();
TepCalculation calculation = new TepCalculator().Calculate(snapshot, options);
new TepExcel().Export(args[2], args[3] + ".xlsx", snapshot, options, calculation);
JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
File.WriteAllText(args[3] + ".snapshot.json", JsonSerializer.Serialize(snapshot, jsonOptions));
File.WriteAllText(args[3] + ".result.json", JsonSerializer.Serialize(calculation, jsonOptions));
foreach (TepResultRow row in calculation.Rows)
    Console.WriteLine($"{row.Metric.Row}: {row.Name} | {row.B1} | {row.Stylobate} | {row.B2} | {row.Total}");
