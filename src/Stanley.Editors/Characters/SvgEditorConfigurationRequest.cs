namespace Stanley.Editors;

/// <summary>
/// Asks the view to set up an SVG editor (none is configured yet) before drawing for
/// <paramref name="Slot"/> can go ahead; the view answers with <see cref="SvgEditorPicker"/>
/// and then calls <see cref="CharacterEditorViewModel.DrawYourOwn"/> again.
/// </summary>
public sealed record SvgEditorConfigurationRequest(string Slot);