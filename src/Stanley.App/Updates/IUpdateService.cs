namespace Stanley.App.Updates;

/// <summary>A newer build found on the configured channel.</summary>
public sealed record AvailableUpdate(string Version, string? Notes);

/// <summary>
/// Checking for and applying app updates, behind an interface so <see cref="MainWindowViewModel"/>
/// stays testable without a real Velopack install - a dev build (`dotnet run`) or a test host is
/// never "installed" the way a packaged, self-updating build is.
/// </summary>
public interface IUpdateService
{
    /// <summary>False for a build that isn't a real Velopack install (running from source, a CI archive, `dotnet run`) - there's nothing to update in place, so the UI hides the update controls rather than offering a check that can never find anything installable.</summary>
    bool IsInstalled { get; }

    Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update <see cref="CheckForUpdatesAsync"/> last found (a no-op if none was),
    /// then restarts into it. A real, installed build never returns from this on success.
    /// </summary>
    Task DownloadAndApplyAsync(CancellationToken cancellationToken = default);
}
