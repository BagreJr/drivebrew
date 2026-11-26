#!/usr/bin/env bash
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
artifacts="$repo/artifacts"
stage="$(mktemp -d)"
trap 'rm -r -- "$stage"' EXIT

mkdir -p "$artifacts" "$stage/Drivebrew"
dotnet publish "$repo/src/DndCompendium.Linux/DndCompendium.Linux.csproj" \
  -c Release -r linux-x64 --self-contained -o "$stage/Drivebrew/App"
install -m 755 "$repo/eng/linux/launch.sh" "$stage/Drivebrew/Drivebrew"
version="$(dotnet msbuild "$repo/src/DndCompendium.Linux/DndCompendium.Linux.csproj" -getProperty:Version)"
tar -C "$stage" -czf "$artifacts/Drivebrew-Linux-x64.tar.gz" Drivebrew

deb="$stage/deb"
mkdir -p "$deb/DEBIAN" "$deb/usr/bin" "$deb/usr/lib" "$deb/usr/share/applications" \
  "$deb/usr/share/icons/hicolor/scalable/apps"
cp -a "$stage/Drivebrew" "$deb/usr/lib/drivebrew"
ln -s ../lib/drivebrew/Drivebrew "$deb/usr/bin/drivebrew"
install -m 644 "$repo/eng/linux/drivebrew.desktop" "$deb/usr/share/applications/drivebrew.desktop"
install -m 644 "$repo/src/DndCompendium.App/Resources/AppIcon/appicon.svg" \
  "$deb/usr/share/icons/hicolor/scalable/apps/drivebrew.svg"
printf '%s\n' 'Package: drivebrew' "Version: $version" 'Architecture: amd64' \
  'Maintainer: Drivebrew <https://github.com/BagreJr/drivebrew>' \
  'Section: games' 'Priority: optional' \
  'Depends: xdg-utils, libc6 (>= 2.35), libgcc-s1, libstdc++6, libgssapi-krb5-2, zlib1g, libssl3 | libssl3t64, libicu70 | libicu72 | libicu74 | libicu76 | libicu77 | libicu78 | libicu79 | libicu80' \
  'Description: Drivebrew, compendio de D&D offline' \
  ' Biblioteca, catálogos y hojas de personaje en el navegador local.' > "$deb/DEBIAN/control"
dpkg-deb --root-owner-group --build "$deb" "$artifacts/Drivebrew-Linux-amd64.deb"
