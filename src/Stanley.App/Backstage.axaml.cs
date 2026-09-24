using Avalonia.Controls;
using Avalonia.Layout;
using Stanley.Editing;
using Stanley.Editors;

namespace Stanley.App;

public partial class Backstage : UserControl
{
    public Backstage()
    {
        InitializeComponent();

        // File > New: a tile per starting layout, like Word's template gallery - the
        // one-panel page first, as "Blank page".
        foreach (var preset in PanelLayoutPresets.All)
        {
            var isBlank = preset.ColumnsPerRow is [1];
            var tile = new Button
            {
                Classes = { "tile" },
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new LayoutPresetPreview { Preset = preset, Width = 76, Height = 108, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = isBlank ? "Blank page" : preset.Name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 }
                    }
                }
            };
            ToolTip.SetTip(tile, isBlank ? "A new comic with one panel filling the page" : $"A new comic laid out as {preset.Name.ToLowerInvariant()}");
            tile.Click += (_, _) => (DataContext as MainWindowViewModel)?.NewCommand.Execute(isBlank ? null : preset);
            NewTiles.Children.Add(tile);
        }
    }
}
