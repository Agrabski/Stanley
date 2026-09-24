# Automatic builds and releases

Stanley builds, versions and releases itself. You never type a version number,
and you never build release files by hand. This page covers the one-time
setup, how to get the builds, and what to do day to day.

## One-time setup (repository owner)

Do these once, in the GitHub repository settings:

1. **Make `develop` the default branch.** Go to *Settings › General › Default
   branch*, pick `develop` and confirm.
   - The daily nightly build only runs from the default branch's workflow.
   - GitHub only honours "Closes #123" in PRs that go into the default branch.
     The release notes and version numbers are built from those links.
2. **Create the `breaking` label.** Go to *Issues › Labels › New label*.
   `bug` and `enhancement` exist by default.
3. **Let the workflow write.** Go to *Settings › Actions › General › Workflow
   permissions*. Either setting works, because the workflow asks for write
   access only in the jobs that need it. If a release or nightly job ever fails
   with `403`, check this setting first.
4. **Optional: auto-merge.** In *Settings › General › Pull Requests*, tick
   *Allow auto-merge*. A PR can then merge by itself once its checks pass.
5. **Optional: protect `develop` and `main`.** In *Settings › Branches*, add
   rules that require the `build-and-test` check. On `develop`, allow GitHub
   Actions to push. Every release merges itself back into `develop`, and that
   step needs the push permission.

## Getting the builds

The repository is private, so you need to be a collaborator (read access is
enough) to download anything.

| You want | Where | How often it changes |
|---|---|---|
| **The latest test build** | *Releases › nightly* | At most once a day (02:00 UTC), and only when `develop` has changed |
| **A stable release** | *Releases* › the newest `vX.Y.Z` | When the owner publishes one |
| **The build of one exact commit** | *Actions* › a run › *Artifacts* | Every nightly and release run; kept for 30 days |

Each release has one file per platform:

- `stanley-<version>-linux-x64.tar.gz`
- `stanley-<version>-win-x64.zip`
- `stanley-<version>-osx-arm64.tar.gz`

Unpack it and run `stanley` (`stanley.exe` on Windows). Nothing needs
installing, because the .NET runtime is included. Run `stanley --version` to
see which build you have.

Stanley doesn't update itself yet. To update, download the file again from the
same place. Self-updating builds (Velopack, a beta channel for testers) are
planned but not built — see [`docs/auto-update.md`](auto-update.md) for what
that needs and why it isn't started yet.

## Day to day

- **Work on a branch and open a PR into `develop`.** Link the issue the PR
  resolves: write "Closes #123" in the description, or use the PR's
  *Development* box. The PR check posts a notice naming the linked issues and
  the version bump they imply.
- **Label issues.** The labels on the closed issues decide the next version
  number:

  | Label on the closed issue | Next version |
  |---|---|
  | `breaking` | major bump (minor while still on 0.x) |
  | `enhancement` or `feature` | minor bump |
  | `bug`, no label, or a PR that closes no issue | patch bump |

  The biggest bump among everything waiting to be released wins. The first
  release is always 0.1.0.
- **Nightly builds are automatic.**
- **To release:**
  1. Open a PR from `develop` into `main` and merge it. CI then keeps a draft
     release `vX.Y.Z` up to date under *Releases*. The draft lists the closed
     issues under New features, Bug fixes, Breaking changes and Other changes.
  2. Edit the notes if you like, then press **Publish release**. CI builds that
     exact version, attaches the three platform files and merges the release
     back into `develop`.
  3. If the merge back fails (a conflict or branch protection), the run shows a
     warning. Merge `main` into `develop` by hand.
- **Urgent fix:** open a PR straight into `main`. It gets counted in the next
  draft release like any other change.

## How the version number is made

The app's version comes from git tags (MinVer), so nothing in the repository
holds a version number:

- A published release `v0.2.0` is exactly `0.2.0`.
- A build after it is `0.2.1-alpha.0.<commits since the tag>`, so nightlies
  always count as newer than the last release and older than the next one.
- Before the first release, builds are `0.1.0-alpha.0.<commits>`.

The details live in `.github/workflows/ci.yml` and
`.github/scripts/release-notes.js`.
