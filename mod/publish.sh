#!/bin/sh
# Publish dist/OkoThings-UnlockTree-<version>.zip to Thunderstore (run ./pack.sh first; bump version in
# package/manifest.json, src/Plugin.cs and thunderstore.toml). Token: ~/.config/thunderstore-token (mode 600).
set -e
cd "$(dirname "$0")"
VERSION=$(python3 -c "import json; print(json.load(open('package/manifest.json'))['version_number'])")
grep -q "versionNumber = \"$VERSION\"" thunderstore.toml || { echo "thunderstore.toml version != $VERSION"; exit 1; }
dotnet tcli publish --file "dist/OkoThings-UnlockTree-$VERSION.zip" --token "$(cat ~/.config/thunderstore-token)"
