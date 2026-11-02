using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace ModelSheetPublisher
{
    public sealed class QueueFile
    {
        public int Version { get; set; } = 1;
        public List<Sheet> Sheets { get; set; } = new List<Sheet>();
        public PublishOptions Options { get; set; } = new PublishOptions();
    }
    public static class QueueStore
    {
        public static void Save(string path, QueueFile data)
        {
            using (var writer = new StreamWriter(path)) new XmlSerializer(typeof(QueueFile)).Serialize(writer, data);
        }
        public static QueueFile Load(string path)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 };
            using (var reader = System.Xml.XmlReader.Create(path, settings))
            {
                var data = (QueueFile)new XmlSerializer(typeof(QueueFile)).Deserialize(reader);
                if (data.Version != 1 || data.Sheets == null || data.Options == null || data.Sheets.Count > 5000) throw new InvalidDataException("Неподдерживаемый файл очереди.");
                foreach (var sheet in data.Sheets)
                {
                    if (sheet == null || sheet.Window == null || string.IsNullOrWhiteSpace(sheet.Source)) throw new InvalidDataException("В очереди нет исходного DWG или области.");
                    sheet.Validate();
                }
                return data;
            }
        }
    }
}
