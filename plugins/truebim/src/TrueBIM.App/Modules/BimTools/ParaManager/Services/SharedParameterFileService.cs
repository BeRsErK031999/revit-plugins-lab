using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class SharedParameterFileService
{
    private readonly ITrueBimLogger logger;

    public SharedParameterFileService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DefinitionFile Open(Application application, string sharedParameterPath)
    {
        if (string.IsNullOrWhiteSpace(sharedParameterPath))
        {
            throw new ArgumentException("Shared parameter file path is empty.", nameof(sharedParameterPath));
        }

        if (!System.IO.File.Exists(sharedParameterPath))
        {
            throw new System.IO.FileNotFoundException("Shared parameter file was not found.", sharedParameterPath);
        }

        application.SharedParametersFilename = sharedParameterPath;
        DefinitionFile? definitionFile = application.OpenSharedParameterFile();
        if (definitionFile is null)
        {
            throw new InvalidOperationException("Revit could not open the selected shared parameter file.");
        }

        logger.Info($"Opened shared parameter file: {sharedParameterPath}");
        return definitionFile;
    }

    public ExternalDefinition GetOrCreateDefinition(DefinitionFile definitionFile, ParameterImportRow row, out SharedParameterDefinitionInfo definitionInfo)
    {
        DefinitionGroup group = GetOrCreateGroup(definitionFile, row.SharedGroup);
        ExternalDefinition? existingDefinition = FindDefinition(group, row.ParameterName);
        if (existingDefinition is not null)
        {
            definitionInfo = new SharedParameterDefinitionInfo(
                existingDefinition.Name,
                row.SharedGroup,
                ParameterDataTypeResolver.NormalizeForDisplay(row.DataType),
                existingDefinition.GUID,
                WasCreated: false);
            return existingDefinition;
        }

        row.TryGetVisible(out bool visible);
        row.TryGetUserModifiable(out bool userModifiable);
#if REVIT2022_OR_GREATER
        ExternalDefinitionCreationOptions options = new(row.ParameterName, ParameterDataTypeResolver.ResolveForgeTypeId(row.DataType));
#else
        ExternalDefinitionCreationOptions options = new(row.ParameterName, ParameterDataTypeResolver.ResolveParameterType(row.DataType));
#endif
        options.Description = row.Description;
        options.Visible = visible;
        options.UserModifiable = userModifiable;

        ExternalDefinition definition = (ExternalDefinition)group.Definitions.Create(options);
        definitionInfo = new SharedParameterDefinitionInfo(
            definition.Name,
            row.SharedGroup,
            ParameterDataTypeResolver.NormalizeForDisplay(row.DataType),
            definition.GUID,
            WasCreated: true);
        return definition;
    }

    private static DefinitionGroup GetOrCreateGroup(DefinitionFile definitionFile, string groupName)
    {
        foreach (DefinitionGroup group in definitionFile.Groups)
        {
            if (string.Equals(group.Name, groupName, StringComparison.CurrentCultureIgnoreCase))
            {
                return group;
            }
        }

        return definitionFile.Groups.Create(groupName);
    }

    private static ExternalDefinition? FindDefinition(DefinitionGroup group, string parameterName)
    {
        foreach (Definition definition in group.Definitions)
        {
            if (definition is ExternalDefinition externalDefinition
                && string.Equals(externalDefinition.Name, parameterName, StringComparison.CurrentCultureIgnoreCase))
            {
                return externalDefinition;
            }
        }

        return null;
    }
}
