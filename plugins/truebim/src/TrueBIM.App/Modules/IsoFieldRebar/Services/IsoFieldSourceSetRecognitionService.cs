using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSourceSetRecognitionService
{
    public IsoFieldRecognitionResult Run(
        IsoFieldSourceSet sourceSet,
        IIsoFieldRecognitionRunner recognitionRunner)
    {
        if (sourceSet is null)
        {
            throw new ArgumentNullException(nameof(sourceSet));
        }

        if (recognitionRunner is null)
        {
            throw new ArgumentNullException(nameof(recognitionRunner));
        }

        if (!sourceSet.IsComplete)
        {
            throw new InvalidOperationException(
                $"IsoField source set is incomplete: {string.Join(" ", sourceSet.ValidationMessages)}");
        }

        List<IsoFieldPolyline> polylines = new();
        List<string> diagnostics = new();
        List<IsoFieldLegend> legends = new();
        foreach (IsoFieldLayerRole role in IsoFieldSourceSet.RequiredRoles)
        {
            IsoFieldSourceFile sourceFile = sourceSet.GetFile(role);
            IsoFieldRecognitionResult result = recognitionRunner.Run(sourceFile.FilePath);
            polylines.AddRange(result.Polylines.Select(polyline => new IsoFieldPolyline(
                $"{role}:{polyline.Id}",
                polyline.Points,
                polyline.ZoneName,
                polyline.Confidence,
                role,
                polyline.LegendBandIndex)));
            diagnostics.AddRange(result.Diagnostics.Select(message => $"[{role}] {message}"));
            legends.AddRange(result.EffectiveLegends.Select(legend => legend with { LayerRole = role }));
        }

        return new IsoFieldRecognitionResult(polylines, diagnostics, legends);
    }
}
