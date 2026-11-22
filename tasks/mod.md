# Unlock Tree — OkoSaga in-game mod (OkoThings-UnlockTree) (BepInEx, Valheim l-1.0.21, r2modman profile "Narnia")

Goal: a Minecraft-style advancement window inside Valheim that shows only what's next:
available nodes, the done step each comes from, and one locked step ahead as silhouettes.
The tree is built live from the running game; the extracted tree (graph.py) ships inside the
mod as a fallback. Nodes can be dragged by hand in an edit mode; positions saved per node id.

Legend: [x] done · [~] written, builds, not yet tested in game

## M1 — Scaffold
- [x] `mod/UnlockTree.csproj` (net461, BepInEx 5 + Harmony, game assemblies by reference; private fields via AccessTools)
- [ ] Plugin loads in the Narnia profile, logs, hotkey opens an empty window
- [~] `deploy.sh`: build + copy into the profile's BepInEx/plugins/OkoThings-UnlockTree

## M2 — Shipped tree (fallback)
- [x] `export_mod.py`: graph.py → `unlocktree.json` (nodes, options, hows, era, parent, tokens) embedded in the dll
- [x] C# loader for it

## M3 — Live tree
- [~] Port graph.py rules to C# over ObjectDB / ZNetScene / ZoneSystem / SpawnSystem
      (recipes, pieces, stations+extensions, drops, pickables, rocks/trees + tool tier, conversions,
      plants, fish, traders, boss altars + boss order, global keys)
- [~] Location contents (dungeon chests, keys): use shipped data unless locations are loaded
- [~] Sanity check vs shipped tree (biome chain intact, most shipped nodes reachable) → else fallback, logged

## M4 — Done state
- [~] Player known materials/recipes/stations/biomes/trophies + profile stats (kills per creature, pieces placed)
- [~] World global keys (bosses killed by anyone) — fixes multiplayer holes
- [~] "Done in the world" inference (every way to get a done node needs X)

## M5 — Window
- [~] Frontier selection: available + done parent + locked children (silhouettes)
- [~] Auto layout into a fixed rectangle, game-styled panel, game icons and fonts, EN/FR via game localization
- [~] Hover/click detail: ways to get it, what it unlocks
- [~] Pan / scroll when the frontier is larger than the window

## M6 — Hand layout
- [~] Edit mode toggle: drag nodes, snap to grid, save `BepInEx/config/unlocktree.layout.json`
- [~] Saved positions override auto layout; reset button

## M7 — Package
- [x] Thunderstore zip via `mod/pack.sh` → mod/dist/ (manifest, README EN+FR, CHANGELOG, icon from package/make_icon.py); dependencies: BepInExPack_Valheim + ValheimModding-Jotunn (window input blocking + wood panel)

## Next
- [x] Hot reload dev loop (ScriptEngine, `deploy.sh dev`)
- [ ] First in-game test (user): F7 opens, live tree accepted or fallback reason in BepInEx log
- [ ] Clickable requirements / unlocks in the detail panel (jump to node)

## Theme pages (user feedback, 2026-11-19)
- [~] Pages: Exploration / Crafting / Building / Combat / Food; a node can be on several
- [~] Full tree drawn left to right; undiscovered nodes are "?" for players (no spoilers in the panel)
- [~] Arrange = dev only (config Dev.DevMode); every node draggable; layout in BepInEx/config, shipped by deploy.sh
- [~] Clickable names in the detail panel (requirements, unlocks) jump to the node / page
- [ ] Hand-arrange all five pages (user, in game)

## Release 0.1.0
- [ ] Arrange the five pages (DevMode) — layout currently only a few moves
- [ ] README screenshots (user)
- [ ] Test the zip in a fresh r2modman profile (Import local mod)
- [x] Published 0.1.0 (user, web) and 0.1.1 (`mod/publish.sh`, tcli local tool, token in ~/.config/thunderstore-token)

## Search (2026-11-22)
- [~] Search box in the window header: accent/case-insensitive match on the localized name (+ other language, prefab id), across all pages; undiscovered nodes never match; dropdown of up to 10 hits (icon, state, pages); click or Enter jumps to the node; Esc clears, second Esc closes
- [~] While typing, game hotkeys are suppressed (TextInput.IsVisible / Chat.HasFocus report true)
- [ ] In-game test (user), then bump version + CHANGELOG and publish
