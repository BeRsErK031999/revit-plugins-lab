using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

public static class FamilyReplacementSelectionRunner
{
    public static FamilyReplacementResult Replace(
        UIDocument uiDocument, IReadOnlyList<long> sourceIds, long targetTypeId,
        FamilyReplacementAlignment alignment, long? targetTagTypeId, Action<Exception> onSelectionRestoreFailure)
    {
        long[] capturedSources = sourceIds.Distinct().ToArray();
        return FamilyReplacementSelectionCoordinator.Execute(new SelectionContext(uiDocument),
            () => new FamilyReplacementService().Replace(uiDocument.Document, capturedSources, targetTypeId, alignment, targetTagTypeId),
            onSelectionRestoreFailure);
    }

    private sealed class SelectionContext : IFamilyReplacementSelectionContext
    {
        private readonly UIDocument uiDocument;

        public SelectionContext(UIDocument uiDocument) => this.uiDocument = uiDocument;

        public IReadOnlyList<long> GetSelectedIds() => uiDocument.Selection.GetElementIds().Select(RevitElementIds.GetValue).ToArray();

        public void SetSelectedIds(IReadOnlyList<long> ids) => uiDocument.Selection.SetElementIds(ids.Select(RevitElementIds.Create).ToList());

        public void RefreshView() => uiDocument.RefreshActiveView();

        public bool ElementExists(long id) => uiDocument.Document.IsValidObject && uiDocument.Document.GetElement(RevitElementIds.Create(id)) is not null;
    }
}
