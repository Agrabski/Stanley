using CommunityToolkit.Mvvm.Input;
using Stanley.Editing;
using Stanley.Editing.Abstractions;
using Stanley.ProjectModel.Ids;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>
/// Where the comic's title page is kept track of (the page navigator): Insert › Title page
/// on any page puts one at the front of the comic, or redoes the one it has.
/// </summary>
public interface ITitlePageHost
{
    bool HasTitlePage { get; }

    bool CanRemoveTitlePage { get; }

    PageItem InsertTitlePage(TitlePageDesign design);

    void RemoveTitlePage();

    event Action? TitlePageChanged;
}

/// <summary>A title page design on offer in Insert › Title page.</summary>
public sealed record TitlePageChoice(TitlePageDesign Design)
{
    public string Name => TitlePages.Name(Design);

    public string Description => TitlePages.Description(Design);
}

public sealed partial class PageEditorViewModel
{
    private ITitlePageHost? _titlePageHost;

    /// <summary>Where the comic's title page lives (the navigator); null for a page edited on its own, which then offers no title page.</summary>
    public ITitlePageHost? TitlePageHost
    {
        get => _titlePageHost;
        set
        {
            if (_titlePageHost != null)
                _titlePageHost.TitlePageChanged -= RaiseTitlePageChanged;
            _titlePageHost = value;
            if (_titlePageHost != null)
                _titlePageHost.TitlePageChanged += RaiseTitlePageChanged;
            RaiseTitlePageChanged();
        }
    }

    public bool HasTitlePageHost => _titlePageHost != null;

    public bool HasTitlePage => _titlePageHost?.HasTitlePage ?? false;

    public IReadOnlyList<TitlePageChoice> TitlePageChoices { get; } = TitlePages.All.Select(d => new TitlePageChoice(d)).ToList();

    /// <summary>Insert › Title page: a title page in the chosen design at the front of the comic - or the comic's title page redone in it.</summary>
    public IRelayCommand<TitlePageChoice> InsertTitlePageCommand { get; private set; } = null!;

    public IRelayCommand RemoveTitlePageCommand { get; private set; } = null!;

    private void InitializeTitlePageCommands()
    {
        InsertTitlePageCommand = new RelayCommand<TitlePageChoice>(choice =>
        {
            if (choice != null)
                _titlePageHost?.InsertTitlePage(choice.Design);
        }, _ => _titlePageHost != null);
        RemoveTitlePageCommand = new RelayCommand(() => _titlePageHost?.RemoveTitlePage(), () => _titlePageHost?.CanRemoveTitlePage ?? false);
    }

    private void RaiseTitlePageChanged()
    {
        OnPropertyChanged(nameof(HasTitlePageHost));
        OnPropertyChanged(nameof(HasTitlePage));
        InsertTitlePageCommand?.NotifyCanExecuteChanged();
        RemoveTitlePageCommand?.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- panel border

    /// <summary>
    /// Whether the selected panel has its border (Panel tab › Border). Off for a title page's
    /// backgrounds and for open, borderless panels. Not a layout change, so a locked layout allows it.
    /// </summary>
    public bool SelectedPanelHasBorder
    {
        get => SelectedPanel is { Borderless: false };
        set
        {
            if (_selectedPanelId is { } id && SelectedPanel is { } panel && panel.Borderless == value)
                SetPanelBorder(id, value);
            OnPropertyChanged();
        }
    }

    /// <summary>Draws or leaves off a panel's border, in one undo step.</summary>
    public void SetPanelBorder(PanelId panelId, bool border) =>
        Apply(EditPanel(Working, panelId, p => EditResult<Panel>.Success(p.Borderless == !border ? p : p with { Borderless = !border })));
}
