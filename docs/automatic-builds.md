# Getting and installing Stanley

Linux only. Repo is private — you need collaborator access.

## Get it

- Latest tested build: *Releases › nightly*
- Latest stable release: *Releases* › newest `vX.Y.Z`

One file per release: `Stanley*.AppImage`.

## Install

```sh
chmod +x Stanley*.AppImage
./Stanley*.AppImage
```

`stanley --version` shows the build.

No `libfuse2`? You'll see `Error: No suitable fusermount binary found on the
$PATH` — ignore it, the AppImage still runs. To silence it:
`sudo apt install libfuse2t64` (or `libfuse2`).

## Turn on self-update

File › Options › Updates:

1. GitHub → Settings → Developer settings → Personal access tokens →
   Fine-grained tokens → this repo only → **Contents: Read-only**.
2. Paste the token into **GitHub token**.
3. Pick a channel: **Stable** or **Nightly**.
4. Turn on **Check for updates automatically**, or press **Check now**.
   **Install and restart** applies an update once found.

Only works when launched from the `.AppImage`.
