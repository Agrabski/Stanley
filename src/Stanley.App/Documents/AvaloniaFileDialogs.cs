using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Stanley.Editors;

namespace Stanley.App.Documents;

/// <summary>The real dialogs: the platform's folder/save pickers via Avalonia's storage provider, and a small modal for "save changes?".</summary>
public sealed class AvaloniaFileDialogs(Window owner) : IFileDialogs
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveLocationAsync(string title, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            // A comic is a folder, and a name that's taken gets "(2)" rather than replacing anything - nothing to confirm.
            ShowOverwritePrompt = false
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = [$"*.{extension}"] }],
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public async Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle)
    {
        var dialog = new Window
        {
            Title = "Stanley",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        Button MakeButton(string text, SaveChangesChoice choice, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = text, MinWidth = 96, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
                button.Classes.Add("accent");
            button.Click += (_, _) => dialog.Close(choice);
            return button;
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"Want to save your changes to \"{documentTitle}\"?", FontSize = 15, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        MakeButton("Save", SaveChangesChoice.Save, isDefault: true),
                        MakeButton("Don't Save", SaveChangesChoice.DontSave),
                        MakeButton("Cancel", SaveChangesChoice.Cancel, isCancel: true)
                    }
                }
            }
        };

        return await dialog.ShowDialog<SaveChangesChoice?>(owner) ?? SaveChangesChoice.Cancel;
    }

    public Task<string?> PickSvgEditorAsync(string? currentPath) => SvgEditorPicker.PickAsync(owner, currentPath);

    public async Task<bool> AskDeleteIssueAsync(string caption)
    {
        var dialog = new Window
        {
            Title = "Stanley",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        // Delete is red (the "danger" class, RibbonStyles.axaml) because it can't be undone - and Cancel, not Delete, is the
        // default, so a stray Enter keeps the issue.
        Button MakeButton(string text, bool choice, bool isDefault = false, bool isCancel = false, bool isDanger = false)
        {
            var button = new Button { Content = text, MinWidth = 96, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
                button.Classes.Add("accent");
            if (isDanger)
                button.Classes.Add("danger");
            button.Click += (_, _) => dialog.Close(choice);
            return button;
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"Delete {caption}? Its pages, panels and pictures go with it - this can't be undone.", FontSize = 15, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        MakeButton("Delete", true, isDanger: true),
                        MakeButton("Cancel", false, isDefault: true, isCancel: true)
                    }
                }
            }
        };

        return await dialog.ShowDialog<bool?>(owner) ?? false;
    }

    public async Task<bool> AskInstallUpdateAsync(string version, string? notes)
    {
        var dialog = new Window
        {
            Title = "Stanley",
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        Button MakeButton(string text, bool choice, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = text, MinWidth = 96, IsDefault = isDefault, IsCancel = isCancel, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (isDefault)
                button.Classes.Add("accent");
            button.Click += (_, _) => dialog.Close(choice);
            return button;
        }

        var content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 18 };
        content.Children.Add(new TextBlock
        {
            Text = $"Version {version} of Stanley is available.",
            FontSize = 15,
            MaxWidth = 420,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(notes))
            content.Children.Add(new TextBlock { Text = notes, MaxWidth = 420, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8 });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                MakeButton("Update Now", true, isDefault: true),
                MakeButton("Later", false, isCancel: true)
            }
        });
        dialog.Content = content;

        return await dialog.ShowDialog<bool?>(owner) ?? false;
    }
}
