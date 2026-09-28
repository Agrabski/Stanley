using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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

        // Comic strips and webcomics: a tile per template, its page drawn to shape with its panels.
        foreach (var template in ComicTemplates.All)
        {
            var size = template.ExportWidthPx is { } width
                ? $"{width} × {template.ExportHeightPx} px"
                : $"{template.Trim.Size.WidthMm:0} × {template.Trim.Size.HeightMm:0} mm";
            var tile = new Button
            {
                Name = "Template" + string.Concat(template.Name.Split(' ', '-').Select(word => char.ToUpperInvariant(word[0]) + word[1..])),
                Classes = { "tile" },
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new LayoutPresetPreview { Preset = template.Layout, PageSize = template.Trim.Size, Grid = template.Grid, Width = 104, Height = 90 },
                        new TextBlock { Text = template.Name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 },
                        new TextBlock { Text = size, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, Opacity = 0.7 }
                    }
                }
            };
            ToolTip.SetTip(tile, template.Description);
            tile.Click += (_, _) => (DataContext as MainWindowViewModel)?.NewFromTemplateCommand.Execute(template);
            (template.Kind == ComicTemplateKind.Strip ? StripTiles : WebcomicTiles).Children.Add(tile);
        }
    }

    /// <summary>File › My Assets › Rename: the name box takes the focus with its text selected as it appears...</summary>
    private void AssetNamePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.OldValue is false && sender is TextBox { IsVisible: true } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    /// <summary>...and Enter keeps the new name, Esc the old one.</summary>
    private void AssetNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: MyAssetTile tile } box)
            return;
        if (e.Key == Key.Enter)
        {
            tile.Name = box.Text ?? "";
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            box.Text = tile.Name;
            tile.IsEditingName = false;
            e.Handled = true;
        }
    }
}
