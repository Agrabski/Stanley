using Avalonia.Controls;

namespace Stanley.Editors;

/// <summary>The canvas layout's inspector for a character; see CharacterInspector.axaml. Everything goes through <see cref="CharacterEditorViewModel"/>, as the character ribbon does.</summary>
public partial class CharacterInspector : UserControl
{
    public CharacterInspector()
    {
        InitializeComponent();
    }

    /// <summary>Finds a named control in any section (each section is its own name scope) - for headless UI tests.</summary>
    public T? FindInSections<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? Sections.Children.OfType<UserControl>().Select(section => section.FindControl<T>(name)).FirstOrDefault(found => found is not null);
}
