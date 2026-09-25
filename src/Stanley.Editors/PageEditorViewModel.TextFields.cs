using CommunityToolkit.Mvvm.Input;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>A field on offer in the Text tab's Fields group: what it's called and what it inserts.</summary>
public sealed record TextFieldChoice(string Name, string Token, string Description)
{
    public static IReadOnlyList<TextFieldChoice> All { get; } =
    [
        new("Comic title", TextFields.TitleField, "Inserts {title}: the comic's title from File › Info, kept up to date"),
        new("Issue number", TextFields.IssueField, "Inserts {issue}: the issue number from File › Info, kept up to date - type your own words around it, e.g. \"Wydanie #{issue}\""),
    ];
}

/// <summary>Asks the view to put a field where the caret is in the inline text editor; <see cref="Handled"/> says it did.</summary>
public sealed class FieldInsertRequest(string token)
{
    public string Token { get; } = token;

    public bool Handled { get; set; }
}

public sealed partial class PageEditorViewModel
{

    /// <summary>What the fields in this page's texts show - the comic's title and issue number (set by the navigator); null draws them as typed.</summary>
    public TextFields? Fields
    {
        get;
        set => SetProperty(ref field, value);
    }

    public IReadOnlyList<TextFieldChoice> TextFieldChoices => TextFieldChoice.All;

    /// <summary>Text tab › Fields: puts a field in the text being typed (at the caret), else at the end of the selected text or bubble.</summary>
    public IRelayCommand<TextFieldChoice> InsertFieldCommand { get; private set; } = null!;

    /// <summary>Raised first by <see cref="InsertField"/>: the view inserts at the caret if its text editor is open, and marks the request handled.</summary>
    public event Action<FieldInsertRequest>? FieldInsertRequested;

    private void InitializeFieldCommands() =>
        InsertFieldCommand = new RelayCommand<TextFieldChoice>(choice =>
        {
            if (choice != null)
                InsertField(choice.Token);
        }, _ => HasSelectedText || HasSelectedBubble);

    /// <summary>Inserts <paramref name="token"/> where the user is typing, or appends it to the selected text or bubble as one undo step.</summary>
    public void InsertField(string token)
    {
        var request = new FieldInsertRequest(token);
        FieldInsertRequested?.Invoke(request);
        if (request.Handled || _selectedPanelId is not { } panelId)
            return;
        if (SelectedText is { } text)
            SetElementText(panelId, _selectedElementIndex, text.Text + token);
        else if (SelectedBubble is { } bubble)
            SetBubbleText(panelId, _selectedBubbleIndex, bubble.Text + token);
    }
}
