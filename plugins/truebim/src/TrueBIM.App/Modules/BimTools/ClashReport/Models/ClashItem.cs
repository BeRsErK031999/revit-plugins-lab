using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed class ClashItem : INotifyPropertyChanged
{
    private ClashStatus status;
    private string comment;
    private bool isElement1Resolved;
    private bool isElement2Resolved;
    private string element1Name = string.Empty;
    private string element2Name = string.Empty;
    private string message = string.Empty;
    private NavigationBounds? navigationBounds;

    public ClashItem(
        string clashId,
        string name,
        long? elementId1,
        long? elementId2,
        double? x,
        double? y,
        double? z,
        ClashStatus status,
        string comment,
        string element1SourceName = "",
        string element2SourceName = "",
        long? linkedElementId2 = null)
    {
        ClashId = string.IsNullOrWhiteSpace(clashId) ? "Clash" : clashId.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? ClashId : name.Trim();
        ElementId1 = elementId1;
        ElementId2 = elementId2;
        X = x;
        Y = y;
        Z = z;
        this.status = status;
        this.comment = comment?.Trim() ?? string.Empty;
        Element1SourceName = element1SourceName?.Trim() ?? string.Empty;
        Element2SourceName = element2SourceName?.Trim() ?? string.Empty;
        LinkedElementId2 = linkedElementId2;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ClashId { get; }

    public string Name { get; }

    public long? ElementId1 { get; }

    public long? ElementId2 { get; }

    public long? LinkedElementId2 { get; }

    public bool IsLinkDriven => LinkedElementId2.HasValue;

    public string Element1SourceName { get; }

    public string Element2SourceName { get; }

    public double? X { get; }

    public double? Y { get; }

    public double? Z { get; }

    public ClashStatus Status
    {
        get => status;
        set
        {
            if (status == value)
            {
                return;
            }

            status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDisplay));
        }
    }

    public string StatusDisplay => ClashStatuses.ToDisplayName(Status);

    public string Comment
    {
        get => comment;
        set
        {
            string normalized = value ?? string.Empty;
            if (comment == normalized)
            {
                return;
            }

            comment = normalized;
            OnPropertyChanged();
        }
    }

    public bool IsElement1Resolved
    {
        get => isElement1Resolved;
        set
        {
            if (isElement1Resolved == value)
            {
                return;
            }

            isElement1Resolved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResolvedDisplay));
        }
    }

    public bool IsElement2Resolved
    {
        get => isElement2Resolved;
        set
        {
            if (isElement2Resolved == value)
            {
                return;
            }

            isElement2Resolved = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ResolvedDisplay));
        }
    }

    public string Element1Name
    {
        get => element1Name;
        set
        {
            element1Name = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string Element2Name
    {
        get => element2Name;
        set
        {
            element2Name = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string Message
    {
        get => message;
        set
        {
            message = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public bool HasPoint => X.HasValue && Y.HasValue && Z.HasValue;

    public string ElementId1Text => ElementId1?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string ElementId2Text => (LinkedElementId2 ?? ElementId2)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string PointText => HasPoint
        ? string.Format(CultureInfo.InvariantCulture, "{0:0.###}; {1:0.###}; {2:0.###}", X, Y, Z)
        : string.Empty;

    public string ResolvedDisplay
    {
        get
        {
            int resolved = (IsElement1Resolved ? 1 : 0) + (IsElement2Resolved ? 1 : 0);
            int total = (ElementId1.HasValue ? 1 : 0) + (ElementId2.HasValue ? 1 : 0);
            return total == 0
                ? "Нет ElementId"
                : $"{resolved}/{total}";
        }
    }

    public bool HasNavigationBounds => navigationBounds is not null;

    public NavigationBounds? Bounds => navigationBounds;

    public void SetNavigationBounds(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        navigationBounds = new NavigationBounds(minX, minY, minZ, maxX, maxY, maxZ);
    }

    public IReadOnlyList<long> GetResolvedElementIds()
    {
        List<long> ids = [];
        if (ElementId1.HasValue && IsElement1Resolved)
        {
            ids.Add(ElementId1.Value);
        }

        if (ElementId2.HasValue && IsElement2Resolved && ElementId2.Value != ElementId1)
        {
            ids.Add(ElementId2.Value);
        }

        return ids;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed record NavigationBounds(
        double MinX,
        double MinY,
        double MinZ,
        double MaxX,
        double MaxY,
        double MaxZ);
}
