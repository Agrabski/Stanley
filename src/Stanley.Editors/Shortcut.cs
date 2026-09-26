using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Stanley.Editors;

/// <summary>
/// A control's keyboard shortcut, shown in its tooltip the way Figma and Word show one: what the
/// button does, then its keys set apart in a keycap - so a forgotten shortcut is one hover away.
/// Written next to a plain tip: <c>ToolTip.Tip="Copy what's selected" local:Shortcut.Keys="Ctrl+C"</c>.
/// Menus show theirs with <see cref="MenuItem.InputGesture"/>.
/// </summary>
public static class Shortcut
{
    public static readonly AttachedProperty<string?> KeysProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Keys", typeof(Shortcut));

    private static bool _composing;

    static Shortcut()
    {
        KeysProperty.Changed.AddClassHandler<Control>((control, _) => Compose(control));
        // Either can come first in XAML, and a tip can change later; the keys stay with it.
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (!_composing && GetKeys(control) != null)
                Compose(control);
        });
    }

    public static string? GetKeys(Control control) => control.GetValue(KeysProperty);

    public static void SetKeys(Control control, string? keys) => control.SetValue(KeysProperty, keys);

    private static void Compose(Control control)
    {
        var tip = ToolTip.GetTip(control);
        var text = tip is ShortcutTip composed ? composed.Text : tip as string ?? "";
        _composing = true;
        try
        {
            ToolTip.SetTip(control, GetKeys(control) is { Length: > 0 } keys ? new ShortcutTip(text, keys) : text.Length > 0 ? text : null);
        }
        finally
        {
            _composing = false;
        }
    }
}

/// <summary>A tooltip with a keyboard shortcut: the words, and the keys in a keycap beside them (<see cref="Shortcut"/>).</summary>
public sealed class ShortcutTip : DockPanel
{
    public ShortcutTip(string text, string keys)
    {
        Text = text;
        Keys = keys;
        var keycap = new Border
        {
            Classes = { "shortcutKeys" },
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(text.Length > 0 ? 12 : 0, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = keys, FontSize = 11, Opacity = 0.8 }
        };
        SetDock(keycap, Avalonia.Controls.Dock.Right);
        Children.Add(keycap);
        if (text.Length > 0)
            Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, VerticalAlignment = VerticalAlignment.Center });
    }

    /// <summary>What the tip says, without the keys.</summary>
    public string Text { get; }

    /// <summary>The keys, as shown: "Ctrl+C", "V".</summary>
    public string Keys { get; }

    public override string ToString() => Text.Length > 0 ? $"{Text} ({Keys})" : Keys;
}
