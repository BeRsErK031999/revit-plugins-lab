using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSourceSetRecognitionService
{
    private const double MaximumBoundsDeviationPixels = 3;

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
                $"Комплект карт не готов. {string.Join(" ", sourceSet.ValidationMessages)}");
        }

        List<IsoFieldPolyline> polylines = new();
        List<string> diagnostics = new();
        List<IsoFieldLegend> legends = new();
        List<IsoFieldImageBounds> calculationBounds = new();
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
            if (result.CalculationBounds is { IsValid: true } bounds)
            {
                calculationBounds.Add(bounds);
            }
        }

        IsoFieldImageBounds? consensusBounds = ResolveCalculationBounds(calculationBounds, diagnostics);
        return new IsoFieldRecognitionResult(polylines, diagnostics, legends, consensusBounds);
    }

    private static IsoFieldImageBounds? ResolveCalculationBounds(
        IReadOnlyList<IsoFieldImageBounds> detectedBounds,
        ICollection<string> diagnostics)
    {
        if (detectedBounds.Count == 0)
        {
            return null;
        }

        if (detectedBounds.Count < 3)
        {
            diagnostics.Add(
                $"Границы расчётного поля найдены только на {detectedBounds.Count} из 4 карт; "
                + "контрольные точки предложены по крайним распознанным зонам.");
            return null;
        }

        IsoFieldImageBounds median = new(
            Median(detectedBounds.Select(bounds => bounds.MinimumX)),
            Median(detectedBounds.Select(bounds => bounds.MinimumY)),
            Median(detectedBounds.Select(bounds => bounds.MaximumX)),
            Median(detectedBounds.Select(bounds => bounds.MaximumY)));
        IsoFieldImageBounds[] agreeingBounds = detectedBounds
            .Where(bounds => IsWithinTolerance(bounds, median))
            .ToArray();
        if (agreeingBounds.Length < 3)
        {
            diagnostics.Add(
                "Границы расчётного поля различаются между картами; "
                + "контрольные точки предложены по крайним распознанным зонам.");
            return null;
        }

        IsoFieldImageBounds consensus = new(
            Median(agreeingBounds.Select(bounds => bounds.MinimumX)),
            Median(agreeingBounds.Select(bounds => bounds.MinimumY)),
            Median(agreeingBounds.Select(bounds => bounds.MaximumX)),
            Median(agreeingBounds.Select(bounds => bounds.MaximumY)));
        diagnostics.Add(
            $"Границы расчётного поля согласованы по {agreeingBounds.Length} картам: "
            + $"X {consensus.MinimumX:0.###}–{consensus.MaximumX:0.###}, "
            + $"Y {consensus.MinimumY:0.###}–{consensus.MaximumY:0.###}.");
        return consensus;
    }

    private static bool IsWithinTolerance(IsoFieldImageBounds bounds, IsoFieldImageBounds reference)
    {
        return Math.Abs(bounds.MinimumX - reference.MinimumX) <= MaximumBoundsDeviationPixels
            && Math.Abs(bounds.MinimumY - reference.MinimumY) <= MaximumBoundsDeviationPixels
            && Math.Abs(bounds.MaximumX - reference.MaximumX) <= MaximumBoundsDeviationPixels
            && Math.Abs(bounds.MaximumY - reference.MaximumY) <= MaximumBoundsDeviationPixels;
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] ordered = values.OrderBy(value => value).ToArray();
        int middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }
}
