using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace UnlockTree
{
    public enum Look { Shown, Unknown }

    /// Theme pages, like Minecraft's advancement tabs. A node can belong to several themes
    /// (a sword is both crafting and combat). Biomes belong to every theme: they are the spine of each tree.
    public static class Themes
    {
        public static readonly (string key, string en, string fr)[] All = {
            ("explore", "Exploration", "Exploration"), ("craft", "Crafting", "Artisanat"), ("build", "Building", "Construction"),
            ("combat", "Combat", "Combat"), ("food", "Food & farming", "Cuisine & culture") };

        static readonly HashSet<int> Weapons = new HashSet<int> { 3, 4, 14, 20, 22, 5 };       // one/two-handed, bow, torch?, shield
        static readonly HashSet<int> Armour = new HashSet<int> { 6, 7, 11, 17, 18 };
        static readonly HashSet<int> Ammo = new HashSet<int> { 9, 23 };
        static readonly HashSet<string> Gather = new HashSet<string> { "pick", "mine", "chop", "break", "loot", "fish", "drop", "spawn", "roam", "buy" };
        static readonly HashSet<string> FoodStations = new HashSet<string> { "piece_cauldron", "piece_cookingstation", "piece_cookingstation_iron",
            "piece_oven", "fermenter", "piece_preptable", "piece_MeadCauldron" };

        public static HashSet<string> Of(Tree t, Node n)
        {
            var s = new HashSet<string>();
            var hows = n.Options.SelectMany(o => o.How).Select(h => (string)h["t"]).ToList();
            var bestHow = n.Best?.How.Select(h => (string)h["t"]).FirstOrDefault();
            switch (n.Kind)
            {
                case "biome": foreach (var th in All) s.Add(th.key); break;
                case "gate": s.Add("explore"); s.Add("combat"); break;
                case "mob": case "boss": s.Add("combat"); if (n.Kind == "boss") s.Add("explore"); break;
                case "piece": case "station": case "level": s.Add("build");
                    if (n.Kind != "piece") s.Add("craft");
                    if (FoodStations.Contains(n.Station ?? n.Id)) s.Add("food");
                    break;
                case "tool": s.Add("craft"); s.Add("explore"); break;
                case "item":
                    var it = n.ItemType;
                    bool crafted = hows.Contains("craft") || hows.Contains("convert");
                    if (Weapons.Contains(it) || Armour.Contains(it) || Ammo.Contains(it)) { s.Add("combat"); if (crafted) s.Add("craft"); }
                    else if (it == 2) s.Add("food");                                   // consumables: food, meads, potions
                    else if (it == 13) s.Add("combat");                                // trophies
                    else if (it == 19 || it == 15) { s.Add("craft"); s.Add("explore"); }  // tools, utility
                    else
                    {
                        if (bestHow != null && Gather.Contains(bestHow) || !crafted) s.Add("explore");
                        if (crafted) s.Add("craft");
                    }
                    if (hows.Contains("grow") || n.Options.Any(o => o.Req.Any(r => FoodStations.Contains(r)))) s.Add("food");
                    break;
                default: s.Add("explore"); break;
            }
            return s;
        }
    }

    /// One theme's tree: every node of the page, done and available ones shown, the rest as "?" that turn
    /// into the real thing as the player progresses.
    public class View
    {
        public string Theme;
        public Dictionary<string, Look> Shown = new Dictionary<string, Look>();
        public Dictionary<string, string> Parent = new Dictionary<string, string>();       // theme parent (drawn edge)
        public Dictionary<string, List<string>> Kids = new Dictionary<string, List<string>>();
        public List<string> Roots = new List<string>();
        public int Done, Total;

        public static View Build(Tree t, Progress p, string theme, Dictionary<string, HashSet<string>> themes)
        {
            var v = new View { Theme = theme };
            bool In(string id) => themes.TryGetValue(id, out var s) && s.Contains(theme);
            // theme parent: nearest ancestor (through the real requirement tree) that is in this theme
            string Tp(Node n)
            {
                var seen = new HashSet<string>();
                for (var a = t.Get(n.Parent); a != null && seen.Add(a.Id); a = t.Get(a.Parent))
                    if (In(a.Id)) return a.Id;
                return null;
            }
            var members = t.Nodes.Values.Where(n => n.Reachable && In(n.Id)).ToList();
            var tp = members.ToDictionary(n => n.Id, Tp);
            v.Total = members.Count; v.Done = members.Count(n => p.IsDone(n.Id));
            // the whole tree is always drawn (its shape is laid out by hand); what isn't discovered yet is a "?"
            foreach (var n in members) v.Shown[n.Id] = p.Of(n.Id) == State.Locked ? Look.Unknown : Look.Shown;
            foreach (var id in v.Shown.Keys)
            {
                var pa = tp[id];
                if (pa != null && v.Shown.ContainsKey(pa))
                {
                    v.Parent[id] = pa;
                    if (!v.Kids.TryGetValue(pa, out var l)) v.Kids[pa] = l = new List<string>();
                    l.Add(id);
                }
                else v.Roots.Add(id);
            }
            // children: by depth, biomes last so the biome spine runs along the bottom of each branch
            int Key(string id) => t.Nodes[id].Kind == "biome" ? 1 : 0;
            foreach (var l in v.Kids.Values)
                l.Sort((a, b) =>
                {
                    int k = Key(a).CompareTo(Key(b)); if (k != 0) return k;
                    int d = t.Nodes[a].Depth.CompareTo(t.Nodes[b].Depth); return d != 0 ? d : string.CompareOrdinal(a, b);
                });
            // Ocean always on top, then the other root biomes, then anything else
            v.Roots = v.Roots.OrderBy(r => r == "biome:Ocean" ? 0 : 1).ThenBy(r => t.Nodes[r].Kind == "biome" ? 0 : 1).ThenBy(r => t.Nodes[r].Depth).ToList();
            return v;
        }
    }

    /// Left-to-right tidy tree: column = depth in the theme tree, a parent sits level with the middle of
    /// its children. Positions dragged by hand (per theme) win; auto-placed nodes step aside from them.
    public static class Layout
    {
        public const float CW = 112, CH = 104;
        static Dictionary<string, float[]> _saved;
        static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "unlocktree.layout.json");

        // Players use the layout shipped inside the mod. In dev mode the working copy in BepInEx/config is
        // loaded and saved instead; deploy.sh embeds it in the next build.
        public static Dictionary<string, float[]> Saved
        {
            get
            {
                if (_saved != null) return _saved;
                try
                {
                    string json = null;
                    if (Plugin.DevMode.Value && File.Exists(FilePath)) json = File.ReadAllText(FilePath);
                    else
                        using (var st = typeof(Layout).Assembly.GetManifestResourceStream("unlocktree.layout.json"))
                            if (st != null) using (var rd = new StreamReader(st)) json = rd.ReadToEnd();
                    if (json != null) _saved = JsonConvert.DeserializeObject<Dictionary<string, float[]>>(json);
                    if (_saved != null && !_saved.ContainsKey("_v2")) { Plugin.Log.LogWarning("old layout format (absolute positions) ignored"); _saved = null; }
                }
                catch (Exception e) { Plugin.Log.LogWarning("layout unreadable, using the automatic one: " + e.Message); }
                return _saved = _saved ?? new Dictionary<string, float[]> { ["_v2"] = new float[] { 2, 0 } };
            }
        }
        static void Save()
        {
            if (!Plugin.DevMode.Value) return;
            try { File.WriteAllText(FilePath, JsonConvert.SerializeObject(Saved, Formatting.Indented)); } catch (Exception e) { Plugin.Log.LogWarning(e.Message); }
        }
        public static string Key(string theme, string id) => theme + "|" + id;
        // A hand move is an offset (columns, rows) from the automatic spot. It applies to the node and its whole
        // branch; a grid of leaves moves as one ("block:<parent>").
        public static string BlockKey(string from) => "block:" + from;
        public static (float c, float r) Offset(string theme, string id) => Saved.TryGetValue(Key(theme, id), out var o) ? (o[0], o[1]) : (0, 0);
        public static void Move(string theme, string id, float dc, float dr)
        {
            var (c, r) = Offset(theme, id); c += dc; r += dr;
            if (Math.Abs(c) < .01f && Math.Abs(r) < .01f) Saved.Remove(Key(theme, id)); else Saved[Key(theme, id)] = new[] { c, r };
            Save();
        }
        public static void ResetTheme(string theme) { foreach (var k in Saved.Keys.Where(k => k.StartsWith(theme + "|")).ToList()) Saved.Remove(k); Save(); }

        /// A box of leaf children drawn as a grid (one edge from the parent to the box instead of one per leaf).
        public class Block { public string From; public float C, R; public int Cols, Rows; public List<string> Ids = new List<string>(); }
        public static List<Block> Blocks = new List<Block>();

        // A placed subtree, relative to its root: cells, the leaf grids, and per column the rows it covers.
        class Shape
        {
            public List<(string id, int c, int r)> Cells = new List<(string, int, int)>();
            public List<Block> Blocks = new List<Block>();
            public Dictionary<int, (int top, int bottom)> Cols = new Dictionary<int, (int, int)>();
            public void Add(string id, int c, int r)
            {
                Cells.Add((id, c, r));
                Cols[c] = Cols.TryGetValue(c, out var x) ? (Math.Min(x.top, r), Math.Max(x.bottom, r)) : (r, r);
            }
            // lowest offset at which `other` fits under what is already here, column by column
            public int Fit(Shape other, int gap)
            {
                int y = int.MinValue;
                foreach (var kv in other.Cols)
                    if (Cols.TryGetValue(kv.Key, out var mine)) y = Math.Max(y, mine.bottom + 1 + gap - kv.Value.top);
                return y == int.MinValue ? (Cols.Count == 0 ? 0 : Cols.Values.Min(x => x.top)) : y;
            }
            public void Merge(Shape o, int dc, int dr)
            {
                foreach (var (id, c, r) in o.Cells) Add(id, c + dc, r + dr);
                foreach (var b in o.Blocks) { b.C += dc; b.R += dr; Blocks.Add(b); }
            }
        }

        /// Compact left-to-right tree: leaf children in rows of up to 5, at most 3 rows (wider rows beyond 15),
        /// sub-branches stacked as tightly as their columns allow. Hand-placed nodes (dev layout) win.
        public static Dictionary<string, (float c, float r)> Place(View v)
        {
            Shape Walk(string id)
            {
                var sh = new Shape(); sh.Add(id, 0, 0);
                var kids = v.Kids.TryGetValue(id, out var l) ? l : new List<string>();
                var leaves = kids.Where(k => !v.Kids.ContainsKey(k) || v.Kids[k].Count == 0).ToList();
                var inner = kids.Except(leaves).ToList();
                int row = 0;
                if (leaves.Count > 0)
                {
                    // as few rows as rows of up to 5 allow (max 3), then balanced: 6 -> 3+3, 7 -> 4+3, 11 -> 4+4+3;
                    // beyond 15 items, 3 longer rows
                    int n = leaves.Count, rows = Math.Min(3, (n + 4) / 5), cols = (n + rows - 1) / rows;
                    var blk = new Block { From = id, C = 1, R = 0, Cols = cols, Rows = rows };
                    for (int i = 0; i < leaves.Count; i++) { sh.Add(leaves[i], 1 + i % cols, i / cols); blk.Ids.Add(leaves[i]); }
                    if (leaves.Count > 1) sh.Blocks.Add(blk);
                    row = rows;
                }
                foreach (var k in inner)
                {
                    var sub = Walk(k);
                    var shifted = new Shape(); foreach (var (cid, c, r) in sub.Cells) shifted.Add(cid, c + 1, r);   // contour only, blocks untouched
                    int y = sh.Cells.Count == 1 ? 0 : Math.Max(sh.Fit(shifted, 0), 0);
                    sh.Merge(sub, 1, y);
                }
                return sh;
            }
            var all = new Shape();
            foreach (var root in v.Roots)
            {
                var s = Walk(root);
                all.Merge(s, 0, all.Cells.Count == 0 ? 0 : Math.Max(all.Fit(s, 1), 0));
            }
            var auto = all.Cells.ToDictionary(x => x.id, x => ((float)x.c, (float)x.r));
            Blocks = all.Blocks;
            // hand offsets: a node's own, plus its block's, plus every ancestor's (the branch follows its parent)
            var blockOf = new Dictionary<string, Block>();
            foreach (var bl in Blocks) foreach (var id in bl.Ids) blockOf[id] = bl;
            var cum = new Dictionary<string, (float c, float r)>();
            (float c, float r) Cum(string id)
            {
                if (cum.TryGetValue(id, out var x)) return x;
                var o = Offset(v.Theme, id);
                if (blockOf.TryGetValue(id, out var bl)) { var bo = Offset(v.Theme, BlockKey(bl.From)); o = (o.c + bo.c, o.r + bo.r); }
                if (v.Parent.TryGetValue(id, out var pa)) { var po = Cum(pa); o = (o.c + po.c, o.r + po.r); }
                return cum[id] = o;
            }
            var pos = new Dictionary<string, (float c, float r)>();
            foreach (var kv in auto) { var o = Cum(kv.Key); pos[kv.Key] = (kv.Value.Item1 + o.c, kv.Value.Item2 + o.r); }
            foreach (var bl in Blocks)
            {
                var o = Cum(bl.From); var bo = Offset(v.Theme, BlockKey(bl.From));
                bl.C += o.c + bo.c; bl.R += o.r + bo.r;
            }
            return pos;
        }
    }
}
