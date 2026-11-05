using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace TrueBIM.App.Modules.GeometricFinishes;

public enum FinishKind { Floors, Walls, Ceilings }
public enum FinishScope { Project, ActiveView, Selected, Level }
public enum FinishElevation { Level, RoomBoundary, Slab }
public enum FinishGrouping { RoomName, WallType, RoomAndWallType }

public sealed class FinishAssignment
{
    public string Key { get; set; } = "";
    public string TypeUniqueId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool RoomBounding { get; set; }
}

public sealed class FinishSettings
{
    public FinishKind Kind { get; set; }
    public FinishScope Scope { get; set; } = FinishScope.ActiveView;
    public string LevelUniqueId { get; set; } = "";
    public string RoomFilter { get; set; } = "";
    public FinishGrouping Grouping { get; set; } = FinishGrouping.RoomName;
    public FinishElevation Elevation { get; set; } = FinishElevation.RoomBoundary;
    public double OffsetMm { get; set; }
    public bool JoinWalls { get; set; } = true;
    public bool CeilingAsFloor { get; set; }
    public bool LimitThickness { get; set; } = true;
    public double MaxThicknessMm { get; set; } = 150;
    public string TypeParameterKey { get; set; } = "";
    public string TypeParameterValue { get; set; } = "";
    public string RoomParameterKey { get; set; } = "";
    public string FinishParameterKey { get; set; } = "";
    public List<FinishAssignment> Assignments { get; set; } = new();

    public string? Validate()
    {
        if (!Enum.IsDefined(typeof(FinishKind), Kind) || !Enum.IsDefined(typeof(FinishScope), Scope)
            || !Enum.IsDefined(typeof(FinishElevation), Elevation) || !Enum.IsDefined(typeof(FinishGrouping), Grouping))
            return "Некорректный вариант настроек. Выберите параметры заново.";
        if (!IsFinite(OffsetMm) || Math.Abs(OffsetMm) > 100000)
            return "Смещение должно быть числом в пределах ±100 000 мм.";
        if (LimitThickness && (!IsFinite(MaxThicknessMm) || MaxThicknessMm <= 0))
            return "Максимальная толщина должна быть положительным числом.";
        if (Scope == FinishScope.Level && string.IsNullOrWhiteSpace(LevelUniqueId))
            return "Выберите этаж.";
        if (string.IsNullOrEmpty(RoomParameterKey) != string.IsNullOrEmpty(FinishParameterKey))
            return "Для привязки выберите оба параметра: помещения и отделки, либо отключите привязку.";
        return null;
    }
    public static bool IsFinite(double number) => !double.IsNaN(number) && !double.IsInfinity(number);

    public static string AssignmentKey(FinishGrouping grouping, string roomName, string wallTypeKey)
    {
        // Length prefixes make keys unambiguous even when names contain separators.
        string room = roomName.Length + ":" + roomName;
        return grouping switch
        {
            FinishGrouping.WallType => "wall:" + wallTypeKey,
            FinishGrouping.RoomAndWallType => "room-wall:" + room + wallTypeKey,
            _ => "room:" + room
        };
    }
}

public static class FinishSettingsStore
{
    public static string PathFor(string documentKey, FinishKind kind)
    {
        using SHA256 hash = SHA256.Create();
        string key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(documentKey))).Replace("-", "").Substring(0, 20);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrueBIM", "GeometricFinishes", key + "-" + kind + ".xml");
    }
    public static FinishSettings Load(string path, FinishKind kind, out string? warning)
    {
        warning = null;
        try
        {
            if (File.Exists(path))
            {
                using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
                FinishSettings? saved = new XmlSerializer(typeof(FinishSettings)).Deserialize(reader) as FinishSettings;
                if (saved != null && saved.Kind == kind && saved.Validate() == null)
                {
                    saved.Assignments ??= new List<FinishAssignment>();
                    return saved;
                }
                warning = "Сохранённые настройки некорректны; загружены начальные значения.";
            }
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is System.Xml.XmlException || ex is UnauthorizedAccessException)
        { warning = "Не удалось прочитать настройки: " + ex.Message; }
        return new FinishSettings { Kind = kind, Grouping = kind == FinishKind.Walls ? FinishGrouping.WallType : FinishGrouping.RoomName };
    }
    public static void Save(string path, FinishSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = File.Create(temporary)) new XmlSerializer(typeof(FinishSettings)).Serialize(stream, settings);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
