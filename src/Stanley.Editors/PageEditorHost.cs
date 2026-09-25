using System.Collections.Specialized;
using System.ComponentModel;
using Stanley.EditorFramework;

namespace Stanley.Editors;

/// <summary>Everything one open comic is edited through: the workspace (history + panes), the page navigator, the Characters pane and the comic's pictures.</summary>
public sealed record EditorSession(EditorWorkspace Workspace, PageNavigatorViewModel Navigator, CharacterLibraryViewModel Characters, PictureLibrary Pictures);

public static class PageEditorHost
{
    /// <summary>
    /// A fresh editing session for <paramref name="project"/>: one undo history (clean,
    /// i.e. "saved") shared by every page and character, the Pages and Characters panes
    /// docked on the left, and the first page's editor in the editor area. Picking a page
    /// swaps the page tab; opening a character adds its own tab alongside it (and alongside
    /// any other character already open) instead of replacing anything. The ribbon follows
    /// whichever tab is active, never the side panes.
    /// </summary>
    public static EditorSession CreateWorkspace(ComicProject project)
    {
        var history = new EditorHistory();
        var characters = new CharacterLibraryViewModel(history, project.Characters);
        var pictures = new PictureLibrary(project.Pictures);
        var navigator = new PageNavigatorViewModel(history, project.Pages, project.PageNumbering, characters, project.IssueLooks, pictures,
            project.Grid, project.NewPageLayout)
        {
            Fields = project.Fields
        };
        var workspace = new EditorWorkspace(history, [navigator.CurrentPage.Editor], [navigator, characters]);

        IEditorPane shownPage = navigator.CurrentPage.Editor;
        navigator.CurrentPageChanged += page =>
        {
            characters.Deselect();
            workspace.Replace(shownPage, page.Editor);
            shownPage = page.Editor;
        };
        characters.CharacterShown += item => workspace.Show(item.Editor);
        characters.PageRequested += closed => workspace.Replace(closed?.Editor, navigator.CurrentPage.Editor);
        characters.CharacterDeleted += item => workspace.Close(item.Editor);
        characters.PlaceRequested += id =>
        {
            navigator.Reveal(navigator.CurrentPage);
            navigator.CurrentPage.Editor.InsertCharacter(id);
        };

        // "In 3 panels" and whether Delete is allowed follow every page edit.
        characters.UsageCounter = id => navigator.Pages.Sum(p => p.Editor.CountPanelsShowing(id));
        characters.LookUsageCounter = (id, look) =>
            navigator.Pages.Sum(p => p.Editor.Working.Panels.Values.Count(panel => panel.CharacterInstances.Any(i => i.CharacterId == id && i.RevisionOverride == look)))
            + (navigator.IssueLooks.TryGetValue(id, out var issueLook) && issueLook == look ? 1 : 0);
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

        return new EditorSession(workspace, navigator, characters, pictures);
    }
}
