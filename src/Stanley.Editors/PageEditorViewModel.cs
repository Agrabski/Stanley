using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

public sealed class PageEditorViewModel : EditorViewModel<PageDocument>
{
    public Rect2D PageBounds { get; }

    public PageEditorViewModel(EditorHistory history, Rect2D pageBounds, PageDocument initial)
        : base(history, "Page", initial)
    {
        PageBounds = pageBounds;
    }

    // Panel layout operations
    public void BeginResizePanel(PanelId id) => BeginGesture();

    public void UpdateResizePanel(PanelId id, Rect2D newBounds)
    {
        var result = ResizeOnePanel(id, newBounds);
        UpdateGesture(result);
    }

    public void BeginDragBoundary(PanelBoundaryDrag boundary) => BeginGesture();

    public void UpdateDragBoundary(PanelBoundaryDrag boundary, double newPosition)
    {
        var result = DragOneBoundary(boundary, newPosition);
        UpdateGesture(result);
    }

    public void SplitPanel(PanelId id, BoundaryOrientation orientation, double fraction)
    {
        if (!Working.Panels.TryGetValue(id, out var panel))
        {
            LastError = $"Unknown panel '{id}'.";
            return;
        }

        var splitResult = PanelLayoutEditing.Split(panel, orientation, fraction);
        if (!splitResult.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(splitResult.Error!));
            return;
        }

        var (first, second) = splitResult.Value;
        var newOrder = Working.PanelOrder.ToList();
        var panelIndex = newOrder.IndexOf(id);
        newOrder[panelIndex] = first.Id;
        newOrder.Insert(panelIndex + 1, second.Id);

        var newPanels = new Dictionary<PanelId, Panel>(Working.Panels)
        {
            [first.Id] = first,
            [second.Id] = second
        };
        newPanels.Remove(id);

        var newDocument = new PageDocument(newOrder, newPanels);
        Apply(EditResult<PageDocument>.Success(newDocument));
    }

    // Bubble operations
    public void InsertBubble(PanelId panelId, Rect2D bounds, BubbleStylePreset style)
    {
        var bubbleResult = BubbleEditing.Create(bounds, style);
        if (!bubbleResult.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(bubbleResult.Error!));
            return;
        }

        var bubble = bubbleResult.Value;
        var panelResult = EditPanel(panelId, p =>
            EditResult<Panel>.Success(p with { Bubbles = [..p.Bubbles, bubble] }));
        Apply(panelResult);
    }

    public void BeginResizeBubble(PanelId panelId, int bubbleIndex) => BeginGesture();

    public void UpdateResizeBubble(PanelId panelId, int bubbleIndex, Rect2D newBounds)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.Resize(b, newBounds));
        UpdateGesture(result);
    }

    public void SetBubbleStyle(PanelId panelId, int bubbleIndex, BubbleStylePreset style)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.SetStyle(b, style));
        Apply(result);
    }

    public void AddBubbleTail(PanelId panelId, int bubbleIndex, Point2D target)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.AddTail(b, target));
        Apply(result);
    }

    public void RemoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.RemoveTail(b, tailIndex));
        Apply(result);
    }

    public void BeginMoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex) => BeginGesture();

    public void UpdateMoveBubbleTail(PanelId panelId, int bubbleIndex, int tailIndex, Point2D newTarget)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.MoveTailTarget(b, tailIndex, newTarget));
        UpdateGesture(result);
    }

    public void BeginSlideBubbleTailAttachment(PanelId panelId, int bubbleIndex, int tailIndex) => BeginGesture();

    public void UpdateSlideBubbleTailAttachment(PanelId panelId, int bubbleIndex, int tailIndex, Point2D pointer)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.SlideTailAttachment(b, tailIndex, pointer));
        UpdateGesture(result);
    }

    public void SetBubbleText(PanelId panelId, int bubbleIndex, string text)
    {
        var result = EditBubbleInPanel(panelId, bubbleIndex, b =>
            BubbleEditing.SetText(b, text));
        Apply(result);
    }

    // Private helpers
    private EditResult<PageDocument> ResizeOnePanel(PanelId id, Rect2D newBounds)
    {
        return EditPanel(id, p => PanelLayoutEditing.Resize(p, newBounds, PageBounds));
    }

    private EditResult<PageDocument> DragOneBoundary(PanelBoundaryDrag boundary, double newPosition)
    {
        var result = PanelLayoutEditing.DragBoundary(Working.Panels, boundary, newPosition, PageBounds);
        if (!result.IsValid)
            return EditResult<PageDocument>.Failure(result.Error!);

        return EditResult<PageDocument>.Success(new PageDocument(Working.PanelOrder, result.Value));
    }

    private EditResult<PageDocument> EditPanel(PanelId id, Func<Panel, EditResult<Panel>> edit)
    {
        if (!Working.Panels.TryGetValue(id, out var panel))
            return EditResult<PageDocument>.Failure($"Unknown panel '{id}'.");

        var result = edit(panel);
        if (!result.IsValid)
            return EditResult<PageDocument>.Failure(result.Error!);

        var newPanels = new Dictionary<PanelId, Panel>(Working.Panels)
        {
            [id] = result.Value
        };

        return EditResult<PageDocument>.Success(new PageDocument(Working.PanelOrder, newPanels));
    }

    private EditResult<PageDocument> EditBubbleInPanel(PanelId panelId, int bubbleIndex, Func<Bubble, EditResult<Bubble>> edit)
    {
        if (!Working.Panels.TryGetValue(panelId, out var panel))
            return EditResult<PageDocument>.Failure($"Unknown panel '{panelId}'.");

        if (bubbleIndex < 0 || bubbleIndex >= panel.Bubbles.Count)
            return EditResult<PageDocument>.Failure("No such bubble.");

        var bubble = panel.Bubbles[bubbleIndex];
        var result = edit(bubble);
        if (!result.IsValid)
            return EditResult<PageDocument>.Failure(result.Error!);

        var newBubbles = panel.Bubbles.ToList();
        newBubbles[bubbleIndex] = result.Value;
        var updatedPanel = panel with { Bubbles = newBubbles };

        return EditPanel(panelId, _ => EditResult<Panel>.Success(updatedPanel));
    }
}
