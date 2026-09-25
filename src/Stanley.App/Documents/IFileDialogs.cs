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
    /// <summary>A project folder to open; null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Where to save the comic and what to call it, as any program's Save As asks: a name box
    /// starting as <paramref name="suggestedName"/>, and a place. Returns the place joined with
    /// the name typed - the comic's folder to be; null if cancelled.
    /// </summary>
    Task<string?> PickSaveLocationAsync(string title, string suggestedName);

    /// <summary>A file path to export to; null if cancelled.</summary>
    Task<string?> PickExportFileAsync(string title, string suggestedFileName, string extension, string fileTypeName);

    /// <summary>Word's "Want to save your changes to …?" prompt.</summary>
    Task<SaveChangesChoice> AskSaveChangesAsync(string documentTitle);

    /// <summary>File &gt; Options &gt; SVG editor's picker (detected programs, or Browse... to any executable); null if cancelled.</summary>
    Task<string?> PickSvgEditorAsync(string? currentPath);
}
