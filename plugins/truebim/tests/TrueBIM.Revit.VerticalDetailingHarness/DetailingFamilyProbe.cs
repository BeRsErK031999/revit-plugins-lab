using System.IO;
using Autodesk.Revit.DB;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private void InspectBreakFamily(Document doc)
    {
        var family = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
            .Single(f => f.Name == "(Узел) Область обрыва • 2");
        Document familyDoc = doc.EditFamily(family);
        try
        {
            var manager = familyDoc.FamilyManager;
            var types = manager.Types.Cast<FamilyType>().Where(t => t.Name == "M50").ToList();
            var data = manager.Parameters.Cast<FamilyParameter>().Select(p => new { Name = p.Definition.Name, p.Formula,
                p.IsInstance, Storage = p.StorageType.ToString(), Values = types.Select(t => new { t.Name,
                    Value = p.StorageType == StorageType.Double ? t.AsDouble(p) : null }).ToList() }).ToList();
            File.WriteAllText(report + ".break-family.json", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data));
            var trials = new List<object>();
            foreach (double arm in new[] { 250.0, 260, 300, 400, 500, 600 })
            foreach (double depth in new[] { 231.0, 500 })
            {
                using TransactionGroup trial = new(familyDoc, "Family dimensions diagnostic"); trial.Start();
                using Transaction change = new(familyDoc, "Set trial dimensions"); change.Start();
                var failure = new FamilyProbeFailures();
                change.SetFailureHandlingOptions(change.GetFailureHandlingOptions().SetFailuresPreprocessor(failure).SetClearAfterRollback(true));
                manager.CurrentType = types.Single();
                manager.Set(manager.get_Parameter("Длина линии 1"), 499.872 / 304.8);
                manager.Set(manager.get_Parameter("Длина линии 2"), arm / 304.8);
                manager.Set(manager.get_Parameter("Глубина маскировки"), depth / 304.8);
                var status = change.Commit();
                trials.Add(new { Arm1Mm = 499.872, Arm2Mm = arm, DepthMm = depth, Status = status.ToString(), failure.Errors });
                trial.RollBack();
                File.WriteAllText(report + ".break-family-trials.json", new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(trials));
            }
        }
        finally { familyDoc.Close(false); }
    }

    private sealed class FamilyProbeFailures : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new();
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor message in accessor.GetFailureMessages())
            {
                if (message.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(message);
                else Errors.Add(message.GetDescriptionText());
            }
            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
