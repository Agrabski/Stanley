using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stanley.ProjectModel.Ids;

namespace Stanley.Editors;

/// <summary>What a row of the Layers pane stands for.</summary>
public enum LayerRowKind
{
    /// <summary>A panel - or a thought cloud - of the page: the header its layers sit under.</summary>
    Panel,
    Bubble,
    Character,
    /// <summary>A drawn shape, text, picture, speed lines or group.</summary>
    Element,
    /// <summary>What fills the panel behind everything: always the last row of its panel, never movable.</summary>
    Background
}

/// <summary>
/// One row of the Layers pane. Rows keep where they point as a panel plus an index into one of
/// the panel's lists - the same addressing the editor's selection uses - and are rebuilt
/// whenever the page changes, so an index is never stale by the time it's clicked.
/// </summary>
public sealed class LayerRow : ObservableObject
{
    private bool _isSelected;

    public LayerRow(LayerRowKind kind, PanelId panelId, int index, string label, string detail, string iconKey)
    {
        Kind = kind;
        PanelId = panelId;
        Index = index;
        Label = label;
        Detail = detail;
        IconKey = iconKey;
    }

    public LayerRowKind Kind { get; }

    public PanelId PanelId { get; }

    /// <summary>Which bubble, character or element of the panel this is; -1 for a panel or its background.</summary>
    public int Index { get; }

    public string Label { get; }

    /// <summary>The quieter second line: what a bubble says, what a group holds. Empty for none.</summary>
    public string Detail { get; }

    /// <summary>The name of an icon geometry in the app's resources (<see cref="IconKeyConverter"/>).</summary>
    public string IconKey { get; }

    public bool IsPanel => Kind == LayerRowKind.Panel;

    public bool HasDetail => Detail.Length > 0;

    /// <summary>A panel's header stands out from the layers under it.</summary>
    public FontWeight LabelWeight => IsPanel ? FontWeight.SemiBold : FontWeight.Normal;

    /// <summary>Indents what sits under a panel's header.</summary>
    public Thickness Indent => IsPanel ? default : new Thickness(22, 0, 0, 0);

    /// <summary>Whether a panel row is open, showing its layers.</summary>
    public bool IsExpanded { get; init; }

    /// <summary>Opens or closes a panel row; null for the rows that don't open.</summary>
    public IRelayCommand? ToggleCommand { get; init; }

    /// <summary>Whether this is part of what's selected on the page.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Tip => HasDetail ? $"{Label} - {Detail}" : Label;
}
