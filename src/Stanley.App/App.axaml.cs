using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Stanley.App.Diagnostics;

namespace Stanley.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // A bug on the UI thread: log it and snapshot the open comic for crash recovery,
            // then let it crash - carrying on in an unknown state risks corrupting the save.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
                try
                {
                    window.ViewModel.WriteRecoverySnapshot();
                    AppLog.Info("Wrote a crash-recovery snapshot before exiting.");
                }
                catch (Exception snapshotError)
                {
                    AppLog.Error("Couldn't write a crash-recovery snapshot.", snapshotError);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
