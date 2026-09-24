using Dock.Model.Core;

namespace Stanley.EditorFramework;

/// <summary>
/// The non-generic face of an <see cref="EditorViewModel{TDocument}"/>: what window-level
/// chrome (the ribbon, the title bar) needs to know about whichever editor pane is active,
/// without caring what kind of document it edits.
/// </summary>
public interface IEditorPane : IDockable
{
    EditorHistory History { get; }
}
