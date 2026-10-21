using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Measures automatic wrapping without inserting line breaks into descriptions.</summary>
internal static class FinishScheduleTextLayout
{
    internal const string BodyTypeName = "TrueBIM • Ведомость отделки • Тело 2,5 мм";

    public static TextNoteType ResolveBodySource(Document document)
    {
        return new FilteredElementCollector(document).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .FirstOrDefault(type => type.Name == BodyTypeName)
            ?? (TextNoteType)document.GetElement(document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType));
    }

    public static Func<string, int> CreateLineCounter(Document document, double widthMillimeters)
    {
        TextNoteType source = ResolveBodySource(document);
        string font = source.get_Parameter(BuiltInParameter.TEXT_FONT).AsString();
        Typeface face = new(new FontFamily(font),
            source.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC).AsInteger() != 0 ? FontStyles.Italic : FontStyles.Normal,
            source.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD).AsInteger() != 0 ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        double capHeight = face.TryGetGlyphTypeface(out GlyphTypeface glyph) && glyph.CapsHeight > 0.1
            ? glyph.CapsHeight : 0.7;
        double em = FinishRoomScheduleStyleRules.BodyTextSizeMillimeters * 96 / 25.4 / capHeight;
        double widthScale = source.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? 1;
        double width = Math.Max(em * 2, (widthMillimeters - 2) * 96 / 25.4 / Math.Max(0.1, widthScale));
        System.Windows.Media.FormattedText Measure(string value) => new(value, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, face, em, Brushes.Black, 1.0) { MaxTextWidth = width };
        double lineHeight = Measure("Аg").Height;
        return value => Math.Max(1, (int)Math.Ceiling(Measure(value).Height / lineHeight - 0.001));
    }
}
