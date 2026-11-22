using System;
using System.Linq;

namespace UnlockTree
{
    /// Picks the tree: built live from the game, or the shipped one when the live build fails or looks wrong.
    public static class Provider
    {
        static Tree _shipped, _tree;
        public static string Why = "";

        public static Tree Shipped { get { if (_shipped == null) { _shipped = Tree.LoadShipped(); _shipped.Solve(); } return _shipped; } }

        public static Tree Current
        {
            get
            {
                if (_tree != null) return _tree;
                try
                {
                    var live = LiveBuilder.Build(Shipped);
                    live.Solve();
                    int lr = live.Nodes.Values.Count(n => n.Reachable), sr = Shipped.Nodes.Values.Count(n => n.Reachable);
                    var missingBiomes = Tree.Eras.Where(e => live.Get("biome:" + e)?.Reachable != true).ToList();
                    if (missingBiomes.Count > 0) Why = "live tree cannot reach " + string.Join(", ", missingBiomes);
                    else if (lr < sr * 0.8) Why = $"live tree reaches only {lr} nodes (shipped: {sr})";
                    else { _tree = live; Why = $"built live from the game: {lr} nodes (shipped tree: {sr})"; }
                }
                catch (Exception e) { Why = "live build failed: " + e.Message; Plugin.Log.LogError(e); }
                if (_tree == null) { _tree = Shipped; Why += " - using the shipped tree"; }
                Plugin.Log.LogInfo("Unlock Tree: " + Why);
                return _tree;
            }
        }

        public static void Reset() => _tree = null;
    }
}
