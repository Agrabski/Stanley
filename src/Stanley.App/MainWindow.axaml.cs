using Avalonia.Controls;
using Avalonia.Input;
using Stanley.EditorFramework;
using Stanley.Editors;

namespace Stanley.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var (workspace, editor) = PageEditorHost.CreateDemoWorkspace();
        Workspace = workspace;
        Editor = editor;
        DataContext = workspace;
        EditorDock.Layout = workspace.Layout;
        EditorDock.Factory = workspace.Factory;

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control), Command = workspace.History.UndoCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), Command = workspace.History.RedoCommand });
    }

    /// <summary>The open project's session: history, pane layout, and which pane the ribbon follows.</summary>
    public EditorWorkspace Workspace { get; }

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public PageEditorViewModel Editor { get; }

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public EditorHistory History => Workspace.History;

    /// <summary>Exposed for headless UI tests: the window-level ribbon bar, above the dock area.</summary>
    public Control RibbonBarControl => RibbonBar;
}
