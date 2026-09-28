namespace Stanley.Editors;

/// <summary>One entry of the split "Left eye"/"Right eye" expression dropdowns (docs/sticker-system.md §21): a variant and its readable name.</summary>
public sealed record EyeExpressionOption(string Value, string Label);
