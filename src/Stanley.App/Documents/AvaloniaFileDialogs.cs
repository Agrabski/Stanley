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
}
