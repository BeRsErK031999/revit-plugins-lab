using TrueBIM.App.Modules.Lintels.Models;

namespace TrueBIM.App.Modules.Lintels.Services;

public sealed class LintelDiagnosticReportBuilder
{
    public LintelDiagnosticResult Build(
        LintelDiagnosticSource source,
        IEnumerable<LintelInstanceSnapshot> instances,
        IEnumerable<LintelExcludedElement>? excludedElements = null,
        IEnumerable<string>? diagnostics = null)
    {
        if (instances is null)
        {
            throw new ArgumentNullException(nameof(instances));
        }

        LintelTypeDiagnostic[] types = instances
            .GroupBy(instance => instance.TypeId)
            .Select(BuildTypeDiagnostic)
            .OrderBy(type => type.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(type => type.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new LintelDiagnosticResult(
            source,
            types,
            (excludedElements ?? Array.Empty<LintelExcludedElement>()).ToArray(),
            (diagnostics ?? Array.Empty<string>()).ToArray());
    }

    private static LintelTypeDiagnostic BuildTypeDiagnostic(IGrouping<long, LintelInstanceSnapshot> group)
    {
        LintelInstanceSnapshot[] instances = group
            .OrderBy(instance => instance.IsAssemblyReady ? 0 : 1)
            .ThenBy(instance => instance.ElementId)
            .ToArray();
        LintelInstanceSnapshot representative = instances[0];
        int readyCount = instances.Count(instance => instance.IsAssemblyReady);
        List<string> diagnostics = [];

        if (readyCount == 0 && instances.Any(instance => instance.HasOwnGeometry))
        {
            diagnostics.Add("геометрия найдена только в родительском экземпляре, но не во вложенных проектных компонентах; пригодность для сборки не подтверждена");
        }
        else if (readyCount == 0 && instances.All(instance => instance.NestedComponents.Count == 0))
        {
            diagnostics.Add("вложенные проектные компоненты не найдены; проверьте, что семейства являются общими");
        }
        else if (readyCount == 0)
        {
            diagnostics.Add("во вложенных компонентах и родителе не найдена модельная геометрия");
        }
        else if (readyCount < instances.Length)
        {
            diagnostics.Add($"часть экземпляров не готова: {instances.Length - readyCount}");
        }

        return new LintelTypeDiagnostic(
            representative.TypeId,
            representative.FamilyName,
            representative.TypeName,
            instances.Length,
            readyCount,
            representative.ElementId,
            representative.HasOwnGeometry,
            representative.NestedComponents.Count,
            representative.GeometryNestedComponentCount,
            representative.AssemblyMemberElementIds,
            diagnostics);
    }
}
