using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace UnlockTree
{
    public enum State { Done, Available, Locked }

    /// What the local player (and their world) has already done, and what that makes available.
    public class Progress
    {
        public readonly Dictionary<string, bool> Done = new Dictionary<string, bool>();
        public readonly HashSet<string> Implied = new HashSet<string>();   // done by someone in the world
        public readonly Dictionary<string, State> St = new Dictionary<string, State>();
        public int DoneCount, Total;

        static T F<T>(object o, string field) => (T)AccessTools.Field(o.GetType(), field).GetValue(o);

        public static Progress Compute(Tree t)
        {
            var p = new Progress();
            var pl = Player.m_localPlayer; if (!pl) return p;
            var mats = F<HashSet<string>>(pl, "m_knownMaterial");
            var recs = F<HashSet<string>>(pl, "m_knownRecipes");
            var stations = F<Dictionary<string, int>>(pl, "m_knownStations");
            var biomes = new HashSet<string>(F<HashSet<string>>(pl, "m_knownBiome"));   // biome names, e.g. "BlackForest"
            var trophies = F<HashSet<string>>(pl, "m_trophies");

            // per-player statistics (kills by creature, items picked up / crafted, pieces placed)
            var kills = new HashSet<string>(); var touched = new HashSet<string>(); var placed = new HashSet<string>();
            try
            {
                foreach (var s in Game.instance.GetPlayerProfile().m_playerStats ?? new PlayerProfile.PlayerStats[0])
                {
                    if (s == null) continue;
                    foreach (var d in s.m_enemyStats ?? new Dictionary<string, float>[0]) if (d != null) kills.UnionWith(d.Where(x => x.Value > 0).Select(x => x.Key));
                    if (s.m_itemPickupStats != null) touched.UnionWith(s.m_itemPickupStats.Keys);
                    if (s.m_itemCraftStats != null) touched.UnionWith(s.m_itemCraftStats.Keys);
                    if (s.m_piecesPlacedStats != null) placed.UnionWith(s.m_piecesPlacedStats.Keys);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("player stats unavailable: " + e.Message); }
            bool World(string key) => !string.IsNullOrEmpty(key) && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);

            // which drops identify a creature (its trophy, or loot only one or two creatures give)
            var dropSources = new Dictionary<string, int>(); var mobDrops = new Dictionary<string, List<string>>();
            foreach (var go in ZNetScene.instance.m_prefabs)
                if (go && go.GetComponent<CharacterDrop>() is CharacterDrop cd)
                {
                    var ds = cd.m_drops.Where(d => d.m_prefab).Select(d => d.m_prefab.name).Distinct().ToList();
                    mobDrops[go.name] = ds;
                    foreach (var d in ds) dropSources[d] = dropSources.TryGetValue(d, out var c) ? c + 1 : 1;
                }
            string Tok(string prefab) { var g = ObjectDB.instance.GetItemPrefab(prefab); return g ? g.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : null; }

            foreach (var n in t.Nodes.Values.Where(x => x.Reachable))
            {
                bool v = false;
                switch (n.Kind)
                {
                    case "item": v = mats.Contains(n.Token ?? "") || touched.Contains(n.Id); break;
                    case "piece": v = recs.Contains(n.Token ?? "") || placed.Contains(n.Id); break;
                    case "station": v = (stations.TryGetValue(n.Token ?? "", out var l1) && l1 >= 1) || recs.Contains(n.Token ?? "") || placed.Contains(n.Id); break;
                    case "level": v = stations.TryGetValue(n.Token ?? "", out var l) && l >= n.Level; break;
                    case "biome": v = biomes.Contains(n.Id.Substring(6)); break;
                    case "tool": v = n.Options.Any(o => o.Req.Count > 0 && mats.Contains(Tok(o.Req[0]) ?? "-")); break;
                    case "mob":
                    case "boss":
                        var c = ZNetScene.instance.GetPrefab(n.Prefab)?.GetComponent<Character>();
                        v = kills.Contains(n.Prefab) || (c && (kills.Contains(c.m_name) || World(c.m_defeatSetGlobalKey)));
                        if (!v && mobDrops.TryGetValue(n.Prefab ?? "", out var ds))
                            v = ds.Any(x => (x.StartsWith("Trophy") || dropSources[x] <= 2) && (mats.Contains(Tok(x) ?? "-") || trophies.Contains(x)));
                        break;
                    case "gate": v = World(n.Id.Substring(5)); break;
                }
                p.Done[n.Id] = v;
            }
            // multiplayer: if something is done, whatever *every* way of getting it requires was done too
            // (by someone in the world). The biome order is not a hard requirement, so it never implies.
            for (bool changed = true; changed;)
            {
                changed = false;
                foreach (var n in t.Nodes.Values)
                {
                    if (!p.Done.TryGetValue(n.Id, out var d) || !d || n.Kind == "biome") continue;
                    var opts = n.Options.Where(o => o.Req.All(r => t.Get(r)?.Reachable == true)).ToList();
                    if (opts.Count == 0) continue;
                    foreach (var r in opts.Skip(1).Aggregate((IEnumerable<string>)opts[0].Req, (a, o) => a.Intersect(o.Req)).ToList())
                        if (p.Done.TryGetValue(r, out var rd) && !rd) { p.Done[r] = true; p.Implied.Add(r); changed = true; }
                }
            }
            foreach (var n in t.Nodes.Values.Where(x => x.Reachable))
            {
                bool done = p.Done[n.Id];
                p.St[n.Id] = done ? State.Done
                    : n.Options.Any(o => o.Req.All(r => p.Done.TryGetValue(r, out var x) && x)) ? State.Available : State.Locked;
                p.Total++; if (done) p.DoneCount++;
            }
            return p;
        }

        public bool IsDone(string id) => Done.TryGetValue(id ?? "", out var d) && d;
        public State Of(string id) => St.TryGetValue(id ?? "", out var s) ? s : State.Locked;
    }
}
