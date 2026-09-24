# Linux packaging options (investigation, not implemented)

Findings as of 2026-09. Items marked *(unverified)* came from memory or search
snippets rather than the official page.

## What GitHub itself offers

- **GitHub Packages has no apt/deb, rpm, Flatpak or Snap registry.** It supports npm,
  RubyGems, Maven, Gradle, NuGet, Docker and the Container registry (GHCR/OCI) only.
- **GitHub Releases** can hold the built files (`.tar.gz`, AppImage, `.deb`, `.rpm`,
  `.flatpak` bundle), built by an Actions workflow on each tag. Each asset must be under
  2 GiB *(unverified)*. This isn't a package manager on its own, but the other channels
  below reuse these files.
- **GitHub Pages as a self-hosted apt/dnf repo:** Actions builds the packages, then
  builds the repo metadata (`reprepro`/`aptly`/`dpkg-scanpackages` for apt,
  `createrepo_c` for dnf). It signs the metadata with a GPG key kept in Secrets and
  pushes to `gh-pages`. Users add the key and a `deb [signed-by=…]` line or a `.repo`
  file. Limits: 1 GB site, a soft limit of 100 GB/month of bandwidth, and a 100 MB
  per-file cap. A self-contained Avalonia build is about 70–100 MB, so old versions have
  to be pruned. Keeping the signing key safe is the ongoing cost.

## Package-manager channels

| Channel | How | Effort | Reach |
|---|---|---|---|
| **Flathub** (Flatpak) | Submit a manifest by PR to `flathub/flathub`. Flathub then builds from source, offline, on its own infrastructure. Needs `org.freedesktop.Sdk.Extension.dotnet10`, `nuget-sources.json` from `flatpak-dotnet-generator` (regenerated whenever NuGet dependencies change), a `.desktop` file, AppStream metainfo and an icon. App id `io.github.agrabski.Stanley`. | Moderate | Highest. Built into Fedora, Mint, elementary and Steam Deck. |
| AppImage on Releases | `appimagetool`/`linuxdeploy` in Actions, with zsync update info (`gh-releases-zsync\|agrabski\|stanley\|latest\|…`). Build on an old glibc base. Users install it with Gear Lever or AppImageLauncher. | Low | Any distro, no package manager needed |
| `.deb` / `.rpm` on Releases | [nfpm](https://nfpm.goreleaser.com/) from the `dotnet publish` output | Low | Download and install by hand |
| AUR `stanley-bin` | A `PKGBUILD` that downloads the Release tarball | Under an hour | Arch |
| Homebrew tap | `agrabski/homebrew-stanley` repo with a formula, updated from Actions | Under an hour | Linuxbrew and macOS |
| Fedora COPR | `.spec` file, triggered from GitHub by Packit or a webhook. Builders can have network access (NuGet restore). Users run `dnf copr enable agrabski/stanley`. | Low–moderate | Fedora, EPEL |
| Cloudsmith OSS / packagecloud | Hosted, signed apt/rpm repos with a free open-source tier *(unverified: the terms change)* | Low–moderate | `apt install` without hosting it yourself |
| Snap Store | `snapcore/action-build` + `action-publish`. The dotnet extension is experimental, so dumping a self-contained build is safer. | Moderate | Mostly Ubuntu, overlaps with Flathub |
| Ubuntu PPA / openSUSE OBS | Build from source with no network, so all NuGet packages have to be vendored | High | Skip for now |

## .NET / Avalonia notes

- `dotnet publish -r linux-x64` (and `linux-arm64`, which runs on the `ubuntu-24.04-arm` runners) with `--self-contained`.
- Keep `SkiaSharp.NativeAssets.Linux`. HarfBuzzSharp's native assets come in through Avalonia.
- Package dependencies: `libfontconfig1 libx11-6 libice6 libsm6 libxrandr2 libxi6 libxcursor1 libgl1`, plus `libicu` unless `InvariantGlobalization=true`.
- Flathub, COPR and similar must build from tagged source. AGPL is fine on all of them.

## Recommendation

1. One tag-triggered Actions workflow that attaches a `tar.gz`, an AppImage (with zsync), a `.deb` and an `.rpm` to the GitHub Release.
2. Flathub: most desktop Linux users get apps there, and it's hosted and built for free.
3. AUR `-bin` and a Homebrew tap, both fed from the Release.
4. COPR for `dnf`.
5. A signed apt repo (Cloudsmith, or Pages with `reprepro`) only if people ask for it.
