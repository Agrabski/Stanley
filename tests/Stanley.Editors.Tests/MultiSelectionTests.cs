using Stanley.Editing;
using Stanley.EditorFramework;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors.Tests;

/// <summary>Shift+click multi-selection: adding to and removing from it, moving, nudging and deleting the whole group, and cleaning up after edits that remove what's selected.</summary>
public class MultiSelectionTests
{
    private static PageDocument CreateSinglePanelDocument(Rect2D? bounds = null)
    {
        var panelId = PanelId.New();
        var panelBounds = bounds ?? new Rect2D(10, 10, 190, 190);
        var panel = new Panel(panelId, PanelShapes.Rectangle(panelBounds), Background: null, CharacterInstances: [], Bubbles: []);
        return new PageDocument([panelId], new Dictionary<PanelId, Panel> { [panelId] = panel });
    }

    private static (EditorHistory History, PageEditorViewModel ViewModel, PanelId PanelId) NewEditor(Rect2D? panelBounds = null)
    {
        var history = new EditorHistory();
        var document = CreateSinglePanelDocument(panelBounds);
        return (history, new PageEditorViewModel(history, new Rect2D(0, 0, 210, 297), document), document.PanelOrder[0]);
    }

    private static Rect2D BubbleBounds(PageEditorViewModel vm, PanelId panelId, int index) =>
        AnchorRing.BoundingBox(vm.Working.Panels[panelId].Bubbles[index].Shape.Anchors);

    private static Rect2D CharacterBounds(PageEditorViewModel vm, PanelId panelId, int index) =>
        vm.CharacterBounds(vm.Working.Panels[panelId].CharacterInstances[index]);

    [Fact]
    public void ToggleSelect_AddsToggledOffAndRemoved()
    {
        var (_, vm, panelId) = NewEditor();
        var a = vm.CreateBubble(panelId, new Point2D(30, 30));
        var b = vm.CreateBubble(panelId, new Point2D(90, 90));
        var c = vm.CreateBubble(panelId, new Point2D(150, 150)); // primary is now c, nothing else selected

        // add: Shift+click on a and then b adds each in turn, the last one becoming primary
        vm.ToggleSelect(panelId, bubbleIndex: a);
        vm.ToggleSelect(panelId, bubbleIndex: b);
        Assert.True(vm.HasMultiSelection);
        Assert.Equal(3, vm.SelectionCount);
        Assert.Equal(b, vm.SelectedBubbleIndex);
        Assert.True(vm.IsPartOfSelection(panelId, bubbleIndex: a));
        Assert.True(vm.IsPartOfSelection(panelId, bubbleIndex: b));
        Assert.True(vm.IsPartOfSelection(panelId, bubbleIndex: c));

        // toggle off the primary (b): the most recently added extra (a) takes over as primary
        vm.ToggleSelect(panelId, bubbleIndex: b);
        Assert.True(vm.HasMultiSelection);
        Assert.Equal(2, vm.SelectionCount);
        Assert.Equal(a, vm.SelectedBubbleIndex);
        Assert.False(vm.IsPartOfSelection(panelId, bubbleIndex: b));

        // remove: Shift+click on a non-primary extra (c) drops just it, leaving the primary as it was
        vm.ToggleSelect(panelId, bubbleIndex: c);
        Assert.False(vm.HasMultiSelection);
        Assert.Equal(1, vm.SelectionCount);
        Assert.Equal(a, vm.SelectedBubbleIndex);
        Assert.False(vm.IsPartOfSelection(panelId, bubbleIndex: c));
    }

    [Fact]
    public void ToggleSelect_CanMixABubbleAndACharacterInOnePanel()
    {
        var (_, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(40, 40));
        var characterIndex = vm.InsertCharacter(CharacterId.New(), panelId); // becomes the sole primary, deselecting the bubble

        Assert.False(vm.HasMultiSelection);

        vm.ToggleSelect(panelId, bubbleIndex: bubbleIndex); // add the bubble back in - the issue's "character and its speech bubble"
        Assert.True(vm.HasMultiSelection);
        Assert.Equal(2, vm.SelectionCount);
        Assert.True(vm.IsPartOfSelection(panelId, bubbleIndex: bubbleIndex));
        Assert.True(vm.IsPartOfSelection(panelId, characterIndex: characterIndex));
    }

