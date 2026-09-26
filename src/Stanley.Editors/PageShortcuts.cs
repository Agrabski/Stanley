namespace Stanley.Editors;

/// <summary>One keyboard shortcut, as View › Keyboard shortcuts lists it: the keys, and what they do.</summary>
public sealed record ShortcutEntry(string Keys, string Does);

/// <summary>A heading's worth of <see cref="ShortcutEntry"/>s.</summary>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Entries);

/// <summary>
/// Every keyboard shortcut the page editor has, for View › Keyboard shortcuts (F1) - the ones
/// buttons and menus show beside themselves, and the ones nothing can show: arrows, Space,
/// the keys held while dragging. Figma's and Word's keyboard shortcut lists, in short.
/// </summary>
public static class PageShortcuts
{
    public static IReadOnlyList<ShortcutGroup> All { get; } =
    [
        new("Tools",
        [
            new("V", "Select, move and resize"),
            new("P", "Draw a panel"),
            new("B", "Add a bubble"),
            new("T", "Add text"),
            new("D", "Draw freehand"),
            new("L", "Draw a line"),
            new("R", "Draw a rectangle"),
            new("E", "Draw an ellipse"),
            new("H", "Move around the page"),
            new("Esc", "Back to Select, then let go of the selection"),
        ]),
        new("Edit",
        [
            new("Ctrl+C", "Copy what's selected"),
            new("Ctrl+X", "Cut"),
            new("Ctrl+V", "Paste"),
            new("Ctrl+D", "Duplicate"),
            new("Alt+drag", "Drag off a copy"),
            new("Delete", "Delete what's selected"),
            new("Enter or F2", "Edit a bubble's or a text's words"),
            new("Ctrl+Z", "Undo"),
            new("Ctrl+Y", "Redo"),
        ]),
        new("Moving and shaping",
        [
            new("Arrow keys", "Nudge what's selected 1 mm"),
            new("Shift+arrow keys", "Nudge it 5 mm"),
            new("Alt while dragging", "Place freely, without snapping"),
            new("Ctrl+drag a bubble", "Move it with its tail"),
            new("Shift while drawing", "Keep a line level, a rectangle square, an ellipse round"),
            new("Shift+drag a corner", "Resize a character without the panel's others"),
            new("F", "Character: front view"),
            new("S", "Character: side view"),
        ]),
        new("View",
        [
            new("Ctrl+0", "Fit the page"),
            new("Ctrl+1", "Actual size"),
            new("Ctrl++", "Zoom in"),
            new("Ctrl+−", "Zoom out"),
            new("Ctrl+scroll", "Zoom at the pointer"),
            new("Space+drag", "Move around the page"),
            new("F1", "This list"),
        ]),
        new("Comic",
        [
            new("Ctrl+S", "Save"),
            new("Ctrl+Shift+S", "Save As"),
            new("Ctrl+N", "New comic"),
            new("Ctrl+O", "Open a comic"),
            new("Alt+F", "File view"),
            new("Ctrl+D", "Pages pane: duplicate the page"),
            new("Ctrl+←, Ctrl+→", "Pages pane: move the page"),
        ]),
    ];
}
