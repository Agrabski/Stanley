# Auto-update

Linux only. Velopack (MIT). File › Options › Updates.

## App side

- `Program.Main` calls `VelopackApp.Build().Run()` first.
- Per-user GitHub token (`Updates/GithubTokenStore`, own file, owner-only
  perms, separate from `settings.txt`) — repo is private, no anonymous feed.
- `AppSettings.UpdateChannel` (Stable/Nightly) → `VelopackUpdateService.ResolveChannel`
  → `linux` / `linux-nightly`.
- `IUpdateService` seam keeps `MainWindowViewModel` testable without a real
  Velopack install (dev/test builds are never `IsInstalled`).
- Install goes through `ConfirmDiscardAsync` (same Save/Don't Save/Cancel gate
  as Close) before restarting.
- Auto-check on startup: `AppSettings.AutoCheckForUpdates`, off by default.

## CI side

`publish` job: `dotnet publish` → `vpk pack --delta None --channel <channel>`
→ uploaded as the single `stanley-linux-x64` artifact (30 days). No separate
plain archive — a bare `dotnet publish` build never reports `IsInstalled`, so
it can never self-update.

`velopack-release` job: `vpk upload github --merge` onto the release. Stable
(`vX.Y.Z`) is a fresh tag every run, nothing to prune. Nightly reuses the
`nightly` tag/release (needed for Velopack's delta feed) — pruned per-run
(`gh release delete-asset`, matches `*linux-nightly*`, `*.tar.gz`, `*.zip`)
instead of deleted/recreated.

No delta chains yet (`--delta None`) — would need downloading the previous
package before packing.

## Not code-signed

Fine for Linux/AppImage — no SmartScreen/Gatekeeper equivalent.

## Why Velopack

| Option | Fit |
|---|---|
| **Velopack** (chosen) | Reads GitHub releases/channels directly, delta patches, `vpk` CLI. |
| Clowd.Squirrel/Squirrel.Windows | Windows only. |
| NetSparkle | Needs its own hosted signed appcast feed. |
| Roll-your-own | Reinvents delta packages, atomic replace, rollback. |
