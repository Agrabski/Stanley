using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Stanley.Editors;

/// <summary>
/// File &gt; Options &gt; SVG editor's "Change..." button, and the prompt the first time "Draw
/// your own..."/"Edit drawing..." runs with none set up yet: pick a detected program
/// (<see cref="SvgEditorCandidates"/>) or browse to any executable. A small modal window
/// built in code, like <c>AvaloniaFileDialogs.AskSaveChangesAsync</c> - no separate .axaml,
/// and nothing to carry back but the chosen path.
/// </summary>
public static class SvgEditorPicker
{
    /// <summary>Shows the picker over <paramref name="owner"/>; returns the chosen executable path, or null if cancelled.</summary>
    public static async Task<string?> PickAsync(Window owner, string? current)
    {
        var candidates = SvgEditorCandidates.Detect();

        var dialog = new Window
        {
            Title = "Stanley",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        var pathBox = new TextBox { Width = 320, Text = current, PlaceholderText = "Path to an SVG editor" };

        var browseButton = new Button { Content = "Browse..." };
        browseButton.Click += async (_, _) =>
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose an SVG editor", AllowMultiple = false });
            if (files is [var picked] && picked.TryGetLocalPath() is { } path)
                pathBox.Text = path;
        };

        var content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12, MaxWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = "Choose the program Stanley opens sticker art in to draw - a vector editor; Inkscape is a good free one. " +
                   "Stanley never guesses: the operating system's default app for SVG files is often just a viewer.",
            TextWrapping = TextWrapping.Wrap
        });

        if (candidates.Count > 0)
        {
            content.Children.Add(new TextBlock { Text = "Found on this computer:", Opacity = 0.7 });
            foreach (var candidate in candidates)
            {
                var button = new Button
                {
                    Content = $"{candidate.Name} ({candidate.Path})",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                button.Click += (_, _) => pathBox.Text = candidate.Path;
                content.Children.Add(button);
            }
        }

        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pathBox, browseButton } });

        Button MakeButton(string text, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = text, MinWidth = 96, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
                button.Classes.Add("accent");
            button.Click += (_, _) => dialog.Close(isCancel ? null : pathBox.Text);
            return button;
        }

        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { MakeButton("Use this", isDefault: true), MakeButton("Cancel", isCancel: true) }
        });
        dialog.Content = content;

        var result = await dialog.ShowDialog<string?>(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }
}