    [Fact]
    public void ToggleSelect_InAnotherPanel_StartsAFreshSelectionThere()
    {
        var (_, vm, left) = NewEditor();
        vm.SplitPanel(left, BoundaryOrientation.Vertical, 0.5);
        var leftPanel = vm.Working.PanelOrder[0];
        var rightPanel = vm.Working.PanelOrder[1];

        var rightBubble = vm.CreateBubble(rightPanel, new Point2D(150, 30));
        var leftA = vm.CreateBubble(leftPanel, new Point2D(30, 30));
        var leftB = vm.CreateBubble(leftPanel, new Point2D(60, 60));
        vm.ToggleSelect(leftPanel, bubbleIndex: leftA);
        Assert.True(vm.HasMultiSelection);

        vm.ToggleSelect(rightPanel, bubbleIndex: rightBubble);

        Assert.Equal(rightPanel, vm.SelectedPanelId);
        Assert.False(vm.HasMultiSelection);
        Assert.Equal(1, vm.SelectionCount);
        Assert.Equal(rightBubble, vm.SelectedBubbleIndex);
        Assert.False(vm.IsPartOfSelection(leftPanel, bubbleIndex: leftA));
        Assert.False(vm.IsPartOfSelection(leftPanel, bubbleIndex: leftB));
    }

    [Fact]
    public void Select_APlainClick_CollapsesAMultiSelection()
    {
        var (_, vm, panelId) = NewEditor();
        var a = vm.CreateBubble(panelId, new Point2D(30, 30));
        var b = vm.CreateBubble(panelId, new Point2D(90, 90));
        vm.ToggleSelect(panelId, bubbleIndex: a);
        Assert.True(vm.HasMultiSelection);

        // A plain click (not Shift+click) on one of the selected bubbles - even the primary
        // itself - drops back to selecting just it.
        vm.Select(panelId, a);

        Assert.False(vm.HasMultiSelection);
        Assert.Equal(1, vm.SelectionCount);
        Assert.False(vm.IsPartOfSelection(panelId, bubbleIndex: b));
    }

    [Fact]
    public void UpdateMoveSelection_MovesEveryItemByTheSameDelta_AsOneUndoStep()
    {
        var (history, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(40, 40));
        var characterIndex = vm.InsertCharacter(CharacterId.New(), panelId);
        vm.ToggleSelect(panelId, bubbleIndex: bubbleIndex); // bubble + its speaker, selected together

        var bubbleBefore = BubbleBounds(vm, panelId, bubbleIndex);
        var characterBefore = CharacterBounds(vm, panelId, characterIndex);

        vm.BeginMoveSelection(panelId);
        vm.UpdateMoveSelection(panelId, 5, 7);
        vm.UpdateMoveSelection(panelId, 8, 3);
        vm.EndGesture(commit: true);

        var bubbleAfter = BubbleBounds(vm, panelId, bubbleIndex);
        var characterAfter = CharacterBounds(vm, panelId, characterIndex);
        Assert.Equal(bubbleBefore.Left + 8, bubbleAfter.Left, 6);
        Assert.Equal(bubbleBefore.Top + 3, bubbleAfter.Top, 6);
        Assert.Equal(characterBefore.Left + 8, characterAfter.Left, 6);
        Assert.Equal(characterBefore.Top + 3, characterAfter.Top, 6);

        history.Undo(); // one undo step for the whole group's move
        Assert.Equal(bubbleBefore, BubbleBounds(vm, panelId, bubbleIndex));
        Assert.Equal(characterBefore, CharacterBounds(vm, panelId, characterIndex));
    }

    [Fact]
    public void NudgeSelection_MovesEveryItemByTheSameAmount()
    {
        var (history, vm, panelId) = NewEditor();
        var a = vm.CreateBubble(panelId, new Point2D(40, 40));
        var b = vm.CreateBubble(panelId, new Point2D(120, 120));
        vm.ToggleSelect(panelId, bubbleIndex: a);

        var beforeA = BubbleBounds(vm, panelId, a);
        var beforeB = BubbleBounds(vm, panelId, b);

        vm.NudgeSelection(3, -2);

        Assert.Equal(beforeA.Left + 3, BubbleBounds(vm, panelId, a).Left, 6);
        Assert.Equal(beforeA.Top - 2, BubbleBounds(vm, panelId, a).Top, 6);
        Assert.Equal(beforeB.Left + 3, BubbleBounds(vm, panelId, b).Left, 6);
        Assert.Equal(beforeB.Top - 2, BubbleBounds(vm, panelId, b).Top, 6);

        history.Undo();
        Assert.Equal(beforeA, BubbleBounds(vm, panelId, a));
        Assert.Equal(beforeB, BubbleBounds(vm, panelId, b));
    }

    [Fact]
    public void Nudging_a_group_takes_the_bubbles_tails_along_the_same_as_dragging_it()
    {
        var (_, vm, panelId) = NewEditor();
        var a = vm.CreateBubble(panelId, new Point2D(40, 40));
        vm.CreateBubble(panelId, new Point2D(120, 120)); // selected; Shift+click adds a
        vm.AddBubbleTail(panelId, a, new Point2D(60, 100));
        vm.ToggleSelect(panelId, bubbleIndex: a);
        var tipBefore = vm.Working.Panels[panelId].Bubbles[a].Tails[0].Target;

        vm.NudgeSelection(3, -2);

        var tipAfter = vm.Working.Panels[panelId].Bubbles[a].Tails[0].Target;
        Assert.Equal(tipBefore.X + 3, tipAfter.X, 6);
        Assert.Equal(tipBefore.Y - 2, tipAfter.Y, 6);
        Assert.Equal(2, vm.SelectionCount);
    }

