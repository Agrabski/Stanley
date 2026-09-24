using Avalonia.Controls;
using Avalonia.Input;
using Stanley.EditorFramework;
using Stanley.Editors;

namespace Stanley.App;

public partial class MainWindow : Window
{
    private readonly EditorHistory _history;

    public MainWindow()
    {
        InitializeComponent();

        var (history, layout, editor) = PageEditorHost.CreateDemoLayout();
        _history = history;
        EditorDock.Layout = layout;
        EditorDock.Factory = layout.Factory;

        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control), Command = history.UndoCommand });
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), Command = history.RedoCommand });

        Editor = editor;
    }

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public PageEditorViewModel Editor { get; }

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public EditorHistory History => _history;
}
