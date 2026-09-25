using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

public sealed partial class PageEditorViewModel
{
    private SpeedLinesStyle _newSpeedLinesStyle = SpeedLinesEditing.DefaultStyle;

    private void InitializeSpeedLinesCommands()
    {
        InsertSpeedLinesCommand = new RelayCommand(() => InsertSpeedLines(), () => Working.PanelOrder.Count > 0);
        SetSpeedLinesColorCommand = new RelayCommand<PaletteColor>(c => { if (c?.Color is { } color) SetCurrentSpeedLinesStyle(CurrentSpeedLinesStyle with { Color = color }); });
        ShuffleSpeedLinesCommand = new RelayCommand(ShuffleSpeedLines, () => HasSelectedSpeedLines);
    }

    private void NotifySpeedLinesCommands()
    {
        InsertSpeedLinesCommand.NotifyCanExecuteChanged();
        ShuffleSpeedLinesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Insert tab: a burst of speed lines, centred in the selected (or first) panel, selected and ready to drag its focus into place.</summary>
    public IRelayCommand InsertSpeedLinesCommand { get; private set; } = null!;

    /// <summary>Speed Lines tab: the lines' colour.</summary>
    public IRelayCommand<PaletteColor> SetSpeedLinesColorCommand { get; private set; } = null!;

    /// <summary>Speed Lines tab › Shuffle: a fresh, differently-jittered burst with the same colour, count and thickness otherwise.</summary>
    public IRelayCommand ShuffleSpeedLinesCommand { get; private set; } = null!;

    /// <summary>A burst of speed lines is selected: the ribbon shows its "Speed Lines" contextual tab.</summary>
    public SpeedLinesElement? SelectedSpeedLines => SelectedElement as SpeedLinesElement;

    public bool HasSelectedSpeedLines => SelectedSpeedLines is not null;

    public bool IsSpeedLinesContext => HasSelectedSpeedLines;

    /// <summary>Like the shape pen: shows the selected speed lines' style, and changing it restyles the selection and becomes the style the next ones inserted start with.</summary>
    public SpeedLinesStyle CurrentSpeedLinesStyle => SelectedSpeedLines?.Style ?? _newSpeedLinesStyle;

    public IBrush SpeedLinesColorBrush => DrawingPalette.BrushOf(CurrentSpeedLinesStyle.Color);

    /// <summary>Speed Lines tab › Lines: how many radiate out - a drag gesture, so dragging the slider is one undo step.</summary>
    public double SpeedLinesCount
    {
        get => CurrentSpeedLinesStyle.Count;
        set => SetCurrentSpeedLinesStyle(CurrentSpeedLinesStyle with { Count = (int)Math.Round(Math.Clamp(value, SpeedLinesEditing.MinCount, SpeedLinesEditing.MaxCount)) });
    }

    /// <summary>Speed Lines tab › Thickness: how wide each line is at its outer end, in page millimetres - a drag gesture, so dragging the slider is one undo step.</summary>
    public double SpeedLinesThickness
    {
        get => CurrentSpeedLinesStyle.WidthMm;
        set => SetCurrentSpeedLinesStyle(CurrentSpeedLinesStyle with { WidthMm = Math.Clamp(value, 0, SpeedLinesEditing.MaxWidthMm) });
    }

    /// <summary>
    /// Insert tab: a burst of speed lines centred in the selected (or first) panel - roughly
    /// a third of it, in the current style, behind the characters - selected, in one undo
    /// step. Returns its index in the panel, or -1 if there's no panel to put it in.
    /// </summary>
    public int InsertSpeedLines()
    {
        if (_selectedPanelId is not { } panelId || !Working.Panels.TryGetValue(panelId, out var panel))
        {
            if (Working.PanelOrder.Count == 0)
                return -1;
            panelId = Working.PanelOrder[0];
            panel = Working.Panels[panelId];
        }

        var speedLines = SpeedLinesEditing.Place(Bounds(panel)) with { Style = _newSpeedLinesStyle };
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, speedLines] })));
        var index = IndexOfElement(panelId, speedLines.Id);
        if (index >= 0)
            SelectElement(panelId, index);
        return index;
    }

    private void ShuffleSpeedLines()
    {
        if (SelectedSpeedLines is { } speedLines)
            SetCurrentSpeedLinesStyle(SpeedLinesEditing.Shuffle(speedLines).Style);
    }

    /// <summary>Restyles the selected speed lines (or sets what new ones will look like): a live preview while a slider drag is active, one undo step either way.</summary>
    private void SetCurrentSpeedLinesStyle(SpeedLinesStyle style)
    {
        _newSpeedLinesStyle = style;
        if (SelectedSpeedLines is { } speedLines && speedLines.Style != style)
        {
            var result = EditElementInPanel(Working, _selectedPanelId!.Value, _selectedElementIndex, e => e is SpeedLinesElement current
                ? SpeedLinesEditing.SetStyle(current, style) is var styled && styled.IsValid
                    ? EditResult<PanelElement>.Success(styled.Value)
                    : EditResult<PanelElement>.Failure(styled.Error!)
                : EditResult<PanelElement>.Failure("That isn't speed lines."));
            if (IsGestureActive)
                UpdateGesture(result);
            else
                Apply(result);
        }
        RaiseElementDerivedChanged();
    }
}
