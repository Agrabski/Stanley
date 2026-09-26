using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stanley.Editors;

/// <summary>
/// A control's keyboard shortcut, shown on it while Ctrl is held - the way Office shows its
/// KeyTips while Alt is: hold Ctrl and every button on screen with a shortcut gets a keycap
/// right by it (under a big ribbon button or an icon, just past a small one), let go and they're
/// gone. Nothing moves to make room: the keycaps float over the window. Written once, on the
/// control: <c>local:Shortcut.Keys="Ctrl+C"</c>. Menus show theirs all the time, with
/// <see cref="MenuItem.InputGesture"/>.
/// </summary>
public static class Shortcut
{
    public static readonly AttachedProperty<string?> KeysProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Keys", typeof(Shortcut));

    private static readonly ConditionalWeakTable<TopLevel, Reveal> Reveals = new();

    /// <summary>How long Ctrl is held before the keycaps show, so a quick Ctrl+C doesn't flash them.</summary>
    public static TimeSpan RevealDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    public static string? GetKeys(Control control) => control.GetValue(KeysProperty);

    public static void SetKeys(Control control, string? keys) => control.SetValue(KeysProperty, keys);

    /// <summary>Makes holding Ctrl in <paramref name="topLevel"/> show the keycaps of everything in it with <see cref="KeysProperty"/>.</summary>
    public static void RevealWhileCtrlHeld(TopLevel topLevel)
    {
        if (!Reveals.TryGetValue(topLevel, out _))
            Reveals.Add(topLevel, new Reveal(topLevel));
    }

    /// <summary>The keycaps showing in <paramref name="topLevel"/> now, and what each is on - for headless UI tests.</summary>
    public static IReadOnlyList<(Control Host, KeyCap Cap)> Showing(TopLevel topLevel) =>
        Reveals.TryGetValue(topLevel, out var reveal) ? reveal.Shown : [];

    private sealed class Reveal
    {
        private readonly TopLevel _topLevel;
        private readonly List<(Control Host, KeyCap Cap)> _shown = [];
        private IDisposable? _pending;

        public Reveal(TopLevel topLevel)
        {
            _topLevel = topLevel;
            // Tunnelled and handled-too, so whatever has the keyboard, the window hears Ctrl first.
            topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
            topLevel.AddHandler(InputElement.PointerPressedEvent, (_, _) => Hide(), RoutingStrategies.Tunnel, handledEventsToo: true);
            if (topLevel is WindowBase window)
                window.Deactivated += (_, _) => Hide();
        }

