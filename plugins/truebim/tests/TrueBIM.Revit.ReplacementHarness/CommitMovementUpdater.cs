using Autodesk.Revit.DB;

namespace TrueBIM.Revit.ReplacementHarness;

// Simulate a host/updater moving the new instance during Commit after it is pinned.
internal sealed class CommitMovementUpdater : IUpdater
{
    private readonly UpdaterId id;
    public CommitMovementUpdater(AddInId addInId) => id = new UpdaterId(addInId, new Guid("8D204361-3484-4819-B047-F44125B3F311"));
    public bool Enabled { get; set; }
    public bool Moved { get; private set; }
    public void Execute(UpdaterData data)
    {
        if (!Enabled || Moved) return;
        Document document = data.GetDocument();
        foreach (ElementId elementId in data.GetModifiedElementIds().Concat(data.GetAddedElementIds()))
        {
            if (document.GetElement(elementId) is not FamilyInstance instance || !instance.Pinned) continue;
            instance.Pinned = false;
            XYZ point = ((LocationPoint)instance.Location).Point;
            ElementTransformUtils.MoveElement(document, elementId, new XYZ(0.25, 0, 0));
            ElementTransformUtils.RotateElement(document, elementId, Line.CreateUnbound(point, XYZ.BasisZ), 0.25);
            Moved = true;
        }
    }
    public string GetAdditionalInformation() => "Replacement regression: commit movement";
    public ChangePriority GetChangePriority() => ChangePriority.FreeStandingComponents;
    public UpdaterId GetUpdaterId() => id;
    public string GetUpdaterName() => "TrueBIM replacement commit regression";
}
