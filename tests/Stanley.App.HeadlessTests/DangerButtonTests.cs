using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stanley.App.Documents;

namespace Stanley.App.HeadlessTests;

/// <summary>The red "danger" button (issue #145): the button that confirms something that can't be undone, in the issue-delete dialog and File › My Assets' remove bar.</summary>
[Collection("Page Editor Tests")]
public class DangerButtonTests
{
    private static Color FaceOf(Button button) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).Background).Color;

    private static ContentPresenter Presenter(Button button) =>
        button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");

    private static Point CenterOf(Control control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    /// <summary>Lets the UI thread work until the dialog's answer is in (closing a window finishes a beat after the click), or a few seconds pass.</summary>
    private static bool Answered(Task<bool> asked)
    {
        for (var waited = 0; !asked.IsCompleted && waited < 5000; waited += 10)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        return asked.IsCompleted;
    }

    private static (Window Window, Button Button) ShowDanger()
    {
        var button = new Button { Content = "Delete", Classes = { "danger" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var window = new Window { Width = 300, Height = 200, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, button);
    }

    [Fact]
    public void The_delete_issue_dialog_makes_Delete_red_and_leaves_Cancel_as_the_default()
    {
        var owner = new MainWindow();
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var asked = new AvaloniaFileDialogs(owner).AskDeleteIssueAsync("Issue 2");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        var delete = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string is "Delete");
        var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string is "Cancel");
        LookTabTests.Snapshot(dialog, "delete-issue-dialog");

        Assert.Contains("danger", delete.Classes);
        Assert.DoesNotContain("danger", cancel.Classes);
        Assert.False(delete.IsDefault); // Enter keeps the issue
        Assert.True(cancel.IsDefault);
        Assert.True(cancel.IsCancel);

        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(Answered(asked));
        Assert.True(asked.Result);
    }

    [Fact]
    public void Cancelling_the_delete_issue_dialog_says_no()
    {
        var owner = new MainWindow();
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var asked = new AvaloniaFileDialogs(owner).AskDeleteIssueAsync("Issue 2");
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string is "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(Answered(asked));
        Assert.False(asked.Result);
    }

    [Fact]
    public void The_My_Assets_remove_bar_confirms_with_a_red_Remove_button()
    {
        var window = new MainWindow();
        window.Show();
        var characters = window.ViewModel.Characters!;
        characters.NewCharacterCommand.Execute(null);
        characters.ReturnToPage();
        characters.KeepInMyAssetsCommand.Execute(characters.Items[0]); // something to remove
        window.ViewModel.ShowBackstage(BackstagePage.MyAssets);
        Dispatcher.UIThread.RunJobs();

        var page = window.ViewModel.MyAssetsPage!;
        page.RemoveCommand.Execute(page.Tiles.First());
        Dispatcher.UIThread.RunJobs();

        var remove = window.BackstageControl.FindControl<Button>("ConfirmRemoveAssetButton")!;
        Assert.True(remove.IsEffectivelyVisible);
        Assert.Contains("danger", remove.Classes);
        LookTabTests.Snapshot(window, "backstage-my-assets-remove");
    }

    [Fact]
    public void A_danger_button_is_white_on_red_and_darkens_under_the_pointer_and_when_pressed()
    {
        var (window, button) = ShowDanger();
        var rest = FaceOf(button);
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).Foreground).Color);
        Assert.True(rest.R > 150 && rest.G < 70 && rest.B < 70, $"red at rest, was {rest}");

        var centre = CenterOf(button, window);
        window.MouseMove(centre);
        Dispatcher.UIThread.RunJobs();
        var hover = FaceOf(button);
        Assert.NotEqual(rest, hover);
        Assert.True(hover.R > 150 && hover.G < 80 && hover.B < 80, $"still red under the pointer, was {hover}");

        window.MouseDown(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var pressed = FaceOf(button);
        Assert.NotEqual(hover, pressed);
        Assert.True(pressed.R > 100 && pressed.G < 50 && pressed.B < 50, $"still red while pressed, was {pressed}");
        window.MouseUp(centre, MouseButton.Left);

        window.MouseMove(new Point(2, 2));
        button.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(rest, FaceOf(button)); // greyed out like any disabled button, not a live-looking red
    }

    [Fact]
    public void A_danger_button_stays_white_on_red_in_the_dark_theme()
    {
        var app = Application.Current!;
        try
        {
            var (window, button) = ShowDanger();
            var light = FaceOf(button);

            app.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            var dark = FaceOf(button);
            Assert.NotEqual(light, dark); // its own brush per theme
            Assert.True(dark.R > 150 && dark.G < 80 && dark.B < 80, $"red in the dark theme, was {dark}");
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(Presenter(button).Foreground).Color);
            LookTabTests.Snapshot(window, "danger-button-dark");
        }
        finally
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
