using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Stanley.Editors;

/// <summary>Something a control's drag can be one gesture of - one undo step however far it goes.</summary>
public interface IDragGesture
{
    void BeginDrag();

    void EndDrag();
}

/// <summary>
/// Makes a slider's drag one gesture: <c>local:DragGesture.Target="{Binding}"</c> on a
/// slider whose data context is an <see cref="IDragGesture"/>. The gesture opens on press
/// (before the slider jumps to the pointer) and closes on release - the same thing the
/// ribbons wire up by hand for their named sliders, for sliders in templates and popups.
/// </summary>
public static class DragGesture
{
    public static readonly AttachedProperty<IDragGesture?> TargetProperty =
        AvaloniaProperty.RegisterAttached<Control, IDragGesture?>("Target", typeof(DragGesture));

    static DragGesture()
    {
        TargetProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.OldValue is null && e.NewValue is not null)
            {
                control.AddHandler(InputElement.PointerPressedEvent, (_, _) => GetTarget(control)?.BeginDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
                control.AddHandler(InputElement.PointerReleasedEvent, (_, _) => GetTarget(control)?.EndDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
                control.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => GetTarget(control)?.EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
            }
        });
    }

    public static IDragGesture? GetTarget(Control control) => control.GetValue(TargetProperty);

    public static void SetTarget(Control control, IDragGesture? value) => control.SetValue(TargetProperty, value);
}
