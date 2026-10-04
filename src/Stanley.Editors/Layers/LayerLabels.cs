using Stanley.ProjectModel.Bubbles;
using Stanley.ProjectModel.Issues;

namespace Stanley.Editors;

/// <summary>The words the Layers pane uses for things that have no name of their own: what a layer is, and a glimpse of what it says.</summary>
public static class LayerLabels
{
    /// <summary>The longest stretch of someone's lettering shown on a row.</summary>
    public const int MaxTextChars = 28;

    public static string Panel(int number) => $"Panel {number}";

    /// <summary>A thought cloud; numbered only when the page has several.</summary>
    public static string Cloud(int number, int clouds) => clouds > 1 ? $"Thought cloud {number}" : "Thought cloud";

    public static string PanelDetail(int layers) => layers switch
    {
        0 => "empty",
        1 => "1 layer",
        _ => $"{layers} layers"
    };

    public static string BubbleName(BubbleStylePreset style) => style switch
    {
        BubbleStylePreset.Shout => "Shout bubble",
        BubbleStylePreset.Whisper => "Whisper bubble",
        BubbleStylePreset.Thought => "Thought bubble",
        _ => "Speech bubble"
    };

    public static string BubbleIcon(BubbleStylePreset style) => style switch
    {
        BubbleStylePreset.Shout => "ShoutIcon",
        BubbleStylePreset.Whisper => "WhisperIcon",
        BubbleStylePreset.Thought => "ThoughtIcon",
        _ => "SpeechIcon"
    };

    public static string ElementName(PanelElement element) => element switch
    {
        ShapeElement { Closed: true } => "Shape",
        ShapeElement => "Line",
        TextElement => "Text",
        PictureElement => "Picture",
        SpeedLinesElement => "Speed lines",
        GroupElement => "Group",
        _ => "Drawing"
    };

    public static string ElementIcon(PanelElement element) => element switch
    {
        ShapeElement { Closed: true } => "RectangleIcon",
        ShapeElement => "LineIcon",
        TextElement => "TextIcon",
        PictureElement => "BackgroundIcon",
        SpeedLinesElement => "SpeedLinesIcon",
        GroupElement => "DuplicateIcon",
        _ => "PenIcon"
    };

    /// <summary>What to say about an element beyond its name: a text's words, how many things a group holds.</summary>
    public static string ElementDetail(PanelElement element) => element switch
    {
        TextElement text => Preview(text.Text),
        GroupElement group => group.Children.Count == 1 ? "1 item" : $"{group.Children.Count} items",
        _ => ""
    };

    /// <summary>What fills the panel behind everything.</summary>
    public static string BackgroundDetail(PanelBackground? background) => background switch
    {
        null => "plain paper",
        InlineBackground => "Picture",
        _ => DrawingPalette.Backgrounds.FirstOrDefault(b => Equals(b.Background, background))?.Name ?? "Custom"
    };

    /// <summary>The first line of <paramref name="text"/>, cut to fit a row; empty for nothing worth showing.</summary>
    public static string Preview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length <= MaxTextChars ? line : line[..(MaxTextChars - 1)].TrimEnd() + "…";
    }
}
