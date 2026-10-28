using CommunityToolkit.Mvvm.Input;

namespace Stanley.Editors;

public sealed partial class PageEditorViewModel
{
    private RelayCommand? _newShapeCharacterCommand;

    /// <summary>
    /// Insert › Character › From shapes (docs/shape-characters.md §9.1): a brand-new character
    /// built from shapes, placed in the selected panel and opened in its editor on the creature
    /// presets - it has no shape yet worth keeping, unlike a new human body.
    /// </summary>
    public IRelayCommand NewShapeCharacterCommand => _newShapeCharacterCommand ??= new RelayCommand(() =>
    {
        if (_catalog == null)
            return;
        var character = _catalog.CreateCharacter(fromShapes: true);
        InsertCharacter(character.Id);
        _catalog.OpenCharacter(character.Id);
    }, () => _catalog != null && Working.PanelOrder.Count > 0);
}
