BINARY_DIRECTORY=~/.local/bin
BINARY_FILE="$BINARY_DIRECTORY/stanley"
mkdir -p ~/.local/bin
echo "Downloading latest release"
curl -L -o $BINARY_FILE  https://github.com/Agrabski/Stanley/releases/download/nightly/Stanley-linux-nightly.AppImage

chmod +x "$BINARY_FILE"
echo "Download done, creating shortcut"

mkdir -p ~/.local/share/applications
cat > ~/.local/share/applications/stanley.desktop <<EOF
[Desktop Entry]
Type=Application
Name=Stanley
Comment=Quickly make comics
Exec=$BINARY_FILE
Icon=applications-graphics
Terminal=false
Categories=Graphics;
EOF
update-desktop-database ~/.local/share/applications 2>/dev/null || true

echo "Instalation complete, Stanley is now available from your launch menu"

if ! command -v stanley >/dev/null 2>&1; then
    echo "Stanley is not on PATH. Add ~/.local/bin to your path to use it from the command line"
else
    echo "You can also invoke it from your command line"
fi

