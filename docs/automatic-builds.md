# Getting and installing Stanley

Linux only. Repo is private — you need collaborator access.

## Get it

- Latest tested build: *Releases › nightly*
- Latest stable release: *Releases* › newest `vX.Y.Z`

One file per release: `Stanley*.AppImage`.

## Install

These steps are for Ubuntu and the distributions based on it (Linux Mint, Pop!_OS,
Zorin, elementary OS…). Other distributions work the same way.

### 1. Put the AppImage in `~/Applications`

```sh
mkdir -p ~/Applications
mv ~/Downloads/Stanley*.AppImage ~/Applications/Stanley.AppImage
chmod +x ~/Applications/Stanley.AppImage
```

- **Keep it in your home folder.** Self-update replaces this file where it is. In a
  folder owned by root (`/opt`, `/usr/local/bin`), every update asks for your
  admin password.
- **Use a fixed name**, `Stanley.AppImage`, without the version. The menu entry
  below points at this path, and updates keep the name, so the entry keeps working.
- To try it once without installing: `chmod +x Stanley*.AppImage`, then
  `./Stanley*.AppImage` in the download folder.

### 2. Add it to the start menu

Paste this into a terminal as it is. It adds a menu entry for your user only (no
`sudo`), and the shell fills in your home folder, because menu entries don't
understand `~`:

```sh
mkdir -p ~/.local/share/applications
cat > ~/.local/share/applications/stanley.desktop <<EOF
[Desktop Entry]
Type=Application
Name=Stanley
Comment=Make comics
Exec="$HOME/Applications/Stanley.AppImage"
Icon=applications-graphics
Terminal=false
Categories=Graphics;
EOF
update-desktop-database ~/.local/share/applications 2>/dev/null || true
```

Stanley then shows up:
- **Ubuntu** (GNOME): in *Show Applications* — search "Stanley". To keep it in the
  dock, right-click › *Pin to Dash* (*Add to Favorites* on older versions).
- **Linux Mint** (Cinnamon, MATE, Xfce): in *Menu › Graphics*, or search "Stanley".
  Right-click it to add it to the panel or the desktop.

If it doesn't appear straight away, log out and back in.

`~/Applications/Stanley.AppImage --version` shows the build.

### No `libfuse2`?

You'll see `Error: No suitable fusermount binary found on the $PATH` — ignore it,
the AppImage still runs. To silence it: `sudo apt install libfuse2t64` (Ubuntu
24.04 / Mint 22 and newer) or `sudo apt install libfuse2` (older).

### Uninstall

```sh
rm ~/Applications/Stanley.AppImage ~/.local/share/applications/stanley.desktop
```

Your comics stay wherever you saved them. Stanley's own settings, logs and
crash-recovery snapshots are in `~/.config/Stanley`; delete that folder too to
remove everything.

## Turn on self-update

File › Options › Updates:

1. GitHub → Settings → Developer settings → Personal access tokens →
   Fine-grained tokens → this repo only → **Contents: Read-only**.
2. Paste the token into **GitHub token**.
3. Pick a channel: **Stable** or **Nightly**.
4. Turn on **Check for updates automatically**, or press **Check now**.
   **Install and restart** applies an update once found.

Only works when launched from the `.AppImage` (the menu entry above does that).
