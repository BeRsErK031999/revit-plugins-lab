using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class DraftingTableService
{
    private const double TextPaddingFeet = 1.8 / 304.8;
    private static readonly HashSet<ViewType> CompatibleViewTypes =
    [
        ViewType.DraftingView,
        ViewType.FloorPlan,
        ViewType.CeilingPlan,
        ViewType.EngineeringPlan,
        ViewType.AreaPlan,
        ViewType.Section,
        ViewType.Elevation,
        ViewType.Detail
    ];

    private readonly DraftingTableLayoutService layoutService;
    private readonly ParsedTableValidationService validationService;
    private readonly ITrueBimLogger logger;

    public DraftingTableService(
        DraftingTableLayoutService layoutService,
        ParsedTableValidationService validationService,
        ITrueBimLogger logger)
    {
        this.layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DraftingTableCreationResult CreateTable(
        Document document,
        View activeView,
        ParsedTable table,
        ImportOptions options)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(options, nameof(options));

        ParsedTableValidationResult validation = validationService.Validate(table);
        if (!validation.Succeeded)
        {
            return new DraftingTableCreationResult(
                activeView.Name,
                false,
                0,
                0,
                Array.Empty<string>(),
                validation.Warnings,
                validation.Errors);
        }

        List<string> warnings = [.. validation.Warnings, .. table.Warnings];
        List<string> skippedCells = [];
        int createdLineCount = 0;
        int createdTextCount = 0;
        bool createdNewView = false;
        View targetView = activeView;

        using Transaction transaction = new(document, "TrueBIM: импорт таблицы");
        transaction.Start();
        try
        {
            targetView = ResolveTargetView(document, activeView, options, warnings, out createdNewView);
            if (!IsDraftingCompatible(targetView))
            {
                transaction.RollBack();
                return new DraftingTableCreationResult(
                    targetView.Name,
                    false,
                    0,
                    0,
                    skippedCells,
                    warnings,
                    ["Текущий вид не поддерживает визуальное размещение таблицы. Включите создание нового чертёжного вида."]);
            }

            ElementId textTypeId = ResolveTextTypeId(document, options);
            if (textTypeId == ElementId.InvalidElementId)
            {
                transaction.RollBack();
                return new DraftingTableCreationResult(
                    targetView.Name,
                    createdNewView,
                    0,
                    0,
                    skippedCells,
                    warnings,
                    ["В документе не найден тип TextNote для создания текста таблицы."]);
            }

            DraftingTableLayout layout = layoutService.CreateLayout(table, options.TableScale);
            XYZ origin = XYZ.Zero;
            XYZ right = ResolveDirection(targetView.RightDirection, XYZ.BasisX);
            XYZ up = ResolveDirection(targetView.UpDirection, XYZ.BasisY);
            Element? lineStyle = ResolveLineStyle(document, options);

            createdLineCount += CreateGridLines(document, targetView, layout, origin, right, up, lineStyle);
            foreach (DraftingTableCellLayout cellLayout in layout.Cells)
            {
                string text = cellLayout.Cell.Text?.Trim() ?? string.Empty;
                if (text.Length == 0)
                {
                    skippedCells.Add($"[{cellLayout.Cell.RowIndex + 1}; {cellLayout.Cell.ColumnIndex + 1}] пустая ячейка");
                    continue;
                }

                XYZ point = ToPoint(
                    origin,
                    right,
                    up,
                    cellLayout.XFeet + TextPaddingFeet,
                    cellLayout.YFeet + TextPaddingFeet);
                double width = Math.Max(0.05, cellLayout.WidthFeet - TextPaddingFeet * 2);
                TextNoteOptions textOptions = new(textTypeId)
                {
                    HorizontalAlignment = HorizontalTextAlignment.Left,
                    VerticalAlignment = VerticalTextAlignment.Top
                };
                TextNote.Create(document, targetView.Id, point, width, text, textOptions);
                createdTextCount++;
            }

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        logger.Info($"Schedule Import created a drafting table on '{targetView.Name}'. Lines: {createdLineCount}; text notes: {createdTextCount}; skipped cells: {skippedCells.Count}.");
        return new DraftingTableCreationResult(
            targetView.Name,
            createdNewView,
            createdLineCount,
            createdTextCount,
            skippedCells,
            warnings.Distinct().ToList(),
            Array.Empty<string>());
    }

    public static bool IsDraftingCompatible(View view)
    {
        return !view.IsTemplate && CompatibleViewTypes.Contains(view.ViewType);
    }

    private static View ResolveTargetView(
        Document document,
        View activeView,
        ImportOptions options,
        List<string> warnings,
        out bool createdNewView)
    {
        createdNewView = false;
        View? targetView = options.TargetViewId is > 0
            ? document.GetElement(RevitElementIds.Create(options.TargetViewId.Value)) as View
            : activeView;

        targetView ??= activeView;
        if (IsDraftingCompatible(targetView))
        {
            return targetView;
        }

        if (!options.CreateNewViewIfNeeded)
        {
            return targetView;
        }

        ViewFamilyType viewFamilyType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.Drafting)
            ?? throw new InvalidOperationException("В документе не найден тип чертёжного вида.");
        ViewDrafting draftingView = ViewDrafting.Create(document, viewFamilyType.Id);
        draftingView.Name = CreateUniqueViewName(document, "TrueBIM_Импорт таблицы");
        createdNewView = true;
        warnings.Add("Таблица создана на новом чертёжном виде, потому что активный вид не подходит для Drafting Table Mode.");
        return draftingView;
    }

    private static string CreateUniqueViewName(Document document, string baseName)
    {
        HashSet<string> names = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        string candidate = $"{baseName} {DateTime.Now:yyyy-MM-dd HH-mm}";
        if (!names.Contains(candidate))
        {
            return candidate;
        }

        for (int index = 2; index < 1000; index++)
        {
            string indexed = $"{candidate} ({index})";
            if (!names.Contains(indexed))
            {
                return indexed;
            }
        }

        return $"{candidate} {Guid.NewGuid():N}";
    }

    private static ElementId ResolveTextTypeId(Document document, ImportOptions options)
    {
        if (options.TextNoteTypeId is > 0)
        {
            return RevitElementIds.Create(options.TextNoteTypeId.Value);
        }

        Element? textNoteType = new FilteredElementCollector(document)
            .OfClass(typeof(TextNoteType))
            .FirstElement();
        return textNoteType?.Id ?? ElementId.InvalidElementId;
    }

    private static Element? ResolveLineStyle(Document document, ImportOptions options)
    {
        if (options.LineStyleId is not > 0)
        {
            return null;
        }

        return document.GetElement(RevitElementIds.Create(options.LineStyleId.Value));
    }

    private static int CreateGridLines(
        Document document,
        View targetView,
        DraftingTableLayout layout,
        XYZ origin,
        XYZ right,
        XYZ up,
        Element? lineStyle)
    {
        int created = 0;
        double width = layout.WidthFeet;
        double height = layout.HeightFeet;
        double x = 0;
        foreach (double columnWidth in layout.ColumnWidthsFeet.Append(0))
        {
            DetailCurve? curve = CreateDetailCurve(
                document,
                targetView,
                ToPoint(origin, right, up, x, 0),
                ToPoint(origin, right, up, x, height),
                lineStyle);
            if (curve is not null)
            {
                created++;
            }

            x += columnWidth;
        }

        double y = 0;
        foreach (double rowHeight in layout.RowHeightsFeet.Append(0))
        {
            DetailCurve? curve = CreateDetailCurve(
                document,
                targetView,
                ToPoint(origin, right, up, 0, y),
                ToPoint(origin, right, up, width, y),
                lineStyle);
            if (curve is not null)
            {
                created++;
            }

            y += rowHeight;
        }

        return created;
    }

    private static DetailCurve? CreateDetailCurve(
        Document document,
        View targetView,
        XYZ start,
        XYZ end,
        Element? lineStyle)
    {
        if (start.DistanceTo(end) < 1e-6)
        {
            return null;
        }

        DetailCurve curve = document.Create.NewDetailCurve(targetView, Line.CreateBound(start, end));
        if (lineStyle is not null)
        {
            curve.LineStyle = lineStyle;
        }

        return curve;
    }

    private static XYZ ToPoint(XYZ origin, XYZ right, XYZ up, double xFeet, double yFeet)
    {
        return origin + right.Multiply(xFeet) - up.Multiply(yFeet);
    }

    private static XYZ ResolveDirection(XYZ direction, XYZ fallback)
    {
        if (direction.GetLength() < 1e-9)
        {
            return fallback;
        }

        return direction.Normalize();
    }
}
