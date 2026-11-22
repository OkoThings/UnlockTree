using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnlockTree
{
    /// Adds an "Unlock Tree" button next to the inventory's Trophies / Skills / Compendium buttons:
    /// a copy of the game's own button, so it looks the same.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    static class InventoryButton
    {
        static GameObject _btn;
        public static void Destroy() { if (_btn) Object.Destroy(_btn); _btn = null; }

        // average opaque colour of the first readable icon sprite in the row (texture may be unreadable: then null)
        static Color? RowGold(List<RectTransform> line)
        {
            foreach (var r in line)
            {
                if (r == (RectTransform)_btn.transform) continue;
                foreach (var img in r.GetComponentsInChildren<Image>(true))
                {
                    if (!img.sprite || img.name == "Background") continue;
                    try
                    {
                        var tex = img.sprite.texture; var rc = img.sprite.textureRect;
                        var px = tex.GetPixels((int)rc.x, (int)rc.y, (int)rc.width, (int)rc.height);
                        float R = 0, G = 0, B = 0, n = 0;
                        foreach (var c in px) if (c.a > .8f) { R += c.r; G += c.g; B += c.b; n++; }
                        if (n < 20) continue;
                        var col = new Color(R / n, G / n, B / n) * img.color; col.a = img.color.a;
                        Plugin.Log.LogInfo($"row icon colour from {r.name}/{img.name}: {col}");
                        return col;
                    }
                    catch { }   // not readable
                }
            }
            return new Color(.79f, .60f, .31f);   // the row's gold, measured on screen
        }

        static Sprite _icon;
        static Sprite Icon()
        {
            if (_icon) return _icon;
            using (var s = typeof(InventoryButton).Assembly.GetManifestResourceStream("button_icon.png"))
            {
                if (s == null) return null;
                var bytes = new byte[s.Length]; s.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                // by reflection: Unity 6 also has a ReadOnlySpan overload that net472 can't compile against
                var load = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (load == null || !(bool)load.Invoke(null, new object[] { tex, bytes, false })) return null;
                return _icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
            }
        }

        // after a hot reload the inventory may already be open: add the button the next time it shows

        static void Postfix(InventoryGui __instance)
        {
            if (_btn) return;
            try
            {
                var buttons = __instance.GetComponentsInChildren<Button>(true);
                bool Opens(Button b, string m) =>
                    Enumerable.Range(0, b.onClick.GetPersistentEventCount()).Any(i => b.onClick.GetPersistentMethodName(i) == m);
                var row = buttons.Where(b => Opens(b, "OnOpenTrophies") || Opens(b, "OnOpenSkills") || Opens(b, "OnOpenTexts") || Opens(b, "OnOpenAchievements")).ToList();
                var model = row.FirstOrDefault(b => Opens(b, "OnOpenTrophies")) ?? row.FirstOrDefault();
                if (!model) { Plugin.Log.LogWarning("inventory buttons not found; use the hotkey"); return; }

                _btn = Object.Instantiate(model.gameObject, model.transform.parent);
                _btn.name = "UnlockTreeButton";
                var b2 = _btn.GetComponent<Button>();
                for (int i = 0; i < b2.onClick.GetPersistentEventCount(); i++) b2.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                b2.onClick.RemoveAllListeners();
                b2.onClick.AddListener(() => { InventoryGui.instance.Hide(); SagaWindow.Open(); });

                // every button of that row (the game's and other mods'), ours just before the last one,
                // spread evenly over the row's original width
                var mrt = (RectTransform)model.transform;
                var panel = mrt.parent;                      // the row's own local space
                // search only the header panel that holds the row (climb while the container is narrower than
                // a few buttons), never the inventory grid: its slots sit at the same height in other panels
                var box = panel;
                while (box.parent && ((RectTransform)box).rect.width < mrt.rect.width * 4) box = box.parent;
                float y = panel.InverseTransformPoint(mrt.position).y, w = mrt.rect.width, h = mrt.rect.height;
                // Selectable, not just Button: the PVP switch at the end of the row is a Toggle
                var line = box.GetComponentsInChildren<Selectable>(true)
                              .Where(b => b && b.gameObject != _btn && b.gameObject.activeSelf
                                          && !b.GetComponentInParent<InventoryGrid>()
                                          && Mathf.Abs(panel.InverseTransformPoint(b.transform.position).y - y) < h * .3f
                                          && ((RectTransform)b.transform).rect.width < w * 2f)
                              .Select(b => (RectTransform)b.transform).OrderBy(r => panel.InverseTransformPoint(r.position).x).ToList();
                var rt = (RectTransform)_btn.transform;
                if (line.Count >= 2)
                {
                    float x0 = panel.InverseTransformPoint(line[0].position).x, x1 = panel.InverseTransformPoint(line[line.Count - 1].position).x;
                    line.Insert(line.Count - 1, rt);
                    float step = (x1 - x0) / (line.Count - 1);
                    for (int i = 0; i < line.Count; i++)
                    {
                        var lp = panel.InverseTransformPoint(line[i].position);
                        line[i].position = panel.TransformPoint(new Vector3(x0 + i * step, y, lp.z));
                    }
                }
                rt.anchoredPosition += Plugin.ButtonOffset.Value;
                _btn.transform.SetAsLastSibling();
                Plugin.Log.LogInfo($"inventory row ({line.Count}): {string.Join(", ", line.Select(r => r.name + (r.parent == panel ? "" : " [other parent]")))}");

                // the game's button: its own Image is the icon; children are labels / hover glows
                var tree = new System.Text.StringBuilder();
                foreach (var t in _btn.GetComponentsInChildren<Transform>(true))
                    tree.Append($" [{t.name}: img={t.GetComponent<Image>()?.sprite?.name} txt={t.GetComponent<TMP_Text>()?.text}]");
                Plugin.Log.LogInfo("button parts:" + tree);
                foreach (var t in _btn.GetComponentsInChildren<TMP_Text>(true)) t.text = Text.L("Unlock Tree", "Arbre de progression");
                var tip = _btn.GetComponent<UITooltip>();
                if (tip) tip.m_text = Text.L("Unlock Tree", "Arbre de progression");
                // the icon is the child drawn last (a "Background" child is the frame / hover glow)
                var own = _btn.GetComponent<Image>();
                var kids = _btn.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != _btn && i.sprite).ToList();
                var icon = kids.FirstOrDefault(i => i.name == "Image" || i.name.ToLower().Contains("icon")) ?? kids.LastOrDefault() ?? (own && own.sprite ? own : null);
                if (icon && Icon() is Sprite sp)
                {
                    // our sprite is white: take the colour of the game's own icons in this row (sampled from a
                    // neighbour's sprite), so it matches whatever tint/theme the row uses
                    var tint = icon.color;
                    if (RowGold(line) is Color g) tint = g;
                    icon.sprite = sp; icon.preserveAspect = true; icon.color = tint;
                }
                Plugin.Log.LogInfo($"inventory button added next to '{model.name}' ({row.Count} buttons in the row)");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("inventory button failed: " + e); }
        }
    }
}
