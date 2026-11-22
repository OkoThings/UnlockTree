using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnlockTree
{
    /// One way of getting a node: every requirement is needed (AND). How = what the game data says
    /// about it (craft at X, dropped by Y at 5%, mine Z...), one entry per source that gives this option.
    public class Option
    {
        public List<string> Req = new List<string>();
        public List<JObject> How = new List<JObject>();
    }

    public class Node
    {
        public string Id, Kind, Token, En, Fr, Prefab, Station, Tool;
        public int ItemType = -1, Level, Tier;
        public List<string> Exts;              // level nodes: station extensions that count
        public List<Option> Options = new List<Option>();
        // solved
        public int Depth = int.MaxValue, Era;
        public Option Best;
        public string Parent;
        public bool Reachable => Depth != int.MaxValue;

        public void Add(IEnumerable<string> req, JObject how)
        {
            var r = req.ToList();
            var o = Options.FirstOrDefault(x => x.Req.Count == r.Count && !x.Req.Except(r).Any());
            if (o == null) { o = new Option { Req = r }; Options.Add(o); }
            if (how != null && o.How.Count < 12 && !o.How.Any(h => JToken.DeepEquals(h, how))) o.How.Add(how);
        }
    }

    public class Tree
    {
        public static readonly string[] Eras = { "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "AshLands", "DeepNorth" };
        public Dictionary<string, Node> Nodes = new Dictionary<string, Node>();
        public string Source = "?";
        public Dictionary<string, List<string>> Unlocks = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> MobDrops;

        public Node Get(string id) => id != null && Nodes.TryGetValue(id, out var n) ? n : null;

        public Node Make(string id, string kind, string token)
        {
            if (!Nodes.TryGetValue(id, out var n)) Nodes[id] = n = new Node { Id = id, Kind = kind, Token = token };
            return n;
        }

        // ---- shipped tree (graph.py export) ----
        public static Tree LoadShipped()
        {
            using (var s = typeof(Tree).Assembly.GetManifestResourceStream("unlocktree.json"))
            using (var rd = new StreamReader(s))
            {
                var j = JObject.Parse(rd.ReadToEnd());
                var t = new Tree { Source = "shipped" };
                foreach (JObject o in j["nodes"])
                {
                    var n = t.Make((string)o["id"], (string)o["kind"], (string)o["token"]);
                    n.En = (string)o["en"]; n.Fr = (string)o["fr"]; n.Prefab = (string)o["prefab"];
                    n.ItemType = (int?)o["itype"] ?? -1; n.Station = (string)o["station"]; n.Level = (int?)o["level"] ?? 0;
                    n.Tool = (string)o["tool"]; n.Tier = (int?)o["tier"] ?? 0;
                    n.Exts = o["exts"]?.Type == JTokenType.Array ? o["exts"].Select(x => (string)x).ToList() : null;
                    foreach (JObject op in o["options"])
                    {
                        var opt = new Option { Req = op["r"].Select(x => (string)x).ToList() };
                        foreach (JObject h in op["h"]) opt.How.Add(h);
                        n.Options.Add(opt);
                    }
                }
                return t;
            }
        }

        // ---- solve: depth (shortest way through the hardest requirement), era (biome), parent ----
        public void Solve()
        {
            foreach (var n in Nodes.Values) { n.Depth = int.MaxValue; n.Best = null; n.Parent = null; }
            int D(string id) => Nodes.TryGetValue(id, out var x) ? x.Depth : int.MaxValue;
            for (int pass = 0; pass < 300; pass++)
            {
                bool changed = false;
                foreach (var n in Nodes.Values)
                {
                    if (n.Kind == "level") LevelOptions(n);
                    foreach (var o in n.Options)
                    {
                        int v = 0; bool ok = true;
                        foreach (var r in o.Req) { int d = D(r); if (d == int.MaxValue) { ok = false; break; } v = Math.Max(v, d + 1); }
                        if (ok && v < n.Depth) { n.Depth = v; n.Best = o; changed = true; }
                    }
                }
                if (!changed) break;
            }
            var prio = new Dictionary<string, int> { { "biome", 4 }, { "boss", 3 }, { "gate", 3 }, { "level", 2 }, { "station", 2 }, { "tool", 1 } };
            foreach (var n in Nodes.Values.Where(x => x.Reachable).OrderBy(x => x.Depth))
            {
                if (n.Kind == "biome")
                {
                    var b = n.Id.Substring(6); int e = Array.IndexOf(Eras, b); n.Era = e < 0 ? 0 : e;
                    continue;
                }
                var reqs = n.Best.Req.Where(r => Get(r)?.Reachable == true).ToList();
                n.Era = reqs.Count == 0 ? 0 : reqs.Max(r => Nodes[r].Era);
                if (reqs.Count > 0)
                    n.Parent = reqs.OrderByDescending(r => Nodes[r].Era).ThenByDescending(r => Nodes[r].Depth)
                                   .ThenByDescending(r => prio.TryGetValue(Nodes[r].Kind, out var p) ? p : 0).First();
            }
            foreach (var n in Nodes.Values.Where(x => x.Kind == "biome" && x.Reachable && x.Best != null))
            {
                var reqs = n.Best.Req.Where(r => Get(r)?.Reachable == true).ToList();
                if (reqs.Count > 0) n.Parent = reqs.OrderByDescending(r => Nodes[r].Depth).First();
            }
            Unlocks.Clear();
            foreach (var n in Nodes.Values.Where(x => x.Reachable))
                foreach (var o in n.Options)
                    if (o.Req.All(r => Get(r)?.Reachable == true))
                        foreach (var r in o.Req.Distinct())
                        {
                            if (r == n.Id) continue;
                            if (!Unlocks.TryGetValue(r, out var l)) Unlocks[r] = l = new List<string>();
                            if (!l.Contains(n.Id)) l.Add(n.Id);
                        }
        }

        // station level L needs level L-1 plus one more extension: the easiest one not yet counted
        void LevelOptions(Node n)
        {
            if (n.Exts == null) return;
            var exts = n.Exts.Where(e => Nodes.ContainsKey(e)).OrderBy(e => Nodes[e].Depth).ToList();
            int k = n.Level - 1;
            string prev = k <= 1 ? n.Station : n.Station + "@" + k;
            var keepHow = n.Options.FirstOrDefault()?.How;
            n.Options.Clear();
            if (exts.Count >= k && Nodes.ContainsKey(prev))
                n.Options.Add(new Option { Req = new List<string> { prev, exts[k - 1] },
                    How = keepHow ?? new List<JObject> { new JObject { ["t"] = "level", ["level"] = n.Level, ["exts"] = new JArray(n.Exts) } } });
        }
    }
}
