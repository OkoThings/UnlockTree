# Unlock Tree — OkoSaga in-game mod (OkoThings-UnlockTree) (BepInEx, Valheim l-1.0.21, r2modman profile "Narnia")

Goal: a Minecraft-style advancement window inside Valheim that shows only what's next:
available nodes, the done step each comes from, and one locked step ahead as silhouettes.
The tree is built live from the running game; the extracted tree (graph.py) ships inside the
mod as a fallback. Nodes can be dragged by hand in an edit mode; positions saved per node id.

Legend: [x] done · [~] written, builds, not yet tested in game · [ ] open

## M1 — Scaffold
- [x] `mod/UnlockTree.csproj` (net461, BepInEx 5 + Harmony, game assemblies by reference; private fields via AccessTools)
- [x] Plugin loads in the Narnia profile, logs, hotkey opens an empty window
- [x] `deploy.sh`: build + copy into the profile's BepInEx/plugins/OkoThings-UnlockTree

## M2 — Shipped tree (fallback)
- [x] `export_mod.py`: graph.py → `unlocktree.json` (nodes, options, hows, era, parent, tokens) embedded in the dll
- [x] C# loader for it

## M3 — Live tree
- [x] Port graph.py rules to C# over ObjectDB / ZNetScene / ZoneSystem / SpawnSystem
      (recipes, pieces, stations+extensions, drops, pickables, rocks/trees + tool tier, conversions,
      plants, fish, traders, boss altars + boss order, global keys)
- [x] Location contents (dungeon chests, keys): use shipped data unless locations are loaded
- [x] Sanity check vs shipped tree (biome chain intact, most shipped nodes reachable) → else fallback, logged

## M4 — Done state
- [x] Player known materials/recipes/stations/biomes/trophies + profile stats (kills per creature, pieces placed)
- [x] World global keys (bosses killed by anyone) — fixes multiplayer holes
- [x] "Done in the world" inference (every way to get a done node needs X)

## M5 — Window
- [x] Frontier selection: available + done parent + locked children (silhouettes)
- [x] Auto layout into a fixed rectangle, game-styled panel, game icons and fonts, EN/FR via game localization
- [x] Hover/click detail: ways to get it, what it unlocks
- [x] Pan / scroll when the frontier is larger than the window

## M6 — Hand layout
- [x] Edit mode toggle: drag nodes, snap to grid, save `BepInEx/config/unlocktree.layout.json`
- [x] Saved positions override auto layout; reset button

## M7 — Package
- [x] Thunderstore zip via `mod/pack.sh` → mod/dist/ (manifest, README EN+FR, CHANGELOG, icon from package/make_icon.py); dependencies: BepInExPack_Valheim + ValheimModding-Jotunn (window input blocking + wood panel)

## Next
- [x] Hot reload dev loop (ScriptEngine, `deploy.sh dev`)
- [x] First in-game test (user): F7 opens, live tree accepted or fallback reason in BepInEx log
- [x] Clickable requirements / unlocks in the detail panel (jump to node)

## Theme pages (user feedback, 2026-11-19)
- [x] Pages: Exploration / Crafting / Building / Combat / Food; a node can be on several
- [x] Full tree drawn left to right; undiscovered nodes are "?" for players (no spoilers in the panel)
- [x] Arrange = dev only (config Dev.DevMode); every node draggable; layout in BepInEx/config, shipped by deploy.sh
- [x] Clickable names in the detail panel (requirements, unlocks) jump to the node / page
- [ ] Hand-arrange all five pages (user, in game)

## Release 0.1.0
- [ ] Arrange the five pages (DevMode) — layout currently only a few moves
- [x] README screenshots (user, 2026-11-22; docs/screenshot.png, shown on Thunderstore via raw GitHub URL)
- [x] Install from r2modman tested (user, 2026-11-23)
- [x] Published 0.1.0 (user, web) and 0.1.1 (`mod/publish.sh`, tcli local tool, token in ~/.config/thunderstore-token)

## Search (2026-11-22)
- [x] Search box in the window header: accent/case-insensitive match on the localized name (+ other language, prefab id), across all pages; undiscovered nodes never match; dropdown of up to 10 hits (icon, state, pages); click or Enter jumps to the node; Esc clears, second Esc closes
- [x] While typing, game hotkeys are suppressed (TextInput.IsVisible / Chat.HasFocus report true)
- [x] Published 0.2.0 (version bump, CHANGELOG, README) — before the in-game test, at the user's request
- [x] In-game test (user, 2026-11-23)
- [x] Website link to https://github.com/OkoThings/UnlockTree + repo README (0.2.1)

## Docs
- [x] Screenshot in English with the search box (user, 2026-11-23)
