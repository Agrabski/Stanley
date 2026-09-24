# Auto-update from nightly builds

Status as of 2026-09-24: implemented end to end - the app (File › Options ›
Updates) and CI (`.github/workflows/ci.yml`) both ship. The Windows and
macOS legs of the CI packaging are **unverified**: this was built and
exercised on a Linux-only environment, so only the `linux-x64` `vpk pack`
step has actually been run. See "How CI publishes it" for what to watch on
the first real nightly/release run on those platforms.

## What's implemented

- **Velopack** (MIT, AGPL-compatible) is wired in: `Program.cs` calls
  `VelopackApp.Build().Run()` first thing, before the existing
  no-args-vs-CLI-args dispatch.
- **Each user supplies their own GitHub personal access token** (File ›
  Options › Updates), not a token baked into the build. Stanley is a private
  repository, so there's no anonymous feed to poll; a shared embedded token
  would mean anyone who obtains a build could extract it and read the
  private source, which defeats the point of the repo being private. A
  per-user token (read access to this repo is enough) has the same blast
  radius as the person already being a collaborator - see "Getting the
  builds" in `docs/automatic-builds.md`. It's stored in its own file
  (`Stanley.App.Updates.GithubTokenStore`, `github-token.txt` under
  `AppPaths.DataDirectory`), owner-only permissions on POSIX, kept separate
  from the plain-text `settings.txt` preferences file so it's never
  casually copied alongside them.
- **Channel picker**: Stable (tagged releases) or Nightly (every change to
  `develop`), `AppSettings.UpdateChannel`. `VelopackUpdateService.ResolveChannel`
  maps it to the channel name a build would be packed under: the OS
  (`win`/`osx`/`linux`) plus `-nightly` for the nightly track, so the two
  can never cross-update into each other.
- **`Stanley.App.Updates.IUpdateService`** is the seam between the real
  `VelopackUpdateService` and `MainWindowViewModel`, so the check/install
  flow is unit-testable without a real Velopack install (`tests/Stanley.App.Tests/UpdatesTests.cs`)
  — a dev build or test host is never "installed" the way a packaged,
  self-updating build is (`UpdateManager.IsInstalled`), so the update
  controls disable themselves gracefully rather than offering a check that
  can never find anything.
- **Applying an update goes through the same Save / Don't Save / Cancel
  gate** as Close (`MainWindowViewModel.ConfirmDiscardAsync`) before
  restarting - never swaps the running build out from under an open,
  unsaved comic.
- An automatic startup check (`AppSettings.AutoCheckForUpdates`, off by
  default - it needs a token configured first, so turning it on is a
  deliberate opt-in) runs a few seconds after the window opens, through the
  same `IDelayScheduler` AutoSave and crash recovery already use.

## How CI publishes it

The `publish` job (matrix, one per RID) now also packs a Velopack release
alongside the existing plain `dotnet publish` output:

- Installs `vpk` (pinned to `1.2.158`, matching the `Velopack` NuGet package
  version `Stanley.App.csproj` references, so the client and the packer
  agree on the package format) and, on Linux, `squashfs-tools` (`vpk`
  packages Linux as an AppImage, which needs `mksquashfs`).
- Computes the channel: the OS (`win`/`osx`/`linux`) plus `-nightly` for a
  schedule/`workflow_dispatch` run, exactly matching
  `VelopackUpdateService.ResolveChannel` on the client side.
- `vpk pack --delta None ...` produces a full package only, no delta patch,
  uploaded as a short-lived (3-day) `velopack-<rid>` build artifact - not a
  release yet, so three parallel matrix jobs never race each other pushing
  to the same GitHub release.
- The **existing plain `tar.gz`/`zip` archives are untouched** (still the
  30-day `stanley-<rid>` artifact, still the manual "download it yourself"
  path `docs/automatic-builds.md` describes) - only the Velopack packaging
  is new.

A single new `velopack-release` job (one runner, so uploads to the same
release never race) then downloads all three platforms' packed output and
uploads them for real:

- **A stable `vX.Y.Z` release is a fresh tag every time** - `vpk upload
  github --merge` just adds that version's three channel packages to the
  release the draft-release flow already published, then the job merges the
  tag back into `develop` (same as before).
- **The `nightly` release/tag is reused every run**, which is exactly the
  case Velopack's GitHub feed is designed for: delta updates work by
  diffing against whatever earlier packages are still attached to the
  release, so the feed is meant to accumulate, not be wiped. The job no
  longer deletes and recreates the `nightly` release the way it used to
  (that would silently erase delta history every night, defeating half of
  Velopack's point); instead it **prunes each channel's previous packages
  first** (`gh release delete-asset`), then uploads the new ones - so the
  release/tag itself is permanent, but doesn't grow without bound. Real
  delta chains (packing against the previous nightly's package instead of
  `--delta None`) are a follow-up, not done here: it needs downloading the
  previous channel's package into the pack step before running `vpk pack`,
  which is more CI plumbing than this pass covers.

## What to verify on the first real run

This was built and tested in a Linux-only sandbox: the full app-side test
suite passes, and `vpk pack` for `linux-x64` was run and inspected by hand
(`Releases/RELEASES-<channel>`, the `.nupkg`, and the `.AppImage` all
produced correctly). The `win-x64` and `osx-arm64` `vpk pack` steps, and
every `vpk upload github` / `gh release delete-asset` call against the real
repository, are **unverified** - watch the first scheduled nightly (or run
it manually via `workflow_dispatch`) for those legs specifically.

## Platform caveats, unchanged since the original investigation

- **Nothing is code-signed.** Windows SmartScreen warns on first run of an
  unsigned Velopack installer; macOS Gatekeeper will quarantine/re-warn on
  an unsigned `.app` on every update unless it's signed *and* notarized
  with an Apple Developer account ($99/yr). Fine for a nightly channel aimed
  at testers who already tolerate warnings; unpleasant for a stable-channel
  default.
- **Linux packaging isn't installer-shaped yet.** Velopack's Linux target
  expects an AppImage; CI currently produces a bare self-contained folder.
  `docs/linux-packaging.md` already recommends adding an AppImage build for
  distribution reasons independent of auto-update - doing that first means
  Linux auto-update falls out of it rather than needing its own change.

## Reference: why Velopack over the alternatives

| Option | License | Platforms | Fit |
|---|---|---|---|
| **[Velopack](https://velopack.io/)** (chosen) | MIT | Windows, macOS, Linux | `GithubSource` reads releases/channels directly from a GitHub repo, ships delta patches, and its CLI (`vpk`) replaces the publish step. Actively maintained successor to Squirrel/Clowd.Squirrel. |
| Clowd.Squirrel / Squirrel.Windows | MIT | Windows only | Rules it out - Stanley ships three platforms. |
| NetSparkle | MIT | Windows, macOS, Linux | Needs its own signed appcast XML feed hosted somewhere; more moving parts than Velopack's direct-from-GitHub-Releases model for no gain here. |
| Roll-your-own (poll the Releases API, download, replace files) | — | All | Reinvents delta packages, atomic replace-while-running, and rollback that Velopack already solves. |
