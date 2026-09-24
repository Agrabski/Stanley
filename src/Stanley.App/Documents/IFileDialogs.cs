namespace Stanley.App.Documents;

public enum SaveChangesChoice
{
    Save,
    DontSave,
    Cancel
}

/// <summary>
/// The OS-facing questions File operations need answered. Behind an interface so the
/// document logic (<see cref="MainWindowViewModel"/>) is testable headlessly with a fake,
/// where there's no real folder picker to click through.
/// </summary>
public interface IFileDialogs
{
    /// <summary>A folder to open a project from or save one into; null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>A file path to export to; null if cancelled.</summary>
    Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName);

    /// <summary>Word's "Want to save your changes to …?" prompt.</summary>
    Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle);
}
