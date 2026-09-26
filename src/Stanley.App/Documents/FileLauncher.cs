using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Stanley.App.Diagnostics;

namespace Stanley.App.Documents;

/// <summary>
/// Handing a file Stanley wrote over to the rest of the computer: opening it with whatever the
/// system opens that kind of file with, or showing it in the file manager. Behind an interface so
/// the export notice (<see cref="MainWindowViewModel.ExportedPath"/>) is testable without
/// launching anything.
/// </summary>
public interface IFileLauncher
{
    /// <summary>Opens <paramref name="path"/> the way double-clicking it would. False if nothing could.</summary>
    Task<bool> OpenAsync(string path);

    /// <summary>Shows the folder <paramref name="path"/> is in - with the file picked out, where the file manager can do that. False if nothing could.</summary>
    Task<bool> ShowInFolderAsync(string path);
}

/// <summary>
/// The real one: Avalonia's launcher to open files and folders, and each system's own way of
/// picking a file out in its file manager - Explorer's <c>/select</c>, Finder's <c>open -R</c>, and
/// on Linux the freedesktop FileManager1 interface most file managers answer (Files, Dolphin,
/// Nemo, Thunar...), falling back to just opening the folder.
/// </summary>
public sealed class SystemFileLauncher(TopLevel owner) : IFileLauncher
{
    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            return await owner.Launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
        catch (Exception e) when (IsLaunchProblem(e))
        {
            AppLog.Error($"Couldn't open {path}.", e);
            return false;
        }
    }

    public async Task<bool> ShowInFolderAsync(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Explorer wants the quotes inside the /select switch, so no ArgumentList here.
                using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
                return explorer is not null;
            }
            if (OperatingSystem.IsMacOS() && await RunAsync("open", "-R", path))
                return true;
            if (OperatingSystem.IsLinux() && await RunAsync("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1",
                    "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", $"array:string:{new Uri(path).AbsoluteUri}", "string:"))
                return true;
            return Path.GetDirectoryName(path) is { } folder && await owner.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
        catch (Exception e) when (IsLaunchProblem(e))
        {
            AppLog.Error($"Couldn't show {path} in its folder.", e);
            return false;
        }
    }

    /// <summary>Runs a helper program to its end (a few seconds at most); whether it said it worked.</summary>
    private static async Task<bool> RunAsync(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, errors);
            return process.ExitCode == 0;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return false; // not installed, or no answer - the caller falls back
        }
    }

    private static bool IsLaunchProblem(Exception e) =>
        e is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or ArgumentException or NotSupportedException or UriFormatException;
}