        public IReadOnlyList<(Control Host, KeyCap Cap)> Shown => _shown;

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl)
            {
                // Held down, the key repeats: once is enough.
                if (_shown.Count == 0 && _pending is null)
                {
                    if (RevealDelay <= TimeSpan.Zero)
                        Show();
                    else
                        _pending = DispatcherTimer.RunOnce(Show, RevealDelay);
                }
                return;
            }
            Hide(); // Ctrl and another key: a shortcut being used, not looked for
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl)
                Hide();
        }

        private void Show()
        {
            _pending = null;
            Hide();
            var placed = new List<(KeyCapPlacement Placement, Rect At)>();
            foreach (var host in _topLevel.GetVisualDescendants().OfType<Control>().ToList())
            {
                if (GetKeys(host) is not { Length: > 0 } keys || !host.IsEffectivelyVisible || host.Bounds.Width <= 0
                    || host.TranslatePoint(default, _topLevel) is not { } origin || !IsOnTop(host, origin))
                    continue;
                var cap = new KeyCap { Keys = keys };
                cap.Measure(Size.Infinity);
                var placement = new KeyCapPlacement(cap, WhereFor(host, cap.DesiredSize.Width));
                placed.Add((placement, new Rect(origin + placement.Position(host.Bounds.Size), cap.DesiredSize)));
                AdornerLayer.SetIsClipEnabled(placement, false);
                AdornerLayer.SetAdorner(host, placement);
                _shown.Add((host, cap));
            }
            Unstack(placed);
        }

        /// <summary>
        /// Keycaps wider than their buttons (a row of small icons, say) would cover each other:
        /// top to bottom, left to right, each one that would steps down below the one in its way,
        /// staying under its own button.
        /// </summary>
        private static void Unstack(List<(KeyCapPlacement Placement, Rect At)> placed)
        {
            var settled = new List<Rect>();
            foreach (var (placement, at) in placed.OrderBy(p => p.At.Top).ThenBy(p => p.At.Left))
            {
                var rect = at;
                for (var i = 0; i < 8 && settled.FirstOrDefault(r => r.Intersects(rect)) is { Width: > 0 } blocker; i++)
                    rect = rect.WithY(blocker.Bottom + 1);
                placement.Offset = new Vector(0, rect.Top - at.Top);
                settled.Add(rect);
            }
        }

        private void Hide()
        {
            _pending?.Dispose();
            _pending = null;
            foreach (var (host, _) in _shown)
                AdornerLayer.SetAdorner(host, null);
            _shown.Clear();
        }

        /// <summary>Whether <paramref name="host"/> is what's on show at its middle - not under the File view, say, which covers the ribbon while it's open.</summary>
        private bool IsOnTop(Control host, Point origin)
        {
            var middle = origin + new Point(host.Bounds.Width / 2, host.Bounds.Height / 2);
            var top = _topLevel.GetVisualsAt(middle, v => v.IsVisible && v is not KeyCapPlacement).FirstOrDefault();
            return top is not null && (ReferenceEquals(top, host) || host.IsVisualAncestorOf(top));
        }

        /// <summary>
        /// A big ribbon button (an icon over its name), an icon or a lone word takes its keys
        /// underneath it; a row (icon and name side by side) at its far end - inside it, when it's
        /// wide enough to have room (the File view's commands), else just past it.
        /// </summary>
        private static Where WhereFor(Control host, double capWidth)
        {
            if (host is ContentControl { Content: StackPanel { Orientation: Orientation.Vertical } or PathIcon or string })
                return Where.Below;
            var content = (host as ContentControl)?.Presenter?.Child;
            var contentEnd = content?.TranslatePoint(new Point(content.Bounds.Width, 0), host)?.X ?? host.Bounds.Width;
            return host.Bounds.Width - contentEnd >= capWidth + 12 ? Where.AtEnd : Where.After;
        }
    }

    private enum Where
    {
        Below,
        AtEnd,
        After
    }

    /// <summary>Lays a keycap out against what it's on - centred on its bottom edge, at its far end, or just past it - without taking up any room.</summary>
    private sealed class KeyCapPlacement : Panel
    {
        private readonly KeyCap _cap;
        private readonly Where _where;

        public KeyCapPlacement(KeyCap cap, Where where)
        {
            _cap = cap;
            _where = where;
            IsHitTestVisible = false;
            Children.Add(cap);
        }

        /// <summary>How far it's stepped aside from where it would go, to keep clear of another keycap.</summary>
        public Vector Offset
        {
            get;
            set
            {
                field = value;
                InvalidateArrange();
            }
        }

        /// <summary>Where the keycap goes against something <paramref name="host"/>-sized, before any <see cref="Offset"/>.</summary>
        public Point Position(Size host)
        {
            var size = _cap.DesiredSize;
            return _where switch
            {
                Where.Below => new Point((host.Width - size.Width) / 2, host.Height - size.Height / 2),
                Where.AtEnd => new Point(host.Width - size.Width - 8, (host.Height - size.Height) / 2),
                _ => new Point(host.Width + 2, (host.Height - size.Height) / 2)
            };
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _cap.Measure(Size.Infinity);
            return default;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _cap.Arrange(new Rect(Position(finalSize) + Offset, _cap.DesiredSize));
            return finalSize;
        }
    }
}

/// <summary>A shortcut's keys set apart as a small keycap, shown by a button while Ctrl is held (<see cref="Shortcut"/>).</summary>
public sealed class KeyCap : Border
{
    private readonly TextBlock _text;

    public KeyCap()
    {
        Classes.Add("shortcutKeys");
        Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30));
        BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        Padding = new Thickness(3, 0);
        BoxShadow = BoxShadows.Parse("0 1 3 0 #60000000");
        _text = new TextBlock { FontSize = 10.5, Foreground = Brushes.White, FontWeight = FontWeight.SemiBold };
        Child = _text;
    }

    public string Keys
    {
        get => _text.Text ?? "";
        set => _text.Text = value;
    }
}
