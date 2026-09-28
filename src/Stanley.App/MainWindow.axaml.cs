using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Stanley.App.Documents;
using Stanley.App.Updates;
using Stanley.EditorFramework;
using Stanley.Editors;

namespace Stanley.App;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow() : this(null)
    {
    }

    /// <param name="viewModel">Supplied by tests (fake dialogs, in-memory recent list); the app itself uses the real ones.</param>
    public MainWindow(MainWindowViewModel? viewModel)
    {
        InitializeComponent();

        if (viewModel is null)
        {
            var settings = new AppSettings(AppPaths.SettingsFile);
            var tokenStore = new GithubTokenStore(AppPaths.GithubTokenFile);
            viewModel = new MainWindowViewModel(
                new AvaloniaFileDialogs(this),
                new RecentProjects(AppPaths.RecentProjectsFile),
                settings: settings,
                recovery: new RecoveryStore(AppPaths.RecoveryDirectory),
                scheduler: new DispatcherDelayScheduler(),
                tokenStore: tokenStore,
                updates: new VelopackUpdateService(() => tokenStore.Token, () => settings.UpdateChannel),
                launcher: new SystemFileLauncher(this),
                myAssets: new MyAssetsLibrary(settings.MyAssetsDirectory ?? AppPaths.MyAssetsDirectory));
        }
        ViewModel = viewModel;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Workspace))
                ShowWorkspace();
        };
        ShowWorkspace();

        void Bind(Key key, KeyModifiers modifiers, System.Windows.Input.ICommand command, object? parameter = null)
        {
            var binding = new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = command };
            if (parameter != null)
                binding.CommandParameter = parameter;
            KeyBindings.Add(binding);
        }

        Bind(Key.Z, KeyModifiers.Control, ViewModel.UndoCommand);
        Bind(Key.Z, KeyModifiers.Control | KeyModifiers.Shift, ViewModel.RedoCommand);
        Bind(Key.Y, KeyModifiers.Control, ViewModel.RedoCommand);
        Bind(Key.S, KeyModifiers.Control, ViewModel.SaveCommand);
        Bind(Key.S, KeyModifiers.Control | KeyModifiers.Shift, ViewModel.SaveAsCommand);
        Bind(Key.F12, KeyModifiers.None, ViewModel.SaveAsCommand);
        Bind(Key.N, KeyModifiers.Control, ViewModel.NewCommand);
        Bind(Key.O, KeyModifiers.Control, ViewModel.OpenBackstageCommand, BackstagePage.Open);
        Bind(Key.F, KeyModifiers.Alt, ViewModel.OpenBackstageCommand);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Shortcut.RevealWhileCtrlHeld(this); // hold Ctrl: every button shows its shortcut
    }

    public MainWindowViewModel ViewModel { get; }

    /// <summary>The open comic's session: history, pane layout, and which pane the ribbon follows.</summary>
    public EditorWorkspace Workspace => ViewModel.Workspace ?? throw new InvalidOperationException("No comic is open.");

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public PageEditorViewModel Editor => ViewModel.Editor ?? throw new InvalidOperationException("No comic is open.");

    /// <summary>Exposed for headless UI tests, which live in a separate assembly from the generated x:Name fields.</summary>
    public EditorHistory History => Workspace.History;

    /// <summary>Exposed for headless UI tests: the window-level ribbon bar, above the dock area.</summary>
    public Control RibbonBarControl => RibbonBar;

    /// <summary>Exposed for headless UI tests: the File view.</summary>
    public Backstage BackstageControl => BackstageView;

    private void ShowWorkspace()
    {
        var workspace = ViewModel.Workspace;
        EditorDock.Factory = workspace?.Factory;
        EditorDock.Layout = workspace?.Layout;
        EditorDock.IsVisible = workspace != null;
    }

    /// <summary>
    /// Escape leaves the File view. Handled on the way down (tunnel), because the page
    /// canvas under the File view can still have keyboard focus and would otherwise take
    /// Escape for itself (deselect) before the window ever saw it.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsBackstageOpen && ViewModel.HasDocument)
        {
            ViewModel.IsBackstageOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>The window is gone for good: end the session cleanly, so its recovery data isn't mistaken for a crash next time.</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        ViewModel.EndSession();
    }

    /// <summary>Closing the window with unsaved changes asks first, like Word (or just saves, with AutoSave on); Cancel keeps the window open.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && ViewModel.IsDirty)
        {
            e.Cancel = true;
            Dispatcher.UIThread.Post(async () =>
            {
                if (await ViewModel.ConfirmDiscardAsync())
                {
                    _closeConfirmed = true;
                    Close();
                }
            });
        }
        base.OnClosing(e);
    }
}
