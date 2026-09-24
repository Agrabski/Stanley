# Auto-update from nightly builds

Status as of 2026-09-24: the **app side is implemented** (File › Options ›
Updates, backed by Velopack). **CI doesn't produce anything for it to
download yet** — see "What's left" below, which is a retention-model
decision, not just more code.

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

## What's left: CI has to actually publish something to update to

None of the above has anything to check *against* yet. Today's `publish` /
`nightly-release` / `release-assets` jobs in `.github/workflows/ci.yml`
produce a plain `dotnet publish` folder as `tar.gz`/`zip` - not a Velopack
release feed. Wiring in `vpk pack` + `vpk upload github` isn't just more
CI steps; it runs into a real conflict with how the nightly job works today,
worth the project owner's call before it's built:

- **Velopack's GitHub feed is meant to accumulate, not be replaced.** Delta
  updates work by diffing against whatever earlier versions are still in
  the release's assets, so `vpk upload github --merge` is designed to add
  to a channel's history over time, across many CI runs.
- **The current nightly job does the opposite on purpose**: `nightly-release`
  in `ci.yml` explicitly deletes the previous `nightly` release and its tag
  every run ("Replace the rolling nightly pre-release") so the download
  page always shows exactly one, current build. Doing that to a Velopack
  channel would delete its delta history every single night, so every
  "nightly update" would silently fall back to a full download - most of
  the point of Velopack, gone, without it ever being obvious from the CI
  logs.

Two honest ways to resolve this, worth deciding rather than picking
silently:

1. **Split the tags.** Keep today's `nightly` release exactly as-is (the
   human "grab the latest build" download, wiped and replaced each run) and
   give Velopack's own channel packages a separate, never-deleted tag (e.g.
   `nightly-vpk`) that only `vpk upload github --merge` touches. Two release
   entries under *Releases* instead of one; a bit more to explain in
   `docs/automatic-builds.md`.
2. **Let Velopack own the nightly release outright**, retire the manual
   `tar.gz`/`zip` archives, and prune old assets from it on a schedule (or
   accept the storage growth - packages are small, and GitHub Releases has
   no published per-repo cap). Simpler infra, but changes what "download
   the nightly" means for someone not using auto-update at all.

Either is buildable; this doc stops short of choosing because it changes
the release process people already rely on, not just the app.

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
