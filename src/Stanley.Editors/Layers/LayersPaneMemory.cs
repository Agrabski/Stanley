namespace Stanley.Editors;

/// <summary>
/// Where the "Layers pane is showing" switch lives (the page editors' View tab flips it; the
/// workspace shows or hides the pane to match). One setting for the whole window, not per
/// page.
/// </summary>
public interface ILayersPaneHost
{
    bool LayersPaneVisible { get; set; }

    event Action? LayersPaneVisibleChanged;
}

/// <summary>
/// Whether the Layers pane is showing - a preference of the person, not part of the comic, so
/// the app keeps it in its settings (<c>AppSettings.ShowLayers</c>); without one plugged in
/// (tests) it lives in memory only - the same arrangement as <see cref="HairUpgradeMemory"/>.
/// Showing until switched off, either way.
/// </summary>
public sealed class LayersPaneMemory : ILayersPaneHost
{
    private readonly Func<bool> _get;
    private readonly Action<bool> _set;

    /// <param name="get">Reads the persisted choice; omit to keep it in memory only.</param>
    /// <param name="set">Persists the choice.</param>
    public LayersPaneMemory(Func<bool>? get = null, Action<bool>? set = null)
    {
        var memory = true;
        _get = get ?? (() => memory);
        _set = set ?? (on => memory = on);
    }

    public bool LayersPaneVisible
    {
        get => _get();
        set
        {
            if (value == _get())
                return;
            _set(value);
            LayersPaneVisibleChanged?.Invoke();
        }
    }

    public event Action? LayersPaneVisibleChanged;
}
