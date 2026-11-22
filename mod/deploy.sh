#!/bin/sh
# Build Unlock Tree and install it into the r2modman profile (default: Narnia).
#   ./deploy.sh        normal install: BepInEx/plugins (what players get)
#   ./deploy.sh dev    hot reload: BepInEx/scripts, picked up by ScriptEngine (F6 or automatically)
# Only one copy may exist, or the mod would run twice: each mode removes the other.
set -e
cd "$(dirname "$0")"
PROFILE="${PROFILE:-$HOME/.config/r2modmanPlus-local/Valheim/profiles/Narnia}"
PLUGINS="$PROFILE/BepInEx/plugins/OkoThings-UnlockTree"
SCRIPTS="$PROFILE/BepInEx/scripts"
(cd .. && uv run -q export_mod.py mod/unlocktree.json)
# the hand-made layout (edited in game with DevMode) ships inside the mod
[ -f "$PROFILE/BepInEx/config/unlocktree.layout.json" ] && cp "$PROFILE/BepInEx/config/unlocktree.layout.json" unlocktree.layout.json
dotnet build -c Release -nologo -v q
if [ "$1" = dev ]; then
    rm -f "$PLUGINS/UnlockTree.dll"
    mkdir -p "$SCRIPTS"
    # ScriptEngine reads the symbols too (and line numbers make stack traces useful); pdb first so the
    # file watcher reloads once both are in place
    cp bin/Release/net472/UnlockTree.pdb "$SCRIPTS/"
    cp bin/Release/net472/UnlockTree.dll "$SCRIPTS/"
    echo "dev install: $SCRIPTS (press F6 in game, or wait for auto reload)"
else
    rm -f "$SCRIPTS/UnlockTree.dll" "$SCRIPTS/UnlockTree.pdb"
    mkdir -p "$PLUGINS"
    cp bin/Release/net472/UnlockTree.dll "$PLUGINS/"
    echo "installed to $PLUGINS"
fi
