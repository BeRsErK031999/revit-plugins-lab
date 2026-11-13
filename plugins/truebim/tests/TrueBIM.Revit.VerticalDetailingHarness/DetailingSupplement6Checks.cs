using Autodesk.Revit.DB;
using TrueBIM.App.Modules.RebarDetailing;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication
{
    private static object CheckSessionSettings(Type windowType, Document doc, object rows, object service)
    {
        const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        const System.Reflection.BindingFlags statics = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var savedFields = windowType.GetFields(statics).Where(f => f.Name.StartsWith("last", StringComparison.Ordinal)).ToList();
        var previous = savedFields.Select(f => f.GetValue(null)).ToList();
        System.Windows.Window Create() => (System.Windows.Window)Activator.CreateInstance(windowType, doc, rows, service)!;
        string Snapshot(System.Windows.Window window)
        {
            object options = windowType.GetProperty("Options")!.GetValue(window)!;
            return string.Join("|", options.GetType().GetProperties().OrderBy(p => p.Name).Select(p => p.Name + "=" + p.GetValue(options)));
        }
        IEnumerable<System.Windows.DependencyObject> Children(System.Windows.DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Children(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        try
        {
            foreach (var field in savedFields) field.SetValue(null, null);
            foreach (object row in (System.Collections.IEnumerable)rows) row.GetType().GetProperty("Selected")!.SetValue(row, true);
            var first = Create();
            string defaults = Snapshot(first);
            foreach (var field in windowType.GetFields(instance))
            {
                if (field.GetValue(first) is System.Windows.Controls.CheckBox check) check.IsChecked = !check.IsChecked.GetValueOrDefault();
                if (field.GetValue(first) is System.Windows.Controls.TextBox text) text.Text = field.Name + " проверка";
                if (field.GetValue(first) is System.Windows.Controls.ComboBox combo && combo.Items.Count > 1)
                    combo.SelectedIndex = (combo.SelectedIndex + 1) % combo.Items.Count;
            }
            ((System.Windows.Controls.CheckBox)windowType.GetField("bars", instance)!.GetValue(first)!).IsChecked = true;
            string expected = Snapshot(first);
            first.Loaded += (_, _) => first.Dispatcher.BeginInvoke(new Action(() =>
            {
                var apply = Children(first).OfType<System.Windows.Controls.Button>().Single(b => Children(b)
                    .OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "Оформить выбранные"));
                apply.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }));
            bool applied = first.ShowDialog() == true;
            var second = Create();
            bool restored = Snapshot(second) == expected;
            ((System.Windows.Controls.TextBox)windowType.GetField("upperTop", instance)!.GetValue(second)!).Text = "Отменено";
            second.Close();
            var third = Create(); bool cancelSafe = Snapshot(third) == expected; third.Close();
            foreach (var field in savedFields) field.SetValue(null, null);
            var fresh = Create(); bool defaultsRestored = Snapshot(fresh) == defaults; fresh.Close();
            return new { Passed = applied && restored && cancelSafe && defaultsRestored, Applied = applied,
                Restored = restored, CancelSafe = cancelSafe, DefaultsRestored = defaultsRestored };
        }
        finally
        {
            for (int i = 0; i < savedFields.Count; i++) savedFields[i].SetValue(null, previous[i]);
        }
    }

    private object CheckSupplement6(Document doc, View view, DetailingOptions options)
    {
        var frame = new DetailingFrame(view);
        var geometry = DetailingGeometry.Read(doc, view);
        var sections = (List<DetailingSection>)typeof(RebarDetailingService).GetMethod("Sections",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { frame, geometry, options })!;
        var lowerSlabs = sections.SelectMany(s => s.LowerConcrete).Where(h => h.Element is Floor
            || h.Element.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralFoundation)).ToList();
        var lowerEnds = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => RebarDetailingService.OwnedDetail(f) && f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal)
                && f.Location is LocationCurve c && c.Curve is Line l && Math.Abs(l.Direction.DotProduct(frame.Up)) > 0.999)
            .Select(f => new { X = frame.X(((LocationCurve)f.Location).Curve.Evaluate(0.5, true)),
                Bottom = RebarDetailingService.BreakGraphicPoints(f, view).Select(frame.Y).DefaultIfEmpty(double.MaxValue).Min() })
            .SelectMany(f => lowerSlabs.Where(h => f.X >= h.Bounds.Left && f.X <= h.Bounds.Right
                && Math.Abs(f.Bottom - h.Bounds.Bottom) < 200 / 304.8)
                .Select(h => (h.Bounds.Bottom - f.Bottom) * 304.8)).ToList();
        bool lowerEndsSafe = lowerSlabs.Count == 0 || lowerEnds.Count > 0 && lowerEnds.All(mm => Math.Abs(mm - 80) < 0.1);
        bool localBarsOnly = geometry.Bars.All(b => b.Link == null);
        bool localTagsOnly = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>().Where(t => t.Category?.Id == new ElementId(BuiltInCategory.OST_RebarTags))
            .All(t => t.GetTaggedReferences().All(r => r.LinkedElementId == ElementId.InvalidElementId));
        var face = geometry.ElevationFaces.FirstOrDefault(f => f.ElevationLinePoints.Count > 0);
        if (face == null) return new { Passed = false, LocalBarsOnly = localBarsOnly, LocalTagsOnly = localTagsOnly,
            Error = "No real horizontal face available for fallback test" };
        string before = AnnotationFingerprint(view);
        using TransactionGroup scenario = new(doc, "Supplement 6 elevation fallback and option ownership"); scenario.Start();
        bool fallbackSafe = false, disabledSafe = false, replacementSafe = false;
        string error = "";
        try
        {
            ElementId lineId, spotId;
            using (Transaction transaction = new(doc, "Force unavailable native reference"))
            {
                transaction.Start();
                face.Reference = null!;
                XYZ point = face.ElevationLinePoints[0];
                XYZ end = frame.Point(frame.BreakBoundary.Left - frame.Paper(24), frame.Y(point));
                var method = typeof(RebarDetailingService).GetMethod("CreateElevation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var spot = (SpotDimension)method.Invoke(null, new object[] { doc, view, frame, face, frame.BreakBoundary,
                    point, end + frame.Right * frame.Paper(10), end, new DetailingResult() })!;
                spot.ChangeTypeId(options.SpotTypeId); doc.Regenerate();
                spotId = spot.Id;
                var line = (DetailCurve)doc.GetElement(spot.References.get_Item(0).ElementId);
                lineId = line.Id;
                fallbackSafe = Math.Abs(line.GeometryCurve.Length * 304.8 - 1) < 0.001
                    && line.LineStyle.Name == "Линия_высотная" && RebarDetailingService.OwnedDetail(line)
                    && Math.Abs(line.GeometryCurve.Evaluate(0.5, true).Z - face.Point.Z) < 0.001 / 304.8;
                transaction.Commit();
            }
            var service = new RebarDetailingService();
            var onlyBars = options.Copy(); onlyBars.Elevations = false; onlyBars.Details = false; onlyBars.Pins = false;
            onlyBars.Tags = false; onlyBars.ConcreteDimensions = false; onlyBars.BarDimensions = true;
            onlyBars.WriteQuantities = false;
            var disabled = service.Apply(doc, new[] { view }, onlyBars).Single();
            disabledSafe = disabled.Succeeded && doc.GetElement(lineId) != null && doc.GetElement(spotId) != null;
            onlyBars.BarDimensions = false; onlyBars.Elevations = true;
            var replaced = service.Apply(doc, new[] { view }, onlyBars).Single();
            replacementSafe = replaced.Succeeded && doc.GetElement(lineId) == null && doc.GetElement(spotId) == null;
        }
        catch (Exception ex) { error = ex.ToString(); }
        scenario.RollBack();
        bool rollbackSafe = before == AnnotationFingerprint(view);
        return new { Passed = localBarsOnly && localTagsOnly && fallbackSafe && disabledSafe && replacementSafe && rollbackSafe && lowerEndsSafe,
            LowerEndsSafe = lowerEndsSafe, LowerEndDistancesMm = lowerEnds,
            LocalBarsOnly = localBarsOnly, LocalTagsOnly = localTagsOnly, FallbackSafe = fallbackSafe, DisabledSafe = disabledSafe,
            ReplacementSafe = replacementSafe, RollbackSafe = rollbackSafe, Error = error };
    }
}
