using Avalonia.Controls;
using Avalonia.Layout;

namespace Stanley.Editors;

/// <summary>The page editor's ribbon content; see PageEditorRibbon.axaml. Everything it does goes through <see cref="PageEditorViewModel"/> commands, since it lives outside the pane's own view.</summary>
public partial class PageEditorRibbon : UserControl
{
    public PageEditorRibbon()
    {
        InitializeComponent();

        // The flyout's content lives in a popup, outside this control's tree, so the
        // preset buttons are wired here rather than with bindings that would have to
        // reach back across the popup boundary.
        foreach (var preset in Editing.PanelLayoutPresets.All)
        {
            var button = new Button
            {
                Classes = { "tool" },
                Width = 80,
                Height = 92,
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new LayoutPresetPreview { Preset = preset, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = preset.Name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            ToolTip.SetTip(button, $"{preset.Name}: {string.Join(" / ", preset.ColumnsPerRow)} panels per row");
            button.Click += (_, _) =>
            {
                ViewModel?.ApplyLayoutCommand.Execute(preset);
                LayoutButton.Flyout?.Hide();
            };
            LayoutPresetPanel.Children.Add(button);
        }

        MarginInput.ValueChanged += (_, e) =>
        {
            if (ViewModel is { } vm && e.NewValue is { } value)
                vm.MarginMm = (double)value;
        };
        GutterInput.ValueChanged += (_, e) =>
        {
            if (ViewModel is { } vm && e.NewValue is { } value)
                vm.GutterMm = (double)value;
        };
    }

    private PageEditorViewModel? ViewModel => DataContext as PageEditorViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            MarginInput.Value = (decimal)vm.MarginMm;
            GutterInput.Value = (decimal)vm.GutterMm;
        }
    }
}
