using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.Print.Models;

public sealed class DwgExportProfile
{
    public const string DefaultProfileName = "Текущие настройки DWG";

    public string ProfileName { get; set; } = DefaultProfileName;

    public string? SourceRevitSetupName { get; set; }

    public bool UsePredefinedRevitSetup { get; set; } = true;

    public bool IsUserProfile { get; set; }

    public ACADVersion FileVersion { get; set; }

    public ExportColorMode Colors { get; set; }

    public PropOverrideMode PropOverrides { get; set; }

    public ExportUnit TargetUnit { get; set; }

    public bool SharedCoords { get; set; }

    public bool ExportingAreas { get; set; }

    public bool MergedViews { get; set; }

    public bool HideScopeBox { get; set; }

    public bool HideReferencePlane { get; set; }

    public bool HideUnreferenceViewTags { get; set; }

    public bool PreserveCoincidentLines { get; set; }

    public SolidGeometry ExportOfSolids { get; set; }

    public LineScaling LineScaling { get; set; }

    public TextTreatment TextTreatment { get; set; }

    public string? LayerMapping { get; set; }

    public string? LinetypesFileName { get; set; }

    public string? HatchPatternsFileName { get; set; }

    public bool MarkNonplotLayers { get; set; }

    public string? NonplotSuffix { get; set; }

    public bool UseHatchBackgroundColor { get; set; }

    public DwgExportColor HatchBackgroundColor { get; set; } = DwgExportColor.White;

    public DwgExportProfile Clone()
    {
        return new DwgExportProfile
        {
            ProfileName = ProfileName,
            SourceRevitSetupName = SourceRevitSetupName,
            UsePredefinedRevitSetup = UsePredefinedRevitSetup,
            IsUserProfile = IsUserProfile,
            FileVersion = FileVersion,
            Colors = Colors,
            PropOverrides = PropOverrides,
            TargetUnit = TargetUnit,
            SharedCoords = SharedCoords,
            ExportingAreas = ExportingAreas,
            MergedViews = MergedViews,
            HideScopeBox = HideScopeBox,
            HideReferencePlane = HideReferencePlane,
            HideUnreferenceViewTags = HideUnreferenceViewTags,
            PreserveCoincidentLines = PreserveCoincidentLines,
            ExportOfSolids = ExportOfSolids,
            LineScaling = LineScaling,
            TextTreatment = TextTreatment,
            LayerMapping = LayerMapping,
            LinetypesFileName = LinetypesFileName,
            HatchPatternsFileName = HatchPatternsFileName,
            MarkNonplotLayers = MarkNonplotLayers,
            NonplotSuffix = NonplotSuffix,
            UseHatchBackgroundColor = UseHatchBackgroundColor,
            HatchBackgroundColor = HatchBackgroundColor
        };
    }
}
