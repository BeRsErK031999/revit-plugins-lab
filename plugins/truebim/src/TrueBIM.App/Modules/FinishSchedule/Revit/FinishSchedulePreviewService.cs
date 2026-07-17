using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishSchedulePreviewService
{
    private readonly FinishElementCollector collector;
    private readonly FinishSchedulePreviewBuilder builder;
    private readonly ITrueBimLogger logger;

    public FinishSchedulePreviewService(
        FinishElementCollector collector,
        FinishSchedulePreviewBuilder builder,
        ITrueBimLogger logger)
    {
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishSchedulePreviewResult Build(
        Document document,
        FinishScheduleSettings settings)
    {
        FinishElementCollection collection = collector.Collect(document, settings);
        FinishSchedulePreviewBuild build = builder.BuildDetailed(collection, settings);
        IFinishQuantitySource quantitySource = new PhysicalFinishQuantitySource(document, logger);
        FinishQuantityResult quantities = quantitySource.Calculate(new FinishQuantityRequest(
            build.RoomScope.SelectedRooms,
            build.Classification.Elements));
        FinishSchedulePreviewResult result = build.Preview.WithQuantities(quantities);
        logger.Info(
            $"Finish Schedule preview built. Rooms={result.RoomScope.SelectedRooms.Count}/{result.CollectedRooms}; "
            + $"Walls={result.Walls.InScope}; Floors={result.Floors.InScope}; Ceilings={result.Ceilings.InScope}; "
            + $"Pairs={result.Index.PotentialRoomElementPairs}; Occurrences={quantities.Occurrences.Count}; "
            + $"GeometryWarnings={quantities.Warnings.Count}.");
        return result;
    }
}
