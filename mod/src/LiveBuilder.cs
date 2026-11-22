using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnlockTree
{
    /// Builds the requirement tree from the running game (ObjectDB, ZNetScene, ZoneSystem), following the
    /// same rules as graph.py. Sources that live inside world locations (boss altars, dungeon chests, locked
    /// doors) are not loaded in memory, so those options are taken from the shipped tree.
    public static class LiveBuilder
    {
        static readonly (int bit, string name)[] Biomes = {
            (1, "Meadows"), (8, "BlackForest"), (2, "Swamp"), (4, "Mountain"), (16, "Plains"),
            (256, "Ocean"), (512, "Mistlands"), (32, "AshLands"), (64, "DeepNorth") };
        static IEnumerable<string> Bits(int mask) => Biomes.Where(b => (mask & b.bit) != 0).Select(b => b.name);

        static JObject H(string t, params (string k, object v)[] kv)
        {
            var o = new JObject { ["t"] = t };
            foreach (var (k, v) in kv)
                if (v != null) o[k] = v is IEnumerable<string> l && !(v is string) ? (JToken)new JArray(l) : JToken.FromObject(v);
            return o;
        }
        static string Pretty(string prefab)
        {
            var s = Regex.Replace(prefab ?? "", @"\s*\(\d+\)", "");
            s = Regex.Replace(s, "(_frac|_new|_destructible)", "").Replace('_', ' ');
            s = Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
            s = Regex.Replace(s, @"(?<=[A-Za-z])\d+\b|\b\d+\b", "");
            var words = new List<string>();
            foreach (var w in s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (words.Count == 0 || !string.Equals(words[words.Count - 1], w, StringComparison.OrdinalIgnoreCase)) words.Add(w);
            s = string.Join(" ", words);
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }
        static string Label(string prefab, string token) =>
            !string.IsNullOrEmpty(token) && token.StartsWith("$") ? token : Pretty(prefab);

        public static Tree Build(Tree shipped)
        {
            var t = new Tree { Source = "live" };
            var odb = ObjectDB.instance; var zns = ZNetScene.instance; var zs = ZoneSystem.instance;
            if (odb == null || zns == null || zs == null) throw new Exception("game databases not ready");

            var prefabs = new Dictionary<string, GameObject>();
            foreach (var go in zns.m_prefabs.Concat(zns.m_nonNetViewPrefabs ?? new List<GameObject>()))
                if (go && !prefabs.ContainsKey(go.name)) prefabs[go.name] = go;
            var items = new Dictionary<string, ItemDrop>();
            foreach (var go in odb.m_items)
                if (go && go.GetComponent<ItemDrop>() is ItemDrop d && !items.ContainsKey(go.name)) items[go.name] = d;
            IEnumerable<(string name, T c)> All<T>() where T : Component =>
                prefabs.Values.Select(g => (g.name, c: g.GetComponent<T>())).Where(x => x.c != null);

            Node Item(string p)
            {
                if (p == null || !items.TryGetValue(p, out var d)) return null;
                var sh = d.m_itemData.m_shared;
                var n = t.Make(p, "item", sh.m_name);
                n.ItemType = (int)sh.m_itemType; n.Prefab = p;
                return n;
            }

            // ---- biomes ----
            foreach (var (bit, b) in Biomes) t.Make("biome:" + b, "biome", "$biome_" + b.ToLower());

            var vegBiomes = new Dictionary<string, HashSet<string>>();
            void AddBiomes(Dictionary<string, HashSet<string>> map, string key, IEnumerable<string> bs)
            {
                if (!map.TryGetValue(key, out var s)) map[key] = s = new HashSet<string>();
                s.UnionWith(bs);
            }
            foreach (var v in zs.m_vegetation)
                if (v.m_enable && v.m_prefab) AddBiomes(vegBiomes, v.m_prefab.name, Bits((int)v.m_biome));
            // trees/rocks leave logs, stubs and fragments behind: those are found where their parent grows
            var links = new Dictionary<string, HashSet<string>>();
            void Link(string a, GameObject b) { if (b) AddBiomes(links, a, new[] { b.name }); }
            foreach (var (n, c) in All<Destructible>()) Link(n, c.m_spawnWhenDestroyed);
            foreach (var (n, c) in All<TreeBase>()) { Link(n, c.m_logPrefab); Link(n, c.m_stubPrefab); }
            foreach (var (n, c) in All<TreeLog>()) Link(n, c.m_subLogPrefab);
            for (int i = 0; i < 6; i++)
                foreach (var kv in links)
                    if (vegBiomes.TryGetValue(kv.Key, out var bs))
                        foreach (var b in kv.Value.ToList()) AddBiomes(vegBiomes, b, bs.ToList());

            // ---- creatures ----
            var chars = All<Character>().ToDictionary(x => x.name, x => x.c);
            var setter = new Dictionary<string, string>();
            foreach (var kv in chars)
                if (!string.IsNullOrEmpty(kv.Value.m_defeatSetGlobalKey) && !setter.ContainsKey(kv.Value.m_defeatSetGlobalKey))
                    setter[kv.Value.m_defeatSetGlobalKey] = "mob:" + kv.Key;
            for (int i = 1; i <= 3; i++)
                if (setter.TryGetValue("BossHildir" + i, out var s)) setter["Hildir" + i] = s;
            List<string> GKey(string k) => !string.IsNullOrEmpty(k) && setter.TryGetValue(k, out var m) ? new List<string> { m } : new List<string>();

            var spawnOpts = new Dictionary<string, List<(string biome, string key)>>();
            var lists = new List<SpawnSystemList>();
            foreach (var (_, c) in All<SpawnSystem>()) lists.AddRange(c.m_spawnLists.Where(l => l));
            lists.AddRange(Resources.FindObjectsOfTypeAll<SpawnSystemList>());
            foreach (var sl in lists.Distinct())
                foreach (var sp in sl.m_spawners)
                {
                    int mask = (int)sp.m_biome;
                    if (!sp.m_enabled || !sp.m_prefab || mask == -1 || mask == 0 || mask >= 0xFFFF) continue;
                    foreach (var b in Bits(mask))
                    {
                        if (!spawnOpts.TryGetValue(sp.m_prefab.name, out var l)) spawnOpts[sp.m_prefab.name] = l = new List<(string, string)>();
                        if (!l.Contains(("biome:" + b, sp.m_requiredGlobalKey ?? ""))) l.Add(("biome:" + b, sp.m_requiredGlobalKey ?? ""));
                    }
                }
            // creature spawners / spawn areas placed directly by world generation
            var placedOpts = new Dictionary<string, List<(List<string> req, JObject how)>>();
            void Placed(string mob, string spawnerPrefab, string gkey)
            {
                if (!vegBiomes.TryGetValue(spawnerPrefab, out var bs)) return;
                foreach (var b in bs)
                {
                    var req = new List<string> { "biome:" + b }; req.AddRange(GKey(gkey));
                    if (!placedOpts.TryGetValue(mob, out var l)) placedOpts[mob] = l = new List<(List<string>, JObject)>();
                    l.Add((req, H("spawn", ("src", Label(spawnerPrefab, null)), ("after", GKey(gkey)))));
                }
            }
            foreach (var (n, c) in All<CreatureSpawner>()) if (c.m_creaturePrefab) Placed(c.m_creaturePrefab.name, n, c.m_requiredGlobalKey);
            foreach (var (n, c) in All<SpawnArea>()) foreach (var e in c.m_prefabs) if (e.m_prefab) Placed(e.m_prefab.name, n, null);

            Node Creature(string p)
            {
                if (!chars.TryGetValue(p, out var c)) return null;
                var n = t.Make("mob:" + p, c.m_boss ? "boss" : "mob", c.m_name);
                n.Prefab = p;
                if (n.Options.Count > 0 || n.Level == -1) return n;
                n.Level = -1;  // marks "options filled"
                if (spawnOpts.TryGetValue(p, out var so))
                    foreach (var (b, k) in so)
                    {
                        var req = new List<string> { b }; req.AddRange(GKey(k));
                        n.Add(req.OrderBy(x => x), H("roam", ("after", GKey(k))));
                    }
                if (!c.m_boss && placedOpts.TryGetValue(p, out var po))
                    foreach (var (req, how) in po) n.Add(req.OrderBy(x => x), how);
                return n;
            }
            var mobDrops = new Dictionary<string, List<string>>();
            foreach (var (p, cd) in All<CharacterDrop>())
            {
                if (Creature(p) == null) continue;
                mobDrops[p] = cd.m_drops.Where(d => d.m_prefab).Select(d => d.m_prefab.name).ToList();
                foreach (var dr in cd.m_drops)
                    if (dr.m_prefab && dr.m_chance > 0 && Item(dr.m_prefab.name) is Node it)
                        it.Add(new[] { "mob:" + p }, H("drop", ("chance", Math.Round(dr.m_chance * 100, 1)),
                            ("lo", dr.m_amountMin), ("hi", dr.m_amountMax)));
            }
            t.MobDrops = mobDrops;

            // ---- tools by tier ----
            var craftable = new HashSet<string>(odb.m_recipes.Where(r => r && r.m_enabled && r.m_item).Select(r => r.m_item.name));
            var tools = new Dictionary<string, Dictionary<string, int>> { ["pickaxe"] = new Dictionary<string, int>(), ["chop"] = new Dictionary<string, int>() };
            foreach (var kv in items)
            {
                if (!craftable.Contains(kv.Key)) continue;
                var sh = kv.Value.m_itemData.m_shared; var type = (int)sh.m_itemType;
                if (type != 9 && type != 23)
                {
                    if (sh.m_damages.m_pickaxe > 0) tools["pickaxe"][kv.Key] = sh.m_toolTier;
                    if (sh.m_damages.m_chop > 0) tools["chop"][kv.Key] = sh.m_toolTier;
                }
                if (kv.Key.Contains("Bomb") && sh.m_toolTier > 3) tools["pickaxe"][kv.Key] = sh.m_toolTier;  // blasts break rock
            }
            string Tool(string kind, int tier)
            {
                var id = $"tool:{kind}@{tier}";
                if (!t.Nodes.ContainsKey(id))
                {
                    var n = t.Make(id, "tool", null); n.Tool = kind; n.Tier = tier;
                    foreach (var kv in tools[kind]) if (kv.Value >= tier && Item(kv.Key) != null) n.Add(new[] { kv.Key }, H("tool", ("tier", kv.Value)));
                }
                return id;
            }

            // ---- rocks, trees, ore, breakables ----
            bool Immune(HitData.DamageModifier m) => m == HitData.DamageModifier.Immune || m == HitData.DamageModifier.Ignore;
            bool Only(HitData.DamageModifiers m, string k)
            {
                var all = new Dictionary<string, HitData.DamageModifier> {
                    ["blunt"] = m.m_blunt, ["slash"] = m.m_slash, ["pierce"] = m.m_pierce, ["chop"] = m.m_chop, ["pickaxe"] = m.m_pickaxe };
                return all.Where(x => x.Key != k).All(x => Immune(x.Value));
            }
            List<string> Drops(DropTable dt) => dt?.m_drops?.Where(d => d.m_item).Select(d => d.m_item.name).ToList() ?? new List<string>();
            foreach (var go in prefabs.Values)
            {
                int tier; HitData.DamageModifiers? mods = null; bool tree = false; string token = null;
                var drops = new List<string>();
                if (go.GetComponent<MineRock>() is MineRock mr) { tier = mr.m_minToolTier; mods = mr.m_damageModifiers; drops.AddRange(Drops(mr.m_dropItems)); token = mr.m_name; }
                else if (go.GetComponent<MineRock5>() is MineRock5 m5) { tier = m5.m_minToolTier; mods = m5.m_damageModifiers; drops.AddRange(Drops(m5.m_dropItems)); token = m5.m_name; }
                else if (go.GetComponent<TreeBase>() is TreeBase tb) { tier = tb.m_minToolTier; mods = tb.m_damageModifiers; tree = true; drops.AddRange(Drops(tb.m_dropWhenDestroyed)); }
                else if (go.GetComponent<TreeLog>() is TreeLog tl) { tier = tl.m_minToolTier; mods = tl.m_damages; tree = true; drops.AddRange(Drops(tl.m_dropWhenDestroyed)); }
                else if (go.GetComponent<Destructible>() is Destructible de)
                {
                    tier = de.m_minToolTier; mods = de.m_damages;
                    if (de.m_spawnWhenDestroyed && items.ContainsKey(de.m_spawnWhenDestroyed.name)) drops.Add(de.m_spawnWhenDestroyed.name);
                }
                else continue;
                if (go.GetComponent<DropOnDestroyed>() is DropOnDestroyed dd) drops.AddRange(Drops(dd.m_dropWhenDestroyed));
                var its = drops.Distinct().Where(x => Item(x) != null).ToList();
                if (its.Count == 0 || !vegBiomes.TryGetValue(go.name, out var bs)) continue;
                List<string> req; string verb;
                if (tree || (mods.HasValue && Only(mods.Value, "chop"))) { req = new List<string> { Tool("chop", tier) }; verb = "chop"; }
                else if ((mods.HasValue && Only(mods.Value, "pickaxe")) || tier > 0) { req = new List<string> { Tool("pickaxe", tier) }; verb = "mine"; }
                else { req = new List<string>(); verb = "break"; }
                var how = H(verb, ("src", Label(go.name, token)));
                foreach (var it in its) foreach (var b in bs) t.Nodes[it].Add(req.Concat(new[] { "biome:" + b }), how);
            }

            // ---- pickables ----
            foreach (var (p, pk) in All<Pickable>())
            {
                if (!vegBiomes.TryGetValue(p, out var bs)) continue;
                var outs = new List<string>();
                if (pk.m_itemPrefab) outs.Add(pk.m_itemPrefab.name);
                outs.AddRange(Drops(pk.m_extraDrops));
                foreach (var it in outs.Distinct())
                    if (Item(it) != null)
                        foreach (var b in bs) t.Nodes[it].Add(new[] { "biome:" + b }, H("pick", ("src", Label(p, pk.m_overrideName))));
            }

            // ---- pieces & stations ----
            var toolFor = new Dictionary<string, string>();
            foreach (var kv in items)
            {
                var table = kv.Value.m_itemData.m_shared.m_buildPieces;
                if (table) foreach (var pc in table.m_pieces) if (pc && !toolFor.ContainsKey(pc.name)) toolFor[pc.name] = kv.Key;
            }
            var extOf = new Dictionary<string, List<string>>();
            foreach (var (p, ex) in All<StationExtension>())
                if (ex.m_craftingStation)
                {
                    if (!extOf.TryGetValue(ex.m_craftingStation.name, out var l)) extOf[ex.m_craftingStation.name] = l = new List<string>();
                    l.Add(p);
                }
            Node Piece(string p)
            {
                if (p == null || !prefabs.TryGetValue(p, out var go) || !toolFor.ContainsKey(p)) return null;
                var pc = go.GetComponent<Piece>(); if (!pc) return null;
                var isStation = go.GetComponent<CraftingStation>() != null;
                bool fresh = !t.Nodes.ContainsKey(p);
                var n = t.Make(p, isStation ? "station" : "piece", pc.m_name); n.Prefab = p;
                if (fresh)
                {
                    var res = pc.m_resources.Where(r => r.m_resItem && Item(r.m_resItem.name) != null).ToList();
                    var req = res.Select(r => r.m_resItem.name).ToList();
                    if (pc.m_craftingStation && Station(pc.m_craftingStation.name, 1) is string s) req.Add(s);
                    if (Item(toolFor[p]) != null) req.Add(toolFor[p]);
                    n.Add(req, H("build", ("tool", toolFor[p]), ("near", pc.m_craftingStation ? pc.m_craftingStation.name : null),
                        ("res", new JArray(res.Select(r => new JArray(r.m_resItem.name, r.m_amount))))));
                }
                return n;
            }
            string Station(string p, int lvl)
            {
                if (lvl <= 1) return Piece(p) != null ? p : null;
                var id = $"{p}@{lvl}";
                if (!t.Nodes.ContainsKey(id))
                {
                    var tok = prefabs.TryGetValue(p, out var go) && go.GetComponent<Piece>() is Piece pc ? pc.m_name : null;
                    var n = t.Make(id, "level", tok); n.Station = p; n.Level = lvl;
                    n.Exts = (extOf.TryGetValue(p, out var l) ? l : new List<string>()).Where(e => Piece(e) != null).ToList();
                }
                return id;
            }

            // ---- conversions, producers, plants, fish, traders ----
            void Convert(string st, GameObject from, GameObject to)
            {
                if (from && to && Item(from.name) != null && Item(to.name) is Node n && Piece(st) != null)
                    n.Add(new[] { from.name, st }, H("convert", ("st", st), ("src", from.name)));
            }
            foreach (var (p, c) in All<Smelter>()) foreach (var cv in c.m_conversion) Convert(p, cv.m_from?.gameObject, cv.m_to?.gameObject);
            foreach (var (p, c) in All<Fermenter>()) foreach (var cv in c.m_conversion) Convert(p, cv.m_from?.gameObject, cv.m_to?.gameObject);
            foreach (var (p, c) in All<CookingStation>()) foreach (var cv in c.m_conversion) Convert(p, cv.m_from?.gameObject, cv.m_to?.gameObject);
            foreach (var (p, c) in All<SapCollector>())
                if (c.m_spawnItem && Item(c.m_spawnItem.name) is Node n && Piece(p) != null) n.Add(new[] { p }, H("produce", ("st", p)));
            foreach (var (p, c) in All<Beehive>())
                if (c.m_honeyItem && Item(c.m_honeyItem.name) is Node n && Piece(p) != null) n.Add(new[] { p }, H("produce", ("st", p)));
            string PickItem(GameObject g) => g && g.GetComponent<Pickable>() is Pickable pk && pk.m_itemPrefab ? pk.m_itemPrefab.name : null;
            foreach (var (p, c) in All<Plant>())
            {
                if (Piece(p) == null) continue;
                foreach (var g in c.m_grownPrefabs ?? new GameObject[0])
                    if (Item(PickItem(g)) is Node n) n.Add(new[] { p }, H("grow", ("st", p)));
            }
            foreach (var (p, c) in All<WispSpawner>())
                if (Item(PickItem(c.m_wispPrefab)) is Node n && Piece(p) != null) n.Add(new[] { p }, H("lure", ("st", p)));
            foreach (var kv in items)
            {
                var fish = kv.Value.GetComponent<Fish>(); if (!fish || Item(kv.Key) == null) continue;
                var bait = fish.m_baits.FirstOrDefault(b => b.m_bait && Item(b.m_bait.name) != null);
                if (bait != null)
                    t.Nodes[kv.Key].Add(Item("FishingRod") != null ? new[] { "FishingRod", bait.m_bait.name } : new[] { bait.m_bait.name },
                        H("fish", ("bait", bait.m_bait.name)));
            }
            foreach (var (p, tr) in All<Trader>())
                foreach (var ti in tr.m_items)
                    if (ti.m_prefab && Item(ti.m_prefab.name) is Node n && Item("Coins") != null)
                    {
                        var k = GKey(ti.m_requiredGlobalKey);
                        n.Add(new[] { "Coins" }.Concat(k), H("buy", ("src", Label(p, tr.m_name)), ("price", ti.m_price), ("after", k)));
                    }

            // ---- recipes ----
            foreach (var r in odb.m_recipes)
            {
                if (!r || !r.m_enabled || !r.m_item || Item(r.m_item.name) == null) continue;
                var res = r.m_resources.Where(x => x.m_resItem && !x.m_upgraderResource && Item(x.m_resItem.name) != null).ToList();
                var st = r.m_craftingStation ? Station(r.m_craftingStation.name, Math.Max(1, r.m_minStationLevel)) : null;
                var node = t.Nodes[r.m_item.name];
                JObject How(IEnumerable<Piece.Requirement> rs) => H("craft", ("st", r.m_craftingStation ? r.m_craftingStation.name : null),
                    ("level", r.m_minStationLevel), ("amount", r.m_amount), ("res", new JArray(rs.Select(x => new JArray(x.m_resItem.name, x.m_amount)))));
                if (r.m_requireOnlyOneIngredient)
                {
                    foreach (var x in res) node.Add(new[] { x.m_resItem.name }.Concat(st != null ? new[] { st } : new string[0]), How(new[] { x }));
                    continue;
                }
                var req = res.Select(x => x.m_resItem.name).ToList(); if (st != null) req.Add(st);
                node.Add(req, How(res));
            }

            // ---- from the shipped tree: what only world locations know ----
            // boss altars (summon), the Deep North gate, biome order, and items found inside locations/dungeons
            int merged = 0;
            foreach (var sn in shipped.Nodes.Values)
                foreach (var o in sn.Options)
                {
                    bool locOnly = o.How.Any(h => h["loc"] != null) || o.How.Any(h => new[] { "summon", "offer", "story", "start" }.Contains((string)h["t"]));
                    if (!locOnly) continue;
                    var n = t.Get(sn.Id);
                    if (n == null)
                    {
                        if (sn.Kind == "gate") n = t.Make(sn.Id, "gate", sn.Token);
                        else if (sn.Kind == "item") n = Item(sn.Id);
                        else if (sn.Kind == "mob" || sn.Kind == "boss") n = Creature(sn.Prefab ?? sn.Id.Substring(4));
                        if (n == null) continue;
                        n.En = sn.En; n.Fr = sn.Fr;
                    }
                    foreach (var r in o.Req)
                    {
                        if (t.Nodes.ContainsKey(r)) continue;
                        var s = shipped.Get(r);
                        if (s?.Kind == "item") Item(r); else if (s?.Kind == "mob" || s?.Kind == "boss") Creature(s.Prefab ?? r.Substring(4));
                    }
                    if (!o.Req.All(r => t.Nodes.ContainsKey(r))) continue;
                    foreach (var h in o.How.DefaultIfEmpty(null)) n.Add(o.Req, h);
                    merged++;
                }
            // labels for nodes the game has no name for
            foreach (var n in t.Nodes.Values)
                if (shipped.Get(n.Id) is Node sn) { n.En = n.En ?? sn.En; n.Fr = n.Fr ?? sn.Fr; }
            Plugin.Log.LogInfo($"live tree: {t.Nodes.Count} nodes, {merged} location options from the shipped tree");
            return t;
        }
    }
}
