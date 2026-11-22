#!/bin/sh
# Build the Thunderstore package: dist/OkoThings-UnlockTree-<version>.zip
# Embeds the fallback tree (export_mod.py) and the hand-made layout from the dev profile.
set -e
cd "$(dirname "$0")"
PROFILE="${PROFILE:-$HOME/.config/r2modmanPlus-local/Valheim/profiles/Narnia}"
VERSION=$(python3 -c "import json; print(json.load(open('package/manifest.json'))['version_number'])")
CODE=$(sed -n 's/.*Version = "\(.*\)";/\1/p' src/Plugin.cs)
[ "$VERSION" = "$CODE" ] || { echo "version mismatch: manifest $VERSION, Plugin.cs $CODE"; exit 1; }
(cd .. && uv run -q export_mod.py mod/unlocktree.json)
[ -f "$PROFILE/BepInEx/config/unlocktree.layout.json" ] && cp "$PROFILE/BepInEx/config/unlocktree.layout.json" unlocktree.layout.json
dotnet build -c Release -nologo -v q
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/plugins"
cp package/manifest.json package/README.md package/CHANGELOG.md package/icon.png "$STAGE/"
cp bin/Release/net472/UnlockTree.dll "$STAGE/plugins/"
mkdir -p dist
OUT="$PWD/dist/OkoThings-UnlockTree-$VERSION.zip"
rm -f "$OUT"
python3 -c "import shutil,sys; shutil.make_archive(sys.argv[1][:-4], 'zip', sys.argv[2])" "$OUT" "$STAGE"
echo "$OUT"
python3 -c "import zipfile,sys; [print(f'{i.file_size:>9}  {i.filename}') for i in zipfile.ZipFile(sys.argv[1]).infolist()]" "$OUT"
