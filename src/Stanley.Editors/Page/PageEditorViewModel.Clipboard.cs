using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Characters;
using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// What Copy (Ctrl+C) put away for Paste (Ctrl+V): one thing off a page (<see cref="Clipping"/>)
/// and the pictures it shows, so it pastes into another page, issue or comic too. Stanley's own
/// rather than the system clipboard: what's on it is a piece of a comic, not text or an image.
/// </summary>
public sealed class PageClipboard
{
    private static readonly IReadOnlyDictionary<string, ArtFile> NoPictures = new Dictionary<string, ArtFile>();

    /// <summary>The one every page editor uses unless it's given another: copy on one page, paste on any other - in any comic opened since.</summary>
    public static PageClipboard Shared { get; } = new();

    public Clipping? Content { get; private set; }

    /// <summary>The pictures <see cref="Content"/> shows, by art file name.</summary>
    public IReadOnlyDictionary<string, ArtFile> Pictures { get; private set; } = NoPictures;

    public void Put(Clipping content, IReadOnlyDictionary<string, ArtFile> pictures)
    {
        Content = content;
        Pictures = pictures;
    }
}

/// <summary>
/// Copy, Cut, Paste and Duplicate for whatever is selected - a bubble, a character, a shape,
/// text, a picture, speed lines or a whole panel - with everything about it: its style, its
/// words, its pose. And Alt+drag, which drags a copy away and leaves the original where it was.
/// </summary>
public sealed partial class PageEditorViewModel
{
    /// <summary>What a move drag works from: <see cref="EditorFramework.EditorViewModel{T}.Committed"/>, plus the copy an Alt+drag is moving.</summary>
    private PageDocument? _moveBase;

    /// <summary>Puts the selection back on the original when an Alt+drag is cancelled (Esc) and its copy goes away.</summary>
    private Action? _duplicateCancelled;

    public IRelayCommand CutCommand { get; private set; } = null!;
    public IRelayCommand CopyCommand { get; private set; } = null!;
    public IRelayCommand PasteCommand { get; private set; } = null!;
    public IRelayCommand DuplicateCommand { get; private set; } = null!;

    /// <summary>Where Copy puts things and Paste takes them from; <see cref="PageClipboard.Shared"/> unless set.</summary>
    public PageClipboard Clipboard
    {
        get;
        set
        {
            field = value;
            PasteCommand?.NotifyCanExecuteChanged();
        }
    } = PageClipboard.Shared;

    private void InitializeClipboardCommands()
    {
        CutCommand = new RelayCommand(() => Cut(), () => CanCopy);
        CopyCommand = new RelayCommand(() => Copy(), () => CanCopy);
        PasteCommand = new RelayCommand(() => Paste(), () => CanPaste);
        DuplicateCommand = new RelayCommand(() => Duplicate(), () => CanCopy);
    }

