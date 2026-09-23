using Avalonia.Controls;
using Avalonia.Interactivity;
using Stanley.Bubbles;

namespace Stanley.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public BubbleCanvasControl CanvasControl => Canvas;
    public RadioButton ShoutStyleRadio => ShoutRadio;
    public Button AddTailButtonControl => AddTailButton;

    private void OnStyleChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true } radio) return;
        var style = radio.Name switch
        {
            nameof(ShoutRadio) => BubbleStylePreset.Shout,
            nameof(WhisperRadio) => BubbleStylePreset.Whisper,
            _ => BubbleStylePreset.Speech
        };
        Canvas.SetStyle(style);
    }

    private void OnAddTail(object? sender, RoutedEventArgs e) => Canvas.AddTail();
}
