using Velopack;
using Velopack.Sources;

namespace Stanley.App.Updates;

/// <summary>
/// Wraps Velopack's <see cref="UpdateManager"/> against this repository's GitHub releases.
/// Stanley is private, so every user supplies their own personal access token (File &gt;
/// Options &gt; Updates) rather than one being baked into the build; the channel picks
/// which packed release track (stable vs. nightly, per OS - see
/// docs/auto-update.md) a check looks at.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/Agrabski/Stanley";

    private static readonly string OsChannel =
        OperatingSystem.IsWindows() ? "win" :
        OperatingSystem.IsMacOS() ? "osx" : "linux";

    private readonly Func<string?> _token;
    private readonly Func<AppUpdateChannel> _channel;
    private UpdateInfo? _pending;

    public VelopackUpdateService(Func<string?> token, Func<AppUpdateChannel> channel)
    {
        _token = token;
        _channel = channel;
    }

    /// <summary>The channel name a build is packed under (`vpk pack --channel`): the OS, plus "-nightly" for
    /// the nightly track, so the two never cross-update into each other.</summary>
    public static string ResolveChannel(AppUpdateChannel channel) =>
        channel == AppUpdateChannel.Nightly ? $"{OsChannel}-nightly" : OsChannel;

    public bool IsInstalled => new UpdateManager().IsInstalled;

    public async Task<AvailableUpdate?> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        _pending = null;
        var manager = CreateManager();
        if (manager is null)
            return null;

        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
            return null;

        _pending = info;
        return new AvailableUpdate(info.TargetFullRelease.Version.ToString(), info.TargetFullRelease.NotesMarkdown);
    }

    public async Task DownloadAndApplyAsync(CancellationToken cancellationToken = default)
    {
        if (_pending is null)
            return;

        var manager = CreateManager();
        if (manager is null)
            return;

        await manager.DownloadUpdatesAsync(_pending, cancelToken: cancellationToken).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }

    /// <summary>Null without a token configured - there's nothing to read this private repo's releases with.</summary>
    private UpdateManager? CreateManager()
    {
        var token = _token();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var channel = _channel();
        var source = new GithubSource(RepoUrl, token, prerelease: channel == AppUpdateChannel.Nightly);
        var options = new UpdateOptions { ExplicitChannel = ResolveChannel(channel) };
        return new UpdateManager(source, options);
    }
}
