namespace TrueBIM.App.Modules.Lintels.Models;

public enum LintelDiagnosticSource
{
    Selection,
    ActiveView
}

public sealed record LintelNestedComponentSnapshot(
    long ElementId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    bool HasGeometry);

public sealed record LintelInstanceSnapshot(
    long ElementId,
    long TypeId,
    string FamilyName,
    string TypeName,
    bool HasOwnGeometry,
    IReadOnlyList<LintelNestedComponentSnapshot> NestedComponents)
{
    public int GeometryNestedComponentCount => NestedComponents.Count(component => component.HasGeometry);

    public IReadOnlyList<long> AssemblyMemberElementIds => NestedComponents
        .Where(component => component.HasGeometry)
        .Select(component => component.ElementId)
        .Distinct()
        .OrderBy(elementId => elementId)
        .ToArray();

    public bool IsAssemblyReady => GeometryNestedComponentCount > 0;
}

public sealed record LintelExcludedElement(
    long ElementId,
    string Name,
    string Reason);

public sealed record LintelTypeDiagnostic(
    long TypeId,
    string FamilyName,
    string TypeName,
    int InstanceCount,
    int ReadyInstanceCount,
    long RepresentativeElementId,
    bool RepresentativeHasOwnGeometry,
    int RepresentativeNestedComponentCount,
    int RepresentativeGeometryNestedComponentCount,
    IReadOnlyList<long> RepresentativeAssemblyMemberIds,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsAssemblyReady => ReadyInstanceCount > 0;

    public string? ExistingAssemblyName { get; init; }

    public bool HasExistingAssembly => !string.IsNullOrWhiteSpace(ExistingAssemblyName);
}

public sealed record LintelDiagnosticResult(
    LintelDiagnosticSource Source,
    IReadOnlyList<LintelTypeDiagnostic> Types,
    IReadOnlyList<LintelExcludedElement> ExcludedElements,
    IReadOnlyList<string> Diagnostics)
{
    public int InstanceCount => Types.Sum(type => type.InstanceCount);

    public int ReadyTypeCount => Types.Count(type => type.IsAssemblyReady);

    public bool HasCandidates => InstanceCount > 0;

    public string BuildSummary()
    {
        string sourceName = Source == LintelDiagnosticSource.Selection
            ? "текущее выделение"
            : "активный вид";

        if (!HasCandidates)
        {
            return $"Источник: {sourceName}.{Environment.NewLine}Кандидаты не найдены. Исключено элементов: {ExcludedElements.Count}.{Environment.NewLine}Модель Revit не изменялась.";
        }

        return $"Источник: {sourceName}.{Environment.NewLine}Найдено экземпляров: {InstanceCount}; типоразмеров: {Types.Count}; готовы к будущей сборке: {ReadyTypeCount}.{Environment.NewLine}Модель Revit не изменялась.";
    }

    public string BuildDetails(int maxTypes = 12, int maxExcludedElements = 6)
    {
        List<string> sections = [];
        if (Types.Count > 0)
        {
            List<string> typeLines = Types
                .Take(maxTypes)
                .Select(FormatType)
                .ToList();
            if (Types.Count > typeLines.Count)
            {
                typeLines.Add($"… ещё типоразмеров: {Types.Count - typeLines.Count}.");
            }

            sections.Add($"Типоразмеры:{Environment.NewLine}{string.Join(Environment.NewLine, typeLines)}");
        }

        if (Diagnostics.Count > 0)
        {
            sections.Add($"Диагностика:{Environment.NewLine}{string.Join(Environment.NewLine, Diagnostics.Select(message => $"• {message}"))}");
        }

        if (ExcludedElements.Count > 0)
        {
            List<string> excludedLines = ExcludedElements
                .Take(maxExcludedElements)
                .Select(item => $"• ID {item.ElementId}, {item.Name}: {item.Reason}")
                .ToList();
            if (ExcludedElements.Count > excludedLines.Count)
            {
                excludedLines.Add($"… ещё исключено: {ExcludedElements.Count - excludedLines.Count}.");
            }

            sections.Add($"Исключённые элементы:{Environment.NewLine}{string.Join(Environment.NewLine, excludedLines)}");
        }

        return sections.Count == 0
            ? "Дополнительная диагностика отсутствует."
            : string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static string FormatType(LintelTypeDiagnostic type)
    {
        string readiness = type.IsAssemblyReady
            ? $"готовых экземпляров {type.ReadyInstanceCount}/{type.InstanceCount}"
            : "нет экземпляров с геометрией во вложенных проектных компонентах";
        string geometrySource = type.RepresentativeGeometryNestedComponentCount > 0
            ? $"вложенных {type.RepresentativeNestedComponentCount}, с геометрией {type.RepresentativeGeometryNestedComponentCount}"
            : type.RepresentativeHasOwnGeometry
                ? "геометрия находится в родительском экземпляре"
                : $"вложенных {type.RepresentativeNestedComponentCount}, геометрия не найдена";
        string diagnostics = type.Diagnostics.Count == 0
            ? string.Empty
            : $" ({string.Join("; ", type.Diagnostics)})";

        return $"• {type.FamilyName} : {type.TypeName} — экземпляров {type.InstanceCount}, {readiness}; представитель ID {type.RepresentativeElementId}; {geometrySource}{diagnostics}.";
    }
}
