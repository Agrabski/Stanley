using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// The Layers side pane (a dock <see cref="Tool"/> on the right of the editors): what the
/// current page's panels hold, front to back as it's drawn (<see cref="PanelStack.Order"/>),
/// so overlapping things can be picked from a list. Like the other side panes it isn't an
/// editor - the ribbon keeps following the page being worked in; the pane follows the page
/// editor it's pointed at (<see cref="Editor"/>), and its highlight follows what's selected
/// there, both ways.
/// </summary>
public sealed class LayersViewModel : Tool
{
    // The panels whose layers are showing. Whatever gets selected opens its panel, so the
    // list grows to where you've been working; the arrow on a panel's row closes it again.
    private readonly HashSet<PanelId> _expanded = [];
    private PageEditorViewModel? _editor;

    public LayersViewModel()
    {
        Id = "Layers";
        Title = "Layers";
        CanClose = false;
        CanFloat = false;
        // It lives in its own column: no pinning it away, dragging it elsewhere or turning it into a tab among the pages.
        CanPin = false;
        CanDrag = false;
        CanDockAsDocument = false;
    }

    /// <summary>The rows, top to bottom: each panel in reading order, and under an open one its layers from the front to the back, then its background.</summary>
    public ObservableCollection<LayerRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>The page editor whose panels are listed - the current page's; null when there's none.</summary>
    public PageEditorViewModel? Editor
    {
        get => _editor;
        set
        {
            if (ReferenceEquals(value, _editor))
                return;
            if (_editor != null)
                _editor.PropertyChanged -= OnEditorPropertyChanged;
            _editor = value;
            if (_editor != null)
            {
                _editor.PropertyChanged += OnEditorPropertyChanged;
                if (_editor.SelectedPanelId is { } selected)
                    _expanded.Add(selected);
            }
            Rebuild();
        }
    }

    /// <summary>Clicking a row selects what it stands for on the page; Shift+click adds a layer to the selection (or drops it), as on the page itself.</summary>
    public void Pick(LayerRow row, bool additive)
    {
        if (_editor is not { } editor)
            return;
        switch (row.Kind)
        {
            case LayerRowKind.Bubble when additive:
                editor.ToggleSelect(row.PanelId, bubbleIndex: row.Index);
                break;
            case LayerRowKind.Bubble:
                editor.Select(row.PanelId, bubbleIndex: row.Index);
                break;
            case LayerRowKind.Character when additive:
                editor.ToggleSelect(row.PanelId, characterIndex: row.Index);
                break;
            case LayerRowKind.Character:
                editor.SelectCharacter(row.PanelId, row.Index);
                break;
            case LayerRowKind.Element when additive:
                editor.ToggleSelect(row.PanelId, elementIndex: row.Index);
                break;
            case LayerRowKind.Element:
                editor.SelectElement(row.PanelId, row.Index);
                break;
            default: // a panel, or its background: the panel itself
                editor.Select(row.PanelId);
                break;
        }
        // The keyboard goes to the page, so Delete and the arrow keys act on what was just picked.
        editor.FocusPage();
    }

    private void ToggleExpanded(PanelId panel)
    {
        if (!_expanded.Remove(panel))
            _expanded.Add(panel);
        Rebuild();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // Structure: only when an edit is committed, not on every frame of a drag.
            case nameof(PageEditorViewModel.Committed):
            case nameof(PageEditorViewModel.CharacterSnapshot):
                Rebuild();
                break;
            case nameof(PageEditorViewModel.SelectedPanelId):
                if (_editor?.SelectedPanelId is { } panel && _expanded.Add(panel))
                    Rebuild();
                else
                    RefreshSelection();
                break;
            case nameof(PageEditorViewModel.SelectedBubbleIndex):
            case nameof(PageEditorViewModel.SelectedCharacterIndex):
            case nameof(PageEditorViewModel.SelectedElementIndex):
            case nameof(PageEditorViewModel.SelectionCount):
                RefreshSelection();
                break;
        }
    }

    private void Rebuild()
    {
        Rows.Clear();
        if (_editor is { } editor)
        {
            var document = editor.Committed;
            var clouds = document.PanelOrder.Count(id => document.Panels.TryGetValue(id, out var p) && p.Kind == PanelKind.Cloud);
            int panelNumber = 0, cloudNumber = 0;
            foreach (var id in document.PanelOrder)
            {
                if (!document.Panels.TryGetValue(id, out var panel))
                    continue;
                var isCloud = panel.Kind == PanelKind.Cloud;
                var order = PanelStack.Order(panel);
                var expanded = _expanded.Contains(id);
                var panelId = id;
                Rows.Add(new LayerRow(LayerRowKind.Panel, id, -1,
                    isCloud ? LayerLabels.Cloud(++cloudNumber, clouds) : LayerLabels.Panel(++panelNumber),
                    LayerLabels.PanelDetail(order.Count), isCloud ? "ThoughtCloudIcon" : "PanelBorderIcon")
                {
                    IsExpanded = expanded,
                    ToggleCommand = new RelayCommand(() => ToggleExpanded(panelId))
                });
                if (!expanded)
                    continue;
                for (var i = order.Count - 1; i >= 0; i--) // the front first, as lists of layers read
                    Rows.Add(RowFor(editor, id, panel, order[i]));
                Rows.Add(new LayerRow(LayerRowKind.Background, id, -1, "Background", LayerLabels.BackgroundDetail(panel.Background), "BackgroundIcon"));
            }
        }
        OnPropertyChanged(nameof(HasRows));
        RefreshSelection();
    }

    private static LayerRow RowFor(PageEditorViewModel editor, PanelId panelId, Panel panel, StackItem item)
    {
        switch (item.Kind)
        {
            case StackKind.Bubble:
                var bubble = panel.Bubbles[item.Index];
                return new LayerRow(LayerRowKind.Bubble, panelId, item.Index, LayerLabels.BubbleName(bubble.Style), LayerLabels.Preview(bubble.Text),
                    LayerLabels.BubbleIcon(bubble.Style));
            case StackKind.Character:
                var instance = panel.CharacterInstances[item.Index];
                var name = editor.CharacterSnapshot.TryGetValue(instance.CharacterId, out var character) ? character.Name : "Missing character";
                return new LayerRow(LayerRowKind.Character, panelId, item.Index, name, "", "CharacterIcon");
            default:
                var element = panel.Elements[item.Index];
                return new LayerRow(LayerRowKind.Element, panelId, item.Index, LayerLabels.ElementName(element), LayerLabels.ElementDetail(element),
                    LayerLabels.ElementIcon(element));
        }
    }

    private void RefreshSelection()
    {
        if (_editor is not { } editor)
            return;
        var itemSelected = editor.HasSelectedBubble || editor.HasSelectedCharacter || editor.HasSelectedElement;
        foreach (var row in Rows)
        {
            row.IsSelected = row.Kind switch
            {
                LayerRowKind.Panel => !itemSelected && Equals(editor.SelectedPanelId, row.PanelId),
                LayerRowKind.Bubble => editor.IsPartOfSelection(row.PanelId, bubbleIndex: row.Index),
                LayerRowKind.Character => editor.IsPartOfSelection(row.PanelId, characterIndex: row.Index),
                LayerRowKind.Element => editor.IsPartOfSelection(row.PanelId, elementIndex: row.Index),
                _ => false
            };
        }
    }
}
