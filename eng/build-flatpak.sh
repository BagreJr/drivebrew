#!/usr/bin/env bash
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
id=com.march.Drivebrew
stage="$(mktemp -d)"
trap 'rm -r -- "$stage"' EXIT

tar -xzf "$repo/artifacts/Drivebrew-Linux-x64.tar.gz" -C "$stage"
flatpak build-init "$stage/build" "$id" org.freedesktop.Platform org.freedesktop.Platform 25.08
files="$stage/build/files"
mkdir -p "$files/bin" "$files/lib" "$files/share/applications" "$files/share/icons/hicolor/scalable/apps"
mv "$stage/Drivebrew" "$files/lib/drivebrew"
ln -s ../lib/drivebrew/Drivebrew "$files/bin/drivebrew"
sed "s/^Icon=.*/Icon=$id/" "$repo/eng/linux/drivebrew.desktop" > "$files/share/applications/$id.desktop"
install -m 644 "$repo/src/DndCompendium.App/Resources/AppIcon/appicon.svg" \
  "$files/share/icons/hicolor/scalable/apps/$id.svg"

# The browser connects to the loopback server and the backend can sync Drive.
# Local files selected by the user remain in the browser's file chooser.
flatpak build-finish --command=drivebrew --share=network "$stage/build"
flatpak build-export "$stage/repo" "$stage/build" stable
flatpak build-bundle "$stage/repo" "$repo/artifacts/Drivebrew-Linux-x86_64.flatpak" "$id" stable
