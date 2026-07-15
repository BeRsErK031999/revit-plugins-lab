using System.ComponentModel;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Services;

namespace TrueBIM.App.Modules.Lintels.UI;

public sealed class LintelTypeSelectionItem : INotifyPropertyChanged
{
    private bool isSelected;

    public LintelTypeSelectionItem(LintelTypeDiagnostic diagnostic)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        ArtifactPreview = LintelArtifactNameBuilder.Build(diagnostic);
        isSelected = diagnostic.IsAssemblyReady;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LintelTypeDiagnostic Diagnostic { get; }

    public LintelArtifactPreview ArtifactPreview { get; }

    public string FamilyName => Diagnostic.FamilyName;

    public string TypeName => Diagnostic.TypeName;

    public int InstanceCount => Diagnostic.InstanceCount;

    public long RepresentativeElementId => Diagnostic.RepresentativeElementId;

    public bool CanSelect => Diagnostic.IsAssemblyReady;

    public string ReadyStatus => CanSelect ? "Готово" : "Заблокировано";

    public string DiagnosticText => Diagnostic.Diagnostics.Count == 0
        ? "Вложенные компоненты с геометрией найдены."
        : string.Join("; ", Diagnostic.Diagnostics);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            bool normalizedValue = CanSelect && value;
            if (isSelected == normalizedValue)
            {
                return;
            }

            isSelected = normalizedValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
