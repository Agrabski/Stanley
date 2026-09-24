using Stanley.ProjectModel.Geometry;
using Stanley.ProjectModel.Ids;

namespace Stanley.ProjectModel.Bubbles;

/// <summary>
/// One speech bubble: a bezier outline, a style preset, any number of tails, and its
/// text. Embedded directly in the one panel that ever references it (<see cref="Ids.BubbleId"/>
/// only needs to be stable within that panel), the same way <c>CharacterInstance</c> is.
/// <c>Stanley.Rendering</c> turns this into pixels; <c>Stanley.Editing</c> owns what
/// counts as a valid edit to it (minimum size, non-empty shape, etc.) - this record has
/// no behaviour of its own.
/// </summary>
public sealed record Bubble(
    BubbleId Id,
    BubbleShape Shape,
    BubbleStylePreset Style,
    IReadOnlyList<BubbleTail> Tails,
    string Text);
