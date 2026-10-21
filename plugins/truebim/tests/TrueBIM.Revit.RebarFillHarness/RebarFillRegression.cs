using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarFill.Revit;

namespace TrueBIM.Revit.RebarFillHarness;

internal static class RebarFillRegression
{
    public static IReadOnlyList<object> Run(Document document)
    {
        List<object> results = new();
        RebarFillService service = new();
        List<Element> all = new FilteredElementCollector(document).WhereElementIsNotElementType()
            .Where(RebarFillService.IsReinforcement).ToList();
        List<Element> nested = all.OfType<FamilyInstance>().Where(e => e.SuperComponent != null).Cast<Element>().ToList();
        Test("Nested_selection_preserves_parent_and_unselected", () =>
        {
            Require(nested.Count >= 2, "No nested reinforcement fixture");
            Element target = nested[0];
            RebarFillParameter option = service.CollectParameters(new[] { target }).First(p => p.WritableCount > 0 && p.Kind == "Текст");
            Dictionary<int, string?> before = all.ToDictionary(e => e.Id.IntegerValue, e => RebarFillService.Find(e, option.Key)?.AsString());
            int parent = ((FamilyInstance)target).SuperComponent.Id.IntegerValue;
            using TransactionGroup fixture = new(document, "Rebar fill regression rollback");
            fixture.Start();
            try
            {
                var rows = service.Apply(document, new[] { target }, option, "TrueBIM_selected_only", false);
                Require(rows.Single().Status == "Записано", string.Join("; ", rows.Select(r => r.Status)));
                Require(RebarFillService.Find(target, option.Key)?.AsString() == "TrueBIM_selected_only", "Readback failed");
                Require(((FamilyInstance)target).SuperComponent.Id.IntegerValue == parent, "Parent changed");
                Require(all.Where(e => e.Id != target.Id).All(e => RebarFillService.Find(e, option.Key)?.AsString() == before[e.Id.IntegerValue]), "Unselected parameter changed");
                Require(service.Apply(document, new[] { target }, option, "TrueBIM_selected_only", false).Single().Status == "Уже задано", "Repeated fill changed value");
                Require(service.Apply(document, new[] { target }, option, "do_not_write", true).Single().Status == "Пропущен: уже заполнен", "Only-empty overwrote value");
            }
            finally { fixture.RollBack(); }
            Require(all.All(e => RebarFillService.Find(e, option.Key)?.AsString() == before[e.Id.IntegerValue]), "Rollback did not restore model");
        });
        Test("Parent_selection_and_deduplication", () =>
        {
            FamilyInstance parent = nested.OfType<FamilyInstance>().Select(e => e.SuperComponent).OfType<FamilyInstance>().First(RebarFillService.IsGenericModel);
            var descendants = service.ResolveTargets(document, new[] { parent.Id });
            Require(descendants.Count > 0, "Parent has no targets");
            var deduplicated = service.ResolveTargets(document, new[] { parent.Id, parent.Id, descendants[0].Id });
            Require(descendants.Select(e => e.Id.IntegerValue).SequenceEqual(deduplicated.Select(e => e.Id.IntegerValue)), "Duplicate selection changes scope");
            Require(service.ResolveTargets(document, new[] { descendants[0].Id }).Count == 1, "Selecting child expands to siblings");
        });
        Test("Readonly_parameter_is_not_written", () =>
        {
            Element target = nested.First();
            RebarFillParameter option = service.CollectParameters(new[] { target }).First(p => p.WritableCount == 0);
            var rows = service.Apply(document, new[] { target }, option, "must_not_write", false);
            Require(!rows.Single().CanWrite && rows.Single().Status != "Записано", "Readonly parameter reported as written");
        });
        Test("Empty_string_fill_and_numeric_zero", () =>
        {
            Element target = nested.First();
            RebarFillParameter option = service.CollectParameters(new[] { target }).First(p => p.WritableCount > 0 && p.Kind == "Текст");
            using TransactionGroup fixture = new(document, "Rebar fill empty fixture rollback");
            fixture.Start();
            try
            {
                service.Apply(document, new[] { target }, option, "", false);
                Require(service.Apply(document, new[] { target }, option, "filled_empty", true).Single().Status == "Записано", "Empty string was not filled");
                Parameter? integer = target.Parameters.Cast<Parameter>().FirstOrDefault(p => p.StorageType == StorageType.Integer && !p.IsReadOnly && !p.HasValue);
                Require(integer != null, "No unset writable integer fixture");
                if (integer != null)
                {
                    RebarFillParameter number = service.CollectParameters(new[] { target }).Single(p => p.Key == RebarFillService.ParameterKey(integer));
                    var zero = service.Apply(document, new[] { target }, number, "0", false);
                    Require(zero.Single().Status == "Записано" && integer.AsInteger() == 0, "Zero was not explicitly written");
                    Require(!RebarFillService.IsEmpty(integer), "Numeric zero treated as empty");
                    Require(service.Apply(document, new[] { target }, number, "1", true).Single().Status == "Пропущен: уже заполнен", "Only-empty overwrote zero");
                    bool rejected = false;
                    try { service.Apply(document, new[] { target }, number, "invalid", false); }
                    catch (ArgumentException) { rejected = true; }
                    Require(rejected, "Invalid number accepted");
                }
                Parameter? real = target.Parameters.Cast<Parameter>().FirstOrDefault(p => p.StorageType == StorageType.Double && !p.IsReadOnly && !p.HasValue);
                Require(real != null, "No unset writable double fixture");
                if (real != null)
                {
                    RebarFillParameter number = service.CollectParameters(new[] { target }).Single(p => p.Key == RebarFillService.ParameterKey(real));
                    var zero = service.Apply(document, new[] { target }, number, "0", false);
                    Require(zero.Single().Status == "Записано" && real.HasValue && real.AsDouble() == 0, "Double zero was not explicitly written");
                    Require(service.Apply(document, new[] { target }, number, "1", true).Single().Status == "Пропущен: уже заполнен", "Only-empty overwrote double zero");
                }
            }
            finally { fixture.RollBack(); }
        });
        Test("Area_reinforcement_preserves_system", () =>
        {
            RebarInSystem target = all.OfType<RebarInSystem>().First();
            ElementId system = target.SystemId;
            RebarFillParameter option = service.CollectParameters(new[] { target }).First(p => p.WritableCount > 0 && p.Kind == "Текст");
            var siblings = all.OfType<RebarInSystem>().Where(e => e.Id != target.Id).ToList();
            var before = siblings.ToDictionary(e => e.Id.IntegerValue, e => RebarFillService.Find(e, option.Key)?.AsString());
            using TransactionGroup fixture = new(document, "System rebar fill rollback");
            fixture.Start();
            try
            {
                var rows = service.Apply(document, new[] { target }, option, "TrueBIM_in_system", false);
                Require(rows.Single().Status == "Записано", rows.Single().Status);
                Require(target.SystemId == system && document.GetElement(system) != null, "System membership changed");
                Require(siblings.All(e => RebarFillService.Find(e, option.Key)?.AsString() == before[e.Id.IntegerValue]), "Unselected system rebar changed");
            }
            finally { fixture.RollBack(); }
        });
        return results;

        void Test(string name, Action run)
        {
            try { run(); results.Add(new { Scenario = name, Passed = true }); }
            catch (Exception ex) { results.Add(new { Scenario = name, Passed = false, Error = ex.ToString() }); }
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
