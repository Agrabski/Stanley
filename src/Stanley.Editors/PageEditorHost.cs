using System.Collections.Specialized;
using System.ComponentModel;
using Stanley.EditorFramework;

namespace Stanley.Editors;

/// <summary>Everything one open comic is edited through: the workspace (history + panes), the page navigator and the Characters pane.</summary>
public sealed record EditorSession(EditorWorkspace Workspace, PageNavigatorViewModel Navigator, CharacterLibraryViewModel Characters);

public static class PageEditorHost
{
    /// <summary>
    /// A fresh editing session for <paramref name="project"/>: one undo history (clean,
    /// i.e. "saved") shared by every page and character, the Pages and Characters panes
    /// docked on the left, and the first page's editor in the editor area. Picking a page
    /// or a character swaps which editor is shown; the ribbon follows the shown editor,
    /// never the side panes.
    /// </summary>
    public static EditorSession CreateWorkspace(ComicProject project)
    {
        var history = new EditorHistory();
        var characters = new CharacterLibraryViewModel(history, project.Characters);
        var navigator = new PageNavigatorViewModel(history, project.Pages, project.PageNumbering, characters);
        var workspace = new EditorWorkspace(history, [navigator.CurrentPage.Editor], [navigator, characters]);

        navigator.CurrentPageChanged += page =>
        {
            characters.Deselect();
            workspace.SwitchTo(page.Editor);
        };
        characters.CharacterShown += item => workspace.SwitchTo(item.Editor);
        characters.PageRequested += () => workspace.SwitchTo(navigator.CurrentPage.Editor);
        characters.PlaceRequested += id =>
        {
            navigator.Reveal(navigator.CurrentPage);
            navigator.CurrentPage.Editor.InsertCharacter(id);
        };

        // "In 3 panels" and whether Delete is allowed follow every page edit.
        characters.UsageCounter = id => navigator.Pages.Sum(p => p.Editor.CountPanelsShowing(id));
        void OnPageChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PageEditorViewModel.Committed))
                characters.RefreshUsage();
        }
        foreach (var page in navigator.Pages)
            page.Editor.PropertyChanged += OnPageChanged;
        navigator.Pages.CollectionChanged += (_, e) =>
        {
            foreach (var added in e.NewItems?.OfType<PageItem>() ?? [])
            {
                added.Editor.PropertyChanged -= OnPageChanged;
                added.Editor.PropertyChanged += OnPageChanged;
            }
            characters.RefreshUsage();
        };

        return new EditorSession(workspace, navigator, characters);
    }
}
