# Auto-update from nightly builds (investigation, not implemented)

Findings as of 2026-09. `docs/automatic-builds.md` already covers how nightly
builds are produced; this covers what it would take for a running copy of
Stanley to update itself, instead of a person re-downloading the archive.

## Nightly cadence: already correct

The second half of this investigation's brief was to confirm nightlies are
*actually* nightly. `.github/workflows/ci.yml` already does this correctly and
needed no change:

- `schedule: "0 2 * * *"` (02:00 UTC daily) plus `workflow_dispatch`.
- The repository's default branch is `develop` (confirmed via the GitHub API),
  which is required — GitHub only fires a scheduled workflow from the copy of
  the file on the default branch.
- `nightly-gate` compares `develop`'s current commit against the commit the
  `nightly` tag currently points at (i.e. the last successful nightly) and
  sets `build=false` when they match, so a quiet day is skipped. A manual
  `workflow_dispatch` always builds regardless. This is exactly "every day, if
  there are changes since the last one" — no gap to fix.

## What "auto-update" needs

Stanley ships as a self-contained `dotnet publish` output (`tar.gz`/`zip`) per
RID (`linux-x64`, `win-x64`, `osx-arm64`), attached to GitHub Releases: the
rolling `nightly` pre-release and tagged `vX.Y.Z` releases. None of that is an
installer, so there's nothing today for a running instance to invoke to
replace itself. Four things are needed regardless of library choice:

1. **A packaging format with an in-place update primitive** (an installer or
   a self-replacing folder layout) — a bare `tar.gz` of a self-contained
   publish can't be swapped out from under a running process on Windows, and
   has no delta/rollback story anywhere.
2. **A feed the app can query for "is there something newer than me, on my
   channel."** MinVer already gives every build an unambiguous, ordered
   version (`0.2.1-alpha.0.<n>` for nightlies, `0.2.0` for the release it
   follows), so the ordering problem is solved; what's missing is a client
   that reads it.
3. **A channel concept.** Nightly and stable are two different audiences
   (testers who want every change vs. everyone else) and must never
   cross-update into each other by accident.
4. **A safe apply point.** Stanley already has an unsaved-changes prompt
   (Save / Don't Save / Cancel) for New/Open/Close/window-close and a
   crash-recovery snapshot; applying an update mid-edit needs to go through
   the same gate, not just swap files under an open document.

## Library options

| Option | License | Platforms | Fit |
|---|---|---|---|
| **[Velopack](https://velopack.io/)** | MIT | Windows, macOS, Linux | Built for exactly this: a `GithubSource` reads releases/channels directly from a GitHub repo (no separate feed server to run), ships delta patches, and its CLI (`vpk`) replaces the publish step. Actively maintained successor to Squirrel/Clowd.Squirrel. |
| Clowd.Squirrel / Squirrel.Windows | MIT | Windows only (Squirrel.Windows unmaintained) | Windows-only rules it out — Stanley ships three platforms. |
| NetSparkle | MIT | Windows, macOS, Linux | Needs its own signed appcast XML feed hosted somewhere; more moving parts than Velopack's direct-from-GitHub-Releases model for no real gain here. |
| Roll-your-own (poll GitHub Releases API, download, replace files) | — | All | Doable but reinvents delta packages, atomic replace-while-running, and rollback that Velopack already solves; only worth it for the "just tell me there's an update" version below. |

**Recommendation: Velopack**, if/when the blockers below are resolved. MIT is
AGPL-compatible (same check this repo already applies to every dependency,
per `CLAUDE.md`'s licensing constraint), it targets all three RIDs Stanley
already builds, and its channel feature maps directly onto nightly vs. stable
without inventing a parallel mechanism.

## Blockers worth resolving before writing code

These are decisions for the project owner, not implementation details:

- **The repository is private.** Velopack's `GithubSource` (and a plain HTTP
  poll of the Releases API) both need to read release assets; a private
  repo means either shipping a token in every client (a real credential-
  leak risk — anyone with the built app gets a token with whatever scope it
  was granted) or making the repository (or at least its Releases) public.
  This is a visibility call, not a coding one.
- **Nothing is code-signed today.** An unsigned Velopack installer still
  installs and updates, but: Windows SmartScreen warns on first run of an
  unsigned installer, and macOS Gatekeeper will quarantine/re-warn on an
  unsigned `.app` on every update unless it's signed *and* notarized with an
  Apple Developer account. Nightly builds aimed at testers can probably
  live with the Windows warning; macOS auto-update is unpleasant without
  notarization. Budget for a certificate (and Apple Developer Program
  membership, $99/yr) is an owner decision.
- **Linux packaging isn't installer-shaped yet.** Velopack's Linux target
  expects an AppImage; CI currently produces a bare self-contained folder.
  `docs/linux-packaging.md` already recommends adding an AppImage build for
  distribution reasons — doing that first means auto-update on Linux falls
  out of it rather than needing its own packaging change.

## If code lands, roughly this shape

- `Program.Main` calls `VelopackApp.Build().Run(args)` as the very first
  line, before the existing no-args-vs-CLI-args dispatch — Velopack hooks
  install/uninstall/update lifecycle events through its own recognized args,
  and needs first refusal on `args` before `System.CommandLine` sees them.
- Two new `AppSettings` keys, next to `AutoSave`/`Theme` (same
  `key=value`, unknown-keys-preserved store): `AutoCheckForUpdates` (bool,
  on by default for stable, off by default for nightly — testers opt in) and
  `UpdateChannel` (`Stable`/`Nightly` enum, same shape as `AppTheme`).
- A "Updates" section in File › Options, next to Appearance, mirroring its
  layout: current version, a channel picker, a "Check now" button, last
  checked time.
- The actual check runs async off the startup path (never blocks opening a
  document) and, like AutoSave/recovery timers, goes through
  `IDelayScheduler` so tests can control it instead of real time.
- Applying a downloaded update reuses the existing Save/Don't Save/Cancel
  prompt (`MainWindowViewModel`) before calling
  `UpdateManager.ApplyUpdatesAndRestart` — never restarts out from under an
  unsaved comic.
- CI: the `publish` job's `dotnet publish` + tar/zip step is replaced with
  `vpk pack --channel nightly` (or the default channel for tagged releases)
  per RID, and a `vpk upload github` step targets the existing `nightly` /
  `vX.Y.Z` releases these jobs already create — no new hosting.

## Cheaper interim step, if full auto-update stalls on the blockers above

A plain "there's a newer nightly" **notice** needs none of the above: poll
`GET /repos/agrabski/stanley/releases/tags/nightly` (or the newest `vX.Y.Z`)
on startup, compare its tag/commit against the running MinVer version, and
show a dismissible line in the title bar or backstage Info page linking to
the download — no packaging change, no installer, no signing. It still needs
the private-repo answer (an unauthenticated request to that endpoint 404s on
a private repo today), but is otherwise buildable immediately and gets most
of the "ease of use" benefit for testers who'd otherwise not know a nightly
moved.