    [Fact]
    public void DeleteSelection_RemovesEveryItem_KeepingTheRightOneWhenSkippingTheMiddle()
    {
        var (history, vm, panelId) = NewEditor();
        var first = vm.CreateBubble(panelId, new Point2D(30, 30));
        vm.SetBubbleText(panelId, first, "First");
        var second = vm.CreateBubble(panelId, new Point2D(90, 30));
        vm.SetBubbleText(panelId, second, "Second");
        var third = vm.CreateBubble(panelId, new Point2D(150, 30));
        vm.SetBubbleText(panelId, third, "Third");

        // Select the first and the third, skipping the one in between - a test for index order
        // when several items come off the same list.
        vm.ToggleSelect(panelId, bubbleIndex: first);
        Assert.True(vm.HasMultiSelection);

        vm.DeleteSelection();

        var remaining = Assert.Single(vm.Working.Panels[panelId].Bubbles);
        Assert.Equal("Second", remaining.Text);
        Assert.False(vm.HasSelectedBubble);
        Assert.True(vm.HasSelectedPanel);

        history.Undo(); // one undo step brings both deleted bubbles back
        Assert.Equal(3, vm.Working.Panels[panelId].Bubbles.Count);
    }

    [Fact]
    public void DeleteSelection_RemovesBubblesCharactersAndElementsTogether()
    {
        var (_, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(30, 30));
        var characterIndex = vm.InsertCharacter(CharacterId.New(), panelId);
        var textIndex = vm.InsertText(TextStylePreset.Caption);
        Assert.True(textIndex >= 0);

        // InsertText left just the text selected; Shift+click the bubble and the character back in too.
        vm.ToggleSelect(panelId, bubbleIndex: bubbleIndex);
        vm.ToggleSelect(panelId, characterIndex: characterIndex);
        Assert.Equal(3, vm.SelectionCount);

        vm.DeleteSelection();

        var panel = vm.Working.Panels[panelId];
        Assert.Empty(panel.Bubbles);
        Assert.Empty(panel.CharacterInstances);
        Assert.Empty(panel.Elements);
    }

    [Fact]
    public void OnUndo_StaleExtraIsDroppedButAValidPrimaryStays()
    {
        var (history, vm, panelId) = NewEditor();
        var a = vm.CreateBubble(panelId, new Point2D(30, 30)); // primary = a
        vm.CreateBubble(panelId, new Point2D(90, 90)); // primary = b, index 1

        // Select b (the primary already) then add a, so a ends up primary and b an extra.
        vm.ToggleSelect(panelId, bubbleIndex: a);
        Assert.Equal(2, vm.SelectionCount);
        Assert.Equal(a, vm.SelectedBubbleIndex);

        // Undoing b's creation removes bubble index 1 - the extra - out from under the selection.
        history.Undo();

        Assert.Single(vm.Working.Panels[panelId].Bubbles);
        Assert.False(vm.HasMultiSelection);
        Assert.Equal(1, vm.SelectionCount);
        Assert.Equal(a, vm.SelectedBubbleIndex); // the still-valid primary survives
        Assert.False(vm.IsPartOfSelection(panelId, bubbleIndex: 1));
    }

    [Fact]
    public void BeginDuplicateSelection_AltDragCopiesEveryItemInTheGroup()
    {
        var (_, vm, panelId) = NewEditor();
        var bubbleIndex = vm.CreateBubble(panelId, new Point2D(40, 40));
        var characterIndex = vm.InsertCharacter(CharacterId.New(), panelId);
        vm.ToggleSelect(panelId, bubbleIndex: bubbleIndex);

        var bubbleBefore = BubbleBounds(vm, panelId, bubbleIndex);
        var characterBefore = CharacterBounds(vm, panelId, characterIndex);

        Assert.True(vm.BeginDuplicateSelection(panelId));
        vm.UpdateMoveSelection(panelId, 10, 10);
        vm.EndGesture(commit: true);

        var panel = vm.Working.Panels[panelId];
        Assert.Equal(2, panel.Bubbles.Count);
        Assert.Equal(2, panel.CharacterInstances.Count);
        Assert.Equal(2, vm.SelectionCount); // the two copies, selected as the new group

        // The originals are untouched; only the copies (now selected) moved.
        Assert.Equal(bubbleBefore, BubbleBounds(vm, panelId, 0));
        Assert.Equal(characterBefore, CharacterBounds(vm, panelId, 0));
        Assert.Equal(bubbleBefore.Left + 10, BubbleBounds(vm, panelId, 1).Left, 6);
        Assert.Equal(characterBefore.Left + 10, CharacterBounds(vm, panelId, 1).Left, 6);
    }
}