    private void NotifyClipboardCommands()
    {
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Something that can be copied is selected: a bubble, a character, an element, or a panel.</summary>
    public bool CanCopy => SelectedClipping() is not null;

    /// <summary>The clipboard holds something that can go on this page: a panel needs the layout unlocked, anything else a panel to go in.</summary>
    public bool CanPaste => Clipboard.Content switch
    {
        null => false,
        PanelClipping => !Working.LayoutLocked,
        _ => Working.PanelOrder.Count > 0
    };

    /// <summary>What's selected, as a clipping - null with nothing selected (or only a panel, on a locked layout).</summary>
    private Clipping? SelectedClipping()
    {
        if (_selectedPanelId is not { } panelId || SelectedPanel is not { } panel)
            return null;
        var bounds = Bounds(panel);
        if (SelectedBubble is { } bubble)
            return new BubbleClipping(panelId, bounds, bubble);
        if (SelectedElement is { } element)
            return new ElementClipping(panelId, bounds, element);
        if (SelectedCharacter is { } character)
            return new CharacterClipping(panelId, bounds, character);
        return Working.LayoutLocked ? null : new PanelClipping(panelId, bounds, panel);
    }

    /// <summary>Ctrl+C: puts a copy of what's selected on the clipboard. False with nothing selected.</summary>
    public bool Copy()
    {
        if (SelectedClipping() is not { } clipping)
            return false;
        Clipboard.Put(clipping, PicturesIn(clipping));
        PasteCommand.NotifyCanExecuteChanged();
        return true;
    }

    /// <summary>Ctrl+X: copies what's selected, then deletes it (one undo step brings it back).</summary>
    public bool Cut()
    {
        if (!Copy())
            return false;
        DeleteSelection();
        return true;
    }

    /// <summary>
    /// Ctrl+V: a copy of what's on the clipboard, selected, in one undo step - a bubble, character
    /// or element in the selected panel (the one whatever is selected is in), else back in the
    /// panel it was copied from, else the first; where it sat in its old panel, stepped aside
    /// from anything already in that spot. A panel goes onto the page. False if nothing was pasted.
    /// </summary>
    public bool Paste() => Clipboard.Content is { } content && PutBack(content, Clipboard.Pictures, PasteTarget(content));

    /// <summary>Paste into <paramref name="panelId"/> - right-click › Paste on a panel of a locked layout, which can't be selected itself. A panel on the clipboard goes onto the page as usual.</summary>
    public bool PasteInto(PanelId panelId) => Clipboard.Content is { } content && PutBack(content, Clipboard.Pictures, panelId);

    /// <summary>Ctrl+D: a copy of what's selected, beside it and selected - one undo step. The clipboard is left as it was.</summary>
    public bool Duplicate() => SelectedClipping() is { } clipping && PutBack(clipping, PictureSnapshot, clipping.FromPanel);

    private PanelId? PasteTarget(Clipping clipping)
    {
        if (_selectedPanelId is { } selected && Working.Panels.ContainsKey(selected))
            return selected;
        if (Working.Panels.ContainsKey(clipping.FromPanel))
            return clipping.FromPanel;
        return Working.PanelOrder.Count > 0 ? Working.PanelOrder[0] : null;
    }

    private bool PutBack(Clipping clipping, IReadOnlyDictionary<string, ArtFile> pictures, PanelId? target)
    {
        if (clipping is PanelClipping panelClipping)
            return PutBackPanel(panelClipping, pictures);
        if (target is not { } panelId || !Working.Panels.TryGetValue(panelId, out var panel))
            return false;

        var bounds = Bounds(panel);
        switch (clipping)
        {
            case BubbleClipping { Bubble: var original }:
                {
                    var bubble = BubbleEditing.Refit(Clippings.Copy(original), clipping.FromPanelBounds, bounds);
                    var box = AnchorRing.BoundingBox(bubble.Shape.Anchors);
                    var spot = Clippings.FreeSpot(box, panel.Bubbles.Select(b => AnchorRing.BoundingBox(b.Shape.Anchors)).ToList(), bounds);
                    bubble = BubbleEditing.KeepInside(BubbleEditing.Move(bubble, spot.X - box.X, spot.Y - box.Y, withTails: true).Value, bounds);
                    Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Bubbles = [.. p.Bubbles, bubble] })));
                    var index = Working.Panels[panelId].Bubbles.ToList().FindIndex(b => b.Id == bubble.Id);
                    if (index < 0)
                        return false;
                    Select(panelId, index);
                    return true;
                }
            case ElementClipping { Element: var original }:
                {
                    var element = WithPictures(ElementEditing.Refit(Clippings.Copy(original), clipping.FromPanelBounds, bounds), TakeInPictures(pictures));
                    var box = PanelElements.Bounds(element);
                    var spot = Clippings.FreeSpot(box, panel.Elements.Select(PanelElements.Bounds).ToList(), bounds);
                    element = ElementEditing.KeepReachable(ElementEditing.Move(element, spot.X - box.X, spot.Y - box.Y), bounds);
                    Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { Elements = [.. p.Elements, element] })));
                    var index = IndexOfElement(panelId, element.Id);
                    if (index < 0)
                        return false;
                    SelectElement(panelId, index);
                    return true;
                }
            case CharacterClipping { Character: var original }:
                {
                    if (!IsInThisComic(original))
                    {
                        Apply(EditResult<PageDocument>.Failure("That character isn't in this comic - bring them in from the Characters pane first."));
                        return false;
                    }
                    var instance = CharacterPlacementEditing.Refit(Clippings.Copy(original), clipping.FromPanelBounds, bounds);
                    var box = CharacterBounds(instance);
                    // Sideways only: a copy steps along the same floor rather than down into it.
                    var spot = Clippings.FreeSpot(box, panel.CharacterInstances.Select(CharacterBounds).ToList(), bounds, stepY: 0);
                    instance = CharacterPlacementEditing.Move(instance, spot.X - box.X, 0);
                    instance = CharacterPlacementEditing.KeepReachable(instance, InstanceExtent(instance), bounds);
                    var index = panel.CharacterInstances.Count;
                    Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p with { CharacterInstances = [.. p.CharacterInstances, instance] })));
                    if (Working.Panels[panelId].CharacterInstances.Count <= index)
                        return false;
                    SelectCharacter(panelId, index);
                    return true;
                }
            default:
                return false;
        }
    }

    /// <summary>A panel, with all that's in it, onto the page: where it was, stepped aside from any panel in that spot (it overlaps its neighbours until moved or resized).</summary>
    private bool PutBackPanel(PanelClipping clipping, IReadOnlyDictionary<string, ArtFile> pictures)
    {
        if (Working.LayoutLocked)
        {
            Apply(EditResult<PageDocument>.Failure("Layout is locked - unlock it on the Layout tab to paste a panel."));
            return false;
        }

        var renames = TakeInPictures(pictures);
        var panel = Clippings.Copy(clipping.Panel);
        panel = panel with
        {
            Background = panel.Background is InlineBackground inline && renames.TryGetValue(inline.ArtFileName, out var name) ? new InlineBackground(name) : panel.Background,
            Elements = panel.Elements.Select(e => WithPictures(e, renames)).ToList(),
            // Characters from another comic stay behind: this one has nothing to draw them with.
            CharacterInstances = panel.CharacterInstances.Where(IsInThisComic).ToList()
        };
        var box = Bounds(panel);
        var spot = Clippings.FreeSpot(box, Working.Panels.Values.Select(Bounds).ToList(), PageBounds);
        var moved = PanelLayoutEditing.Move(panel, spot.X - box.X, spot.Y - box.Y, PageBounds);
        if (!moved.IsValid)
        {
            Apply(EditResult<PageDocument>.Failure(moved.Error!));
            return false;
        }

        var panels = new Dictionary<PanelId, Panel>(Working.Panels) { [moved.Value.Id] = moved.Value };
        Apply(EditResult<PageDocument>.Success(Working with { Panels = panels, PanelOrder = [.. Working.PanelOrder, moved.Value.Id] }));
        if (!Working.Panels.ContainsKey(moved.Value.Id))
            return false;
        Select(moved.Value.Id);
        return true;
    }

    /// <summary>Whether this comic has <paramref name="instance"/>'s character - always, for a page edited without a Characters pane.</summary>
    private bool IsInThisComic(CharacterInstance instance) => _catalog is null || CharacterSnapshot.ContainsKey(instance.CharacterId);

    /// <summary>The pictures a clipping shows, from this comic's, so they go wherever it's pasted.</summary>
    private IReadOnlyDictionary<string, ArtFile> PicturesIn(Clipping clipping)
    {
        IEnumerable<string> names = clipping switch
        {
            PanelClipping p => PanelElements.ArtFileNames(p.Panel),
            ElementClipping { Element: var element } => PanelElements.ArtFileNames(element),
            _ => []
        };
        var files = PictureSnapshot;
        return names.Distinct().Where(files.ContainsKey).ToDictionary(n => n, n => files[n], StringComparer.Ordinal);
    }

    /// <summary>Takes pictures copied from another comic into this one's; returns the names any came in under that differ from their old ones.</summary>
    private Dictionary<string, string> TakeInPictures(IReadOnlyDictionary<string, ArtFile> pictures)
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_pictures is null)
            return renames;
        foreach (var (name, file) in pictures)
        {
            if (_pictures.Files.ContainsKey(name))
                continue;
            var added = _pictures.Add(file, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
            if (added != name)
                renames[name] = added;
        }
        return renames;
    }

    private static PanelElement WithPictures(PanelElement element, IReadOnlyDictionary<string, string> renames) => element switch
    {
        PictureElement picture when renames.TryGetValue(picture.ArtFileName, out var name) => picture with { ArtFileName = name },
        GroupElement group when renames.Count > 0 => group with { Children = group.Children.Select(c => WithPictures(c, renames)).ToList() },
        _ => element
    };

    // ---------------------------------------------------------------- Alt+drag

    /// <summary>
    /// Alt+drag on a bubble: starts a move gesture on a copy of it, added in front of the rest
    /// and selected, leaving the original where it is. Move the copy with
    /// <see cref="UpdateMoveBubble"/>; the copy and the move are one undo step, and cancelling
    /// takes the copy away again. Returns the copy's index, or -1.
    /// </summary>
    public int BeginDuplicateBubble(PanelId panelId, int index) =>
        BeginDuplicate(panelId, index, p => index < p.Bubbles.Count ? p with { Bubbles = [.. p.Bubbles, Clippings.Copy(p.Bubbles[index])] } : null,
            p => p.Bubbles.Count - 1, i => Select(panelId, i), () => Select(panelId, index));

    /// <summary>Alt+drag on an element: <see cref="BeginDuplicateBubble"/> for a shape, text, picture or speed lines; move it with <see cref="UpdateMoveElement"/>.</summary>
    public int BeginDuplicateElement(PanelId panelId, int index) =>
        BeginDuplicate(panelId, index, p => index < p.Elements.Count ? p with { Elements = [.. p.Elements, Clippings.Copy(p.Elements[index])] } : null,
            p => p.Elements.Count - 1, i => SelectElement(panelId, i), () => SelectElement(panelId, index));

    /// <summary>Alt+drag on a character: <see cref="BeginDuplicateBubble"/> for a placed character; move it with <see cref="UpdateMoveCharacter"/> (it snaps onto the original's floor).</summary>
    public int BeginDuplicateCharacter(PanelId panelId, int index) =>
        BeginDuplicate(panelId, index, p => index < p.CharacterInstances.Count ? p with { CharacterInstances = [.. p.CharacterInstances, Clippings.Copy(p.CharacterInstances[index])] } : null,
            p => p.CharacterInstances.Count - 1, i => SelectCharacter(panelId, i), () => SelectCharacter(panelId, index));

    /// <summary>Alt+drag on a panel: a copy of it and all it holds, on top of the other panels; move it with <see cref="UpdateMovePanel"/>. Returns the copy's id, or null (a locked layout).</summary>
    public PanelId? BeginDuplicatePanel(PanelId panelId)
    {
        if (Committed.LayoutLocked || !Committed.Panels.TryGetValue(panelId, out var panel))
            return null;
        var copy = Clippings.Copy(panel);
        StartDuplicate(Committed with
        {
            Panels = new Dictionary<PanelId, Panel>(Committed.Panels) { [copy.Id] = copy },
            PanelOrder = [.. Committed.PanelOrder, copy.Id]
        }, () => Select(panelId));
        Select(copy.Id);
        return copy.Id;
    }

    /// <summary>
    /// Alt+drag on a multi-selection: a copy of every selected bubble, character and element,
    /// added in front of the rest and selected as the new group, leaving the originals where
    /// they were. Move the copies with <see cref="UpdateMoveSelection"/>; the copies and the
    /// move are one undo step, and cancelling (Esc) puts the original selection back. False if
    /// nothing in the selection still exists to copy.
    /// </summary>
    public bool BeginDuplicateSelection(PanelId panelId)
    {
        if (!Committed.Panels.TryGetValue(panelId, out var panel))
            return false;

        var bubbles = panel.Bubbles.ToList();
        var characters = panel.CharacterInstances.ToList();
        var elements = panel.Elements.ToList();
        var copies = new List<SelectedItem>();
        // What was grouped together is copied as a group of its own, not into the original's.
        var relinker = new Clippings.Relinker();
        foreach (var item in AllSelected())
        {
            switch (item.Kind)
            {
                case SelectionKind.Bubble when item.Index >= 0 && item.Index < bubbles.Count:
                    bubbles.Add(Clippings.Copy(bubbles[item.Index]) with { Link = relinker.For(bubbles[item.Index].Link) });
                    copies.Add(new SelectedItem(SelectionKind.Bubble, bubbles.Count - 1));
                    break;
                case SelectionKind.Character when item.Index >= 0 && item.Index < characters.Count:
                    characters.Add(Clippings.Copy(characters[item.Index]) with { Link = relinker.For(characters[item.Index].Link) });
                    copies.Add(new SelectedItem(SelectionKind.Character, characters.Count - 1));
                    break;
                case SelectionKind.Element when item.Index >= 0 && item.Index < elements.Count:
                    elements.Add(Clippings.Copy(elements[item.Index]) with { Link = relinker.For(elements[item.Index].Link) });
                    copies.Add(new SelectedItem(SelectionKind.Element, elements.Count - 1));
                    break;
            }
        }
        if (copies.Count == 0)
            return false;

        var originalPrimary = PrimaryItem;
        var originalExtras = _extraSelection.ToList();
        var withCopies = panel with { Bubbles = bubbles, CharacterInstances = characters, Elements = elements };
        StartDuplicate(Committed with { Panels = new Dictionary<PanelId, Panel>(Committed.Panels) { [panelId] = withCopies } },
            () => RestoreSelection(originalPrimary, originalExtras));

        _extraSelection.Clear();
        _extraSelection.AddRange(copies.Skip(1));
        SetPrimaryIndex(copies[0]);
        return true;
    }

    /// <summary>Puts the pre-duplicate selection back - what an Alt+drag's cancel restores.</summary>
    private void RestoreSelection(SelectedItem? primary, List<SelectedItem> extras)
    {
        _extraSelection.Clear();
        _extraSelection.AddRange(extras);
        if (primary is { } item)
            SetPrimaryIndex(item);
    }

    private int BeginDuplicate(PanelId panelId, int index, Func<Panel, Panel?> addCopy, Func<Panel, int> copyIndex, Action<int> select, Action reselectOriginal)
    {
        if (index < 0 || !Committed.Panels.TryGetValue(panelId, out var panel) || addCopy(panel) is not { } withCopy)
            return -1;
        StartDuplicate(Committed with { Panels = new Dictionary<PanelId, Panel>(Committed.Panels) { [panelId] = withCopy } }, reselectOriginal);
        var copy = copyIndex(withCopy);
        select(copy);
        return copy;
    }

    private void StartDuplicate(PageDocument withCopy, Action reselectOriginal)
    {
        BeginGesture();
        _moveBase = withCopy;
        _duplicateCancelled = reselectOriginal;
        UpdateGesture(EditResult<PageDocument>.Success(withCopy));
    }

    /// <summary>What a move drag's <c>Update*</c> works from - the committed page, with an Alt+drag's copy added.</summary>
    private PageDocument MoveBase => _moveBase ?? Committed;
}
