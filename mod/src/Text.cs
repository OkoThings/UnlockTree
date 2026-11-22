using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnlockTree
{
    /// Names come from the game's own localization; the few sentences the mod writes are in English and French.
    public static class Text
    {
        public static bool Fr => Localization.instance != null && Localization.instance.GetSelectedLanguage() == "French";
        public static string L(string en, string fr) => Fr ? fr : en;

        static string Loc(string token)
        {
            if (string.IsNullOrEmpty(token) || Localization.instance == null) return null;
            var s = Localization.instance.Localize(token.StartsWith("$") ? token : "$" + token);
            return string.IsNullOrEmpty(s) || s.StartsWith("[") ? null : s;
        }

        public static string Name(Tree t, string id)
        {
            var n = t.Get(id);
            if (n == null) return id;
            switch (n.Kind)
            {
                case "tool": return (n.Tool == "pickaxe" ? L("Pickaxe", "Pioche") : L("Axe", "Hache")) + L(" tier ", " niveau ") + n.Tier;
                case "level": return (Loc(n.Token) ?? n.Station) + " " + n.Level;
                case "biome": return Loc(n.Token) ?? (Fr ? n.Fr : n.En) ?? id;
            }
            return Loc(n.Token) ?? (Fr ? n.Fr : n.En) ?? id;
        }

        public static string Src(JToken v)
        {
            if (v == null) return "";
            if (v.Type == JTokenType.Array) return (string)v[Fr ? 1 : 0];
            var s = (string)v;
            return s.StartsWith("$") ? (Loc(s) ?? s) : s;
        }

        public static string Description(Tree t, string id)
        {
            var go = ObjectDB.instance?.GetItemPrefab(id);
            var tok = go ? go.GetComponent<ItemDrop>().m_itemData.m_shared.m_description : null;
            if (tok == null && ZNetScene.instance?.GetPrefab(id) is GameObject p && p.GetComponent<Piece>() is Piece pc) tok = pc.m_description;
            return tok != null ? Loc(tok) : null;
        }

        /// One sentence for a way of getting a node, e.g. "Craft at Forge (level 2)" or "Mine Copper deposit in Black Forest".
        public static string Way(Tree t, Option o)
        {
            var h = o.How.FirstOrDefault() ?? new JObject();
            string N(string key) => h[key] != null ? Name(t, (string)h[key]) : "";
            string mob = o.Req.FirstOrDefault(r => r.StartsWith("mob:"));
            string biome = o.Req.FirstOrDefault(r => r.StartsWith("biome:"));
            int lvl = (int?)h["level"] ?? 1, amount = (int?)h["amount"] ?? 1;
            string s;
            switch ((string)h["t"])
            {
                case "craft":
                    s = h["st"] != null && h["st"].Type != JTokenType.Null
                        ? L("Craft at ", "Fabriquer à : ") + N("st") + (lvl > 1 ? L($" (level {lvl})", $" (niveau {lvl})") : "")
                        : L("Craft by hand", "Fabriquer à la main");
                    if (amount > 1) s += L($", makes {amount}", $", donne {amount}");
                    break;
                case "build": s = L("Build with the ", "Construire avec : ") + N("tool"); break;
                case "drop":
                    var ch = (double?)h["chance"] ?? 100;
                    s = L("Dropped by ", "Lâché par ") + Name(t, mob) + (ch < 100 ? $" ({ch}%)" : ""); break;
                case "mine": s = L("Mine ", "Miner : ") + Src(h["src"]); break;
                case "chop": s = L("Chop ", "Abattre : ") + Src(h["src"]); break;
                case "break": s = L("Break ", "Casser : ") + Src(h["src"]); break;
                case "pick": s = L("Pick up ", "Ramasser : ") + Src(h["src"]); break;
                case "loot": s = L("Loot from ", "Butin de : ") + Src(h["src"]); break;
                case "spawn": s = h["src"] != null ? L("Spawns from ", "Apparaît depuis : ") + Src(h["src"]) : L("Lives there", "Vit sur place"); break;
                case "roam": s = L("Roams", "Rôde"); break;
                case "summon": s = L($"Summon by offering {h["n"]}× ", $"Invoquer en offrant {h["n"]}× ") + N("item"); break;
                case "offer": s = L($"Open by offering {h["n"]}× ", $"Ouvrir en offrant {h["n"]}× ") + N("item"); break;
                case "story": return L("Next biome after defeating ", "Biome suivant après avoir vaincu ") + N("boss");
                case "start": return L("Where every journey starts", "Là où tout commence");
                case "tool": s = L($"Any tool of tier {h["tier"]}", $"Outil de niveau {h["tier"]}"); break;
                case "convert": s = L("Process in a ", "Transformer dans : ") + N("st"); break;
                case "produce": s = L("Collected from a ", "Récolté sur : ") + N("st"); break;
                case "grow": s = L("Grows from a planted ", "Pousse depuis : ") + N("st"); break;
                case "lure": s = L("Lured to a ", "Attiré par : ") + N("st"); break;
                case "fish": s = L("Fish with ", "Pêcher avec : ") + N("bait"); break;
                case "buy": s = L($"Buy from {Src(h["src"])} for {h["price"]} coins", $"Acheter à {Src(h["src"])} pour {h["price"]} pièces"); break;
                case "level": s = L("Previous level plus one more upgrade", "Niveau précédent plus une amélioration"); break;
                default: s = L("Get it", "Obtenir"); break;
            }
            if (biome != null) s += L(" in ", ", ") + Name(t, biome);
            if (h["loc"] != null) s += " (" + Src(h["loc"]) + ")";
            foreach (var k in (h["keys"] as JArray) ?? new JArray()) s += L(", door locked with ", ", porte fermée par ") + Name(t, (string)k);
            foreach (var k in (h["after"] as JArray) ?? new JArray()) s += L(", after ", ", après ") + Name(t, (string)k);
            return s;
        }

        /// Amount of each requirement in a way (recipe ingredients, offerings)
        public static Dictionary<string, int> Amounts(Option o)
        {
            var a = new Dictionary<string, int>();
            foreach (var h in o.How)
            {
                foreach (var r in (h["res"] as JArray) ?? new JArray()) if (!a.ContainsKey((string)r[0])) a[(string)r[0]] = (int)r[1];
                var t = (string)h["t"];
                if ((t == "summon" || t == "offer") && h["item"] != null) a[(string)h["item"]] = (int?)h["n"] ?? 1;
            }
            return a;
        }
    }
}
