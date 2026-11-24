using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace TrueBIM.App.Modules.SystemDesignation;

internal static class SystemDesignationValues
{
    public static IEnumerable<Connector> ServiceConnectors(FamilyInstance family)
    {
        ConnectorManager? manager = family.MEPModel?.ConnectorManager;
        if (manager == null) yield break;
        foreach (Connector connector in manager.Connectors)
            if (connector.Domain is Domain.DomainHvac or Domain.DomainPiping && connector.ConnectorType != ConnectorType.Logical)
                yield return connector;
    }

    public static string Read(Element source)
    {
        // Equipment can have several systems (and electrical ports) on the same instance.
        if (source is FamilyInstance family)
        {
            List<string> abbreviations = [];
            foreach (Connector connector in ServiceConnectors(family))
            {
                var system = connector.MEPSystem;
                // On some equipment the system is exposed on the physically mated port.
                if (system == null && connector.IsConnected)
                {
                    foreach (Connector mate in connector.AllRefs)
                    {
                        if (mate.Owner.Id == source.Id || mate.Domain != connector.Domain ||
                            mate.ConnectorType == ConnectorType.Logical || !connector.IsConnectedTo(mate)) continue;
                        AddSystem(mate.MEPSystem);
                    }
                }
                else AddSystem(system);
            }

            void AddSystem(MEPSystem? system)
            {
                if (system is not (MechanicalSystem or PipingSystem)) return;
                if (source.Document.GetElement(system.GetTypeId()) is MEPSystemType type)
                    abbreviations.Add(type.Abbreviation);
            }
            if (abbreviations.Count > 0) return SystemAbbreviations.Format(abbreviations);
        }
        string raw = source.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "";
        return SystemAbbreviations.Format(raw.Split(','));
    }
}
