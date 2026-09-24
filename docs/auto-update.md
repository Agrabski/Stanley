# Auto-update from nightly builds

Status as of 2026-09-24: implemented end to end - the app (File › Options ›
Updates) and CI (`.github/workflows/ci.yml`) both ship. **Linux only**
(`linux-x64`) - Windows and macOS aren't supported build targets, so there's
nothing platform-specific left to verify beyond the one RID this repo builds.

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
  maps it to the channel name a build would be packed under (`linux` /
  `linux-nightly`), so the two can never cross-update into each other.
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

The `publish` job runs a plain `dotnet publish` for `linux-x64` first (input
to the packing step below, and what the smoke test runs against), then packs
it with Velopack - **that pack is the only thing uploaded anywhere**, as the
`stanley-linux-x64` build artifact (30 days) and, from there, the release
asset. There's no separate plain archive to publish alongside it and no
second artifact for someone to download the wrong one by mistake.

- Installs `vpk` (pinned to `1.2.158`, matching the `Velopack` NuGet package
  version `Stanley.App.csproj` references, so the client and the packer
  agree on the package format) and `squashfs-tools` (`vpk` packages Linux as
  an AppImage, which needs `mksquashfs`).
- Computes the channel: `linux`, or `linux-nightly` for a
  schedule/`workflow_dispatch` run, exactly matching
  `VelopackUpdateService.ResolveChannel` on the client side.
- `vpk pack --delta None ...` produces a full package only, no delta patch,
  uploaded as the `stanley-linux-x64` build artifact - not a release yet, so
  the packaging step and the upload step (see below) stay independent.

A single `velopack-release` job then downloads that packed output and
uploads it for real:

- **A stable `vX.Y.Z` release is a fresh tag every time** - `vpk upload
  github --merge` just adds that version's package to the release the
  draft-release flow already published, then the job merges the tag back
  into `develop` (same as before).
- **The `nightly` release/tag is reused every run**, which is exactly the
  case Velopack's GitHub feed is designed for: delta updates work by
  diffing against whatever earlier packages are still attached to the
  release, so the feed is meant to accumulate, not be wiped. The job no
  longer deletes and recreates the `nightly` release the way it used to
  (that would silently erase delta history every night, defeating half of
  Velopack's point); instead it **prunes the previous nightly package
  first** (`gh release delete-asset`), then uploads the new one - so the
  release/tag itself is permanent, but doesn't grow without bound. This
  step also sweeps up any leftover `.tar.gz`/`.zip`: earlier versions of
  this workflow attached plain archives for three platforms directly to
  releases, before Velopack existed here, and those are stale now - the
  live `nightly` release had exactly this leftover clutter until this
  pruning rule was added. Real delta chains (packing against the previous
  nightly's package instead of `--delta None`) are a follow-up, not done
  here: it needs downloading the previous package into the pack step before
  running `vpk pack`, which is more CI plumbing than this pass covers.

## What's been verified locally, and why only the AppImage ships

The full app-side test suite passes. Beyond that, this was tested end to end
in a Linux sandbox (no real display, but `xvfb-run` stands in for one):

- A plain `dotnet publish` build actually launches the GUI cleanly under
  Xvfb (logs a normal startup, opens a blank comic, no errors) - so the app
  itself works on any machine with the usual desktop X11/GL libraries
  (`libx11-6`, `libice6`, `libsm6`, `libgl1`, …, all standard on any desktop
  Linux distro).
- But `Velopack.UpdateManager.IsInstalled` is **`false`** for that same plain
  build, and `CanCheckForUpdates` requires it - so a build handed out as a
  bare `dotnet publish` folder (which is what the old `tar.gz` archive was)
  can never offer updates, no matter what's configured in Options. This is
  exactly why this workflow no longer produces that archive at all: shipping
  it alongside the AppImage would silently give most users a copy that can
  never self-update, with no indication why.
- Packing the same build with `vpk pack` and running the resulting
  `.AppImage` instead gives `IsInstalled = true` (`LinuxVelopackLocator`
  finds its embedded manifest) - confirmed by instrumenting `Program.Main`
  temporarily and running both builds side by side.
- The AppImage itself ran and logged a clean startup too, but printed `Error:
  No suitable fusermount binary found on the $PATH` first (this sandbox has
  no `libfuse2`) before falling back to extracting and running itself - see
  "Installing" in `docs/automatic-builds.md` for the one-line fix and why
  it's safe to ignore either way.

Every `vpk upload github` / `gh release delete-asset` call against the real
repository is still **unverified** - watch the first scheduled nightly (or
run it manually via `workflow_dispatch`) to confirm the upload and pruning
steps behave as expected against a live GitHub release, and that the
leftover three-platform `.tar.gz`/`.zip` files get cleaned off it.

## Platform caveat

**Nothing is code-signed.** This matters less on Linux than it would on
Windows/macOS (no SmartScreen or Gatekeeper equivalent), but an AppImage
still isn't marked executable by default after download and some desktop
environments warn before running an unrecognised binary. Fine for a nightly
channel aimed at testers.

## Reference: why Velopack over the alternatives

| Option | License | Fit |
|---|---|---|
| **[Velopack](https://velopack.io/)** (chosen) | MIT | `GithubSource` reads releases/channels directly from a GitHub repo, ships delta patches, and its CLI (`vpk`) replaces the publish step. Actively maintained successor to Squirrel/Clowd.Squirrel. Also covers Windows/macOS if support is ever added back. |
| Clowd.Squirrel / Squirrel.Windows | MIT | Windows only - moot here, but also would have ruled it out regardless. |
| NetSparkle | MIT | Needs its own signed appcast XML feed hosted somewhere; more moving parts than Velopack's direct-from-GitHub-Releases model for no gain here. |
| Roll-your-own (poll the Releases API, download, replace files) | — | Reinvents delta packages, atomic replace-while-running, and rollback that Velopack already solves. |
