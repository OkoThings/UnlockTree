using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnlockTree
{
    /// Looks borrowed from the game's own inventory window, so the panel sits naturally in the UI.
    static class Style
    {
        public static TMP_FontAsset Body, Title;
        public static Sprite Panel, Slot;
        public static Material PanelMat;
        public static Color Gold = new Color(1f, .78f, .36f), Steel = new Color(.62f, .78f, .9f), Dim = new Color(.55f, .55f, .55f),
                            Ink = new Color(.92f, .88f, .8f), LockedC = new Color(.3f, .3f, .3f, .9f);
        public static void Load()
        {
            if (Body) return;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            Body = fonts.FirstOrDefault(f => f.name == "Valheim-AveriaSerifLibre") ?? fonts.FirstOrDefault();
            Title = fonts.FirstOrDefault(f => f.name == "Valheim-Norsebold") ?? Body;
            var gui = InventoryGui.instance;
            if (gui)
            {
                var bg = gui.m_player ? gui.m_player.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite && i.rectTransform.rect.width > 300) : null;
                if (bg) { Panel = bg.sprite; PanelMat = bg.material; }
                var slot = gui.m_playerGrid && gui.m_playerGrid.m_elementPrefab ? gui.m_playerGrid.m_elementPrefab.GetComponent<Image>() : null;
                if (slot) Slot = slot.sprite;
            }
        }
    }

    public static class SagaWindow
    {
        static GameObject _root;
        static int _closedFrame = -1;
        public static bool IsOpen => _root && _root.activeSelf;
        public static bool ClosedThisFrame => _closedFrame == Time.frameCount;
        public static void Toggle() { if (IsOpen) Close(); else Open(); }
        // Jotunn's BlockInput frees the cursor and stops the player like the game's own menus; it counts calls,
        // so every Open is paired with exactly one Close.
        public static void Open()
        {
            if (IsOpen) return;
            if (!_root) _root = Ui.Create();
            _root.SetActive(true); Jotunn.Managers.GUIManager.BlockInput(true);
            Ui.Refresh();
        }
        public static void Destroy() { if (_root) Object.Destroy(_root); _root = null; }
        public static void Close()
        {
            if (!IsOpen) return;
            Ui.ClearSearch();
            _root.SetActive(false); _closedFrame = Time.frameCount; Jotunn.Managers.GUIManager.BlockInput(false);
        }
    }

    static class Ui
    {
        const float W = 1180, H = 720, Side = 360;
        static RectTransform _content, _viewport, _detail; static TMP_Text _title, _count, _detailText, _foot;
        static Button _arrangeBtn, _resetBtn; static readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>();
        static Tree _tree; static Progress _prog; static View _view; static string _theme = "explore", _selected;
        static Dictionary<string, HashSet<string>> _themes; static Tree _themesFor;
        static Dictionary<string, (float c, float r)> _pos;
        public static bool Arranging;

        // ---------- construction ----------
        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
        static RectTransform Place(RectTransform r, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        { r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = oMin; r.offsetMax = oMax; return r; }
        static Image Img(RectTransform r, Color c, Sprite s = null)
        {
            var i = r.gameObject.AddComponent<Image>(); i.color = c; i.sprite = s;
            if (s) i.type = Image.Type.Sliced;
            return i;
        }
        static TMP_Text Txt(RectTransform r, string s, float size, Color c, TextAlignmentOptions al = TextAlignmentOptions.TopLeft, bool title = false)
        {
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = title ? Style.Title : Style.Body; t.fontSize = size; t.color = c; t.alignment = al; t.text = s;
            t.richText = true; t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }
        static Button Btn(Transform parent, string label, float w, System.Action onClick)
        {
            var r = Rect("btn", parent); r.sizeDelta = new Vector2(w, 30);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 30;
            Img(r, new Color(0, 0, 0, .45f), Style.Slot);
            var b = r.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => onClick());
            Txt(Place(Rect("t", r), Vector2.zero, Vector2.one, new Vector2(6, 0), new Vector2(-6, 0)), label, 16, Style.Ink, TextAlignmentOptions.Center);
            return b;
        }
        static void SetBtn(Button b, bool on)
        {
            b.GetComponent<Image>().color = on ? new Color(.55f, .4f, .15f, .8f) : new Color(0, 0, 0, .45f);
        }

        public static GameObject Create()
        {
            Style.Load();
            // Jotunn's GUI canvas scales with the game's UI scale setting; its wood panel is the game's own look
            var gm = Jotunn.Managers.GUIManager.Instance;
            var front = Jotunn.Managers.GUIManager.CustomGUIFront;
            GameObject root; RectTransform win;
            if (front && gm != null)
            {
                root = gm.CreateWoodpanel(front.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, W, H);
                root.name = "UnlockTree"; win = (RectTransform)root.transform;
            }
            else
            {
                root = new GameObject("UnlockTree", typeof(RectTransform));
                Object.DontDestroyOnLoad(root);
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
                var sc = root.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = .5f;
                root.AddComponent<GraphicRaycaster>();
                win = Rect("window", root.transform); win.sizeDelta = new Vector2(W, H);
                Img(win, new Color(.11f, .095f, .08f, .97f));
            }

            // header
            _title = Txt(Place(Rect("title", win), new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -58), new Vector2(-24, -14)),
                         "Unlock Tree", 34, Style.Gold, TextAlignmentOptions.Left, true);
            _count = Txt(Place(Rect("count", win), new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -58), new Vector2(-64, -14)),
                         "", 18, Style.Ink, TextAlignmentOptions.Right);
            var x = Btn(win, "×", 32, SagaWindow.Close).GetComponent<RectTransform>();
            Place(x, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -50), new Vector2(-18, -18));

            // toolbar: theme pages + arrange
            var bar = Place(Rect("bar", win), new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -98), new Vector2(-24, -64));
            var hl = bar.gameObject.AddComponent<HorizontalLayoutGroup>(); hl.spacing = 6; hl.childForceExpandWidth = false; hl.childControlWidth = true; hl.childControlHeight = true;
            foreach (var th in Themes.All)
            {
                var k = th.key;
                _tabs[k] = Btn(bar, Text.L(th.en, th.fr), 150, () => { _theme = k; Refresh(); });
            }
            var spacer = Rect("sp", bar); spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _arrangeBtn = Btn(bar, Text.L("Arrange", "Disposer"), 120, () => { Arranging = !Arranging; Refresh(); });
            _resetBtn = Btn(bar, Text.L("Reset page", "Réinitialiser"), 140, () => { Layout.ResetTheme(_theme); Refresh(); });

            // tree viewport (scrolls / drags) on the left, details on the right
            var view = Place(Rect("view", win), Vector2.zero, Vector2.one, new Vector2(24, 44), new Vector2(-Side - 36, -106));
            Img(view, new Color(0, 0, 0, .35f));
            view.gameObject.AddComponent<RectMask2D>();
            _viewport = view;
            _content = Rect("content", view); _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0, 1);
            var sr = view.gameObject.AddComponent<ScrollRect>(); sr.content = _content; sr.viewport = view;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 40; sr.inertia = false;

            _detail = Place(Rect("detail", win), new Vector2(1, 0), new Vector2(1, 1), new Vector2(-Side - 24, 44), new Vector2(-24, -106));
            Img(_detail, new Color(0, 0, 0, .35f));
            _detail.gameObject.AddComponent<RectMask2D>();
            var dc = Rect("dcontent", _detail); dc.anchorMin = new Vector2(0, 1); dc.anchorMax = new Vector2(1, 1); dc.pivot = new Vector2(.5f, 1);
            dc.offsetMin = new Vector2(14, 0); dc.offsetMax = new Vector2(-14, -12);
            _detailText = Txt(dc, "", 16, Style.Ink); _detailText.overflowMode = TextOverflowModes.Overflow;
            _detailText.raycastTarget = true; dc.gameObject.AddComponent<LinkClicks>();
            dc.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var dsr = _detail.gameObject.AddComponent<ScrollRect>(); dsr.content = dc; dsr.viewport = _detail; dsr.horizontal = false;
            dsr.movementType = ScrollRect.MovementType.Clamped; dsr.scrollSensitivity = 30;

            _foot = Txt(Place(Rect("foot", win), new Vector2(0, 0), new Vector2(1, 0), new Vector2(24, 10), new Vector2(-24, 38)), "", 13, Style.Dim, TextAlignmentOptions.Left);
            CreateSearch(win, gm);
            return root;
        }

        // ---------- search ----------
        // A box in the header; matches drop down under it, across every page. Picking one jumps to it.
        // Undiscovered nodes never match (same no-spoiler rule as the "?" in the tree).
        static InputField _search; static RectTransform _results;
        const int MaxResults = 10;
        public static bool Typing => _search && _search.isFocused;

        static void CreateSearch(RectTransform win, Jotunn.Managers.GUIManager gm)
        {
            if (gm == null) return;
            var go = gm.CreateInputField(win, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-340, -36), InputField.ContentType.Standard,
                                         Text.L("Search…", "Rechercher…"), 16, 300, 32);
            _search = go.GetComponent<InputField>();
            _search.onValueChanged.AddListener(_ => ShowResults());
            _search.onEndEdit.AddListener(_ =>
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { var f = Search(_search.text).FirstOrDefault(); if (f != null) Pick(f); }
            });
            _results = Rect("results", win);
            _results.anchorMin = _results.anchorMax = new Vector2(1, 1); _results.pivot = new Vector2(.5f, 1);
            _results.anchoredPosition = new Vector2(-340, -54);
            Img(_results, new Color(.08f, .07f, .06f, .97f), Style.Slot);
            var vl = _results.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.padding = new RectOffset(4, 4, 4, 4); vl.spacing = 2; vl.childControlWidth = vl.childControlHeight = true; vl.childForceExpandHeight = false;
            var fit = _results.gameObject.AddComponent<ContentSizeFitter>(); fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _results.sizeDelta = new Vector2(360, 0);
            _results.gameObject.SetActive(false);
        }

        /// Escape while searching clears the box instead of closing the window.
        /// The InputField may handle Escape itself (drop focus) before our Update runs, so focus is also judged
        /// from the previous frame (WasTyping, set in Plugin.LateUpdate).
        public static bool WasTyping;
        public static bool ConsumeEscape()
        {
            if (!_search || (string.IsNullOrEmpty(_search.text) && !_search.isFocused && !WasTyping)) return false;
            ClearSearch();
            return true;
        }

        public static void ClearSearch()
        {
            if (!_search) return;
            _search.text = "";
            _search.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject == _search.gameObject) EventSystem.current.SetSelectedGameObject(null);
            if (_results) _results.gameObject.SetActive(false);
        }

        // lower case, no accents, no rich text: "Épée" matches "epee"
        static string Fold(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        static List<string> Search(string query)
        {
            var q = Fold(query).Trim();
            if (q.Length == 0 || _tree == null || _themes == null) return new List<string>();
            int Score(string id)
            {
                var name = Fold(Text.Name(_tree, id));
                if (name.StartsWith(q)) return 0;
                if (name.Contains(" " + q)) return 1;
                if (name.Contains(q)) return 2;
                var n = _tree.Nodes[id];   // the other language's name, or the prefab id
                return Fold(n.En).Contains(q) || Fold(n.Fr).Contains(q) || Fold(id).Contains(q) ? 3 : -1;
            }
            return _themes.Keys.Where(id => !Unknown(id))
                .Select(id => (id, s: Score(id))).Where(x => x.s >= 0)
                .OrderBy(x => x.s).ThenBy(x => _prog.Of(x.id) == State.Available ? 0 : 1).ThenBy(x => _tree.Nodes[x.id].Depth).ThenBy(x => Text.Name(_tree, x.id))
                .Select(x => x.id).ToList();
        }

        static void ShowResults()
        {
            for (int i = _results.childCount - 1; i >= 0; i--) Object.Destroy(_results.GetChild(i).gameObject);
            if (string.IsNullOrWhiteSpace(_search.text)) { _results.gameObject.SetActive(false); return; }
            var hits = Search(_search.text);
            _results.gameObject.SetActive(true); _results.SetAsLastSibling();
            if (hits.Count == 0)
            {
                var r = Rect("none", _results); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
                Txt(Place(Rect("t", r), Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0)), Text.L("No match among discovered steps", "Aucun résultat parmi les étapes découvertes"),
                    14, Style.Dim, TextAlignmentOptions.MidlineLeft);
                return;
            }
            foreach (var id in hits.Take(MaxResults)) ResultRow(id);
            if (hits.Count > MaxResults)
            {
                var r = Rect("more", _results); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
                Txt(Place(Rect("t", r), Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0)), Text.L($"…{hits.Count - MaxResults} more, keep typing", $"…{hits.Count - MaxResults} de plus, précise"),
                    13, Style.Dim, TextAlignmentOptions.MidlineLeft);
            }
        }

        static void ResultRow(string id)
        {
            var n = _tree.Get(id); var st = _prog.Of(id);
            var r = Rect("hit:" + id, _results); r.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            Img(r, new Color(0, 0, 0, .35f));
            var icon = Icons.For(_tree, n);
            var ir = Place(Rect("icon", r), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(4, -14), new Vector2(32, 14));
            if (icon) { var ii = Img(ir, Color.white); ii.sprite = icon; ii.type = Image.Type.Simple; ii.preserveAspect = true; ii.raycastTarget = false; }
            else Txt(ir, Glyph(n), 20, Style.Gold, TextAlignmentOptions.Center, true);
            var pages = Themes.All.Where(t => _themes[id].Contains(t.key)).Select(t => Text.L(t.en, t.fr));
            var stTxt = st == State.Done ? Text.L("done", "fait") : Text.L("available", "disponible");
            Txt(Place(Rect("t", r), Vector2.zero, Vector2.one, new Vector2(40, 0), new Vector2(-8, 0)),
                $"{Text.Name(_tree, id)}  <size=12><color=#{Hex(Style.Dim)}>{stTxt} · {string.Join(", ", pages)}</color></size>",
                15, st == State.Done ? Style.Gold : Style.Steel, TextAlignmentOptions.MidlineLeft).textWrappingMode = TextWrappingModes.NoWrap;
            var b = r.gameObject.AddComponent<Button>(); b.onClick.AddListener(() => Pick(id));
        }

        static void Pick(string id)
        {
            ClearSearch();
            Jump(id);
        }

        // ---------- content ----------
        public static void Refresh()
        {
            _tree = Provider.Current;
            if (_themesFor != _tree) { _themes = _tree.Nodes.Values.Where(n => n.Reachable).ToDictionary(n => n.Id, n => Themes.Of(_tree, n)); _themesFor = _tree; }
            _prog = Progress.Compute(_tree);
            _view = View.Build(_tree, _prog, _theme, _themes);
            foreach (var th in Themes.All)
            {
                var b = _tabs[th.key]; SetBtn(b, th.key == _theme);
                int d = _themes.Count(kv => kv.Value.Contains(th.key) && _prog.IsDone(kv.Key)), tot = _themes.Count(kv => kv.Value.Contains(th.key));
                b.GetComponentInChildren<TMP_Text>().text = $"{Text.L(th.en, th.fr)} <size=12><color=#{Hex(Style.Dim)}>{d}/{tot}</color></size>";
            }
            if (!Plugin.DevMode.Value) Arranging = false;
            _arrangeBtn.gameObject.SetActive(Plugin.DevMode.Value);
            SetBtn(_arrangeBtn, Arranging); _resetBtn.gameObject.SetActive(Arranging);
            _count.text = $"{_prog.DoneCount} / {_prog.Total}";
            int avail = _view.Shown.Keys.Count(id => _prog.Of(id) == State.Available);
            _foot.text = Arranging
                ? Text.L("Arrange (dev): every node is shown; drag them into place. Saved to BepInEx/config/unlocktree.layout.json, shipped by deploy.sh.",
                         "Disposer (dev) : tout est affiché ; glisse les éléments. Enregistré dans BepInEx/config/unlocktree.layout.json, intégré par deploy.sh.")
                : Text.L($"{avail} available on this page · {_view.Done}/{_view.Total} done · tree {(_tree.Source == "live" ? "built from the game" : "shipped")}",
                         $"{avail} disponibles sur cette page · {_view.Done}/{_view.Total} faits · arbre {(_tree.Source == "live" ? "lu dans le jeu" : "intégré")}");
            if (_selected == null || !_view.Shown.ContainsKey(_selected))
                _selected = _view.Shown.Keys.Where(id => _prog.Of(id) == State.Available).OrderBy(id => _tree.Nodes[id].Depth).FirstOrDefault()
                            ?? _view.Roots.FirstOrDefault();
            Draw(!Arranging);
            ShowDetail(_selected);
        }

        // icon centre of a cell: high in the cell so the two-line label underneath has room
        const float IconY = 42;
        static Vector2 At((float c, float r) p) => new Vector2(p.c * Layout.CW + Layout.CW / 2 + 10, -(p.r * Layout.CH + IconY + 8));

        static void Draw(bool scrollToFrontier = false)
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Object.Destroy(_content.GetChild(i).gameObject);
            _pos = Layout.Place(_view);
            float maxC = _pos.Values.Select(p => p.c).DefaultIfEmpty(0).Max(), maxR = _pos.Values.Select(p => p.r).DefaultIfEmpty(0).Max();
            _content.sizeDelta = new Vector2((maxC + 1) * Layout.CW + 20, (maxR + 1) * Layout.CH + 24);
            var edges = Rect("edges", _content); edges.anchorMin = edges.anchorMax = edges.pivot = new Vector2(0, 1);
            var boxed = new HashSet<string>();
            foreach (var bl in Layout.Blocks)
            {
                if (!_pos.TryGetValue(bl.From, out var fp)) continue;
                boxed.UnionWith(bl.Ids);
                var tl = At((bl.C, bl.R)) + new Vector2(-Layout.CW / 2 + 4, IconY - 4);
                var box = Rect("block:" + bl.From, edges); box.anchorMin = box.anchorMax = new Vector2(0, 1); box.pivot = new Vector2(0, 1);
                box.anchoredPosition = tl; box.sizeDelta = new Vector2(bl.Cols * Layout.CW - 8, bl.Rows * Layout.CH - 8);
                var done = bl.Ids.All(_prog.IsDone);
                Img(box, done ? new Color(.85f, .65f, .3f, .55f) : new Color(.6f, .6f, .6f, .35f)).raycastTarget = false;          // rim
                Img(Place(Rect("fill", box), Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2)), new Color(.05f, .04f, .03f, .6f)).raycastTarget = false;
                var st = bl.Ids.All(_prog.IsDone) ? State.Done : _prog.IsDone(bl.From) ? State.Available : State.Locked;
                // the wire ends on the box's left border, half a cell before a normal child, so bend it just before the
                // border (in the gap after the parent's label) rather than halfway, which would cut through the label
                var boxLeft = At((bl.C, bl.R)) - new Vector2(Layout.CW / 2 - 4, 0);
                Edge(edges, At(fp), boxLeft, st, boxLeft.x - 4);
            }
            foreach (var kv in _view.Parent)
                if (!boxed.Contains(kv.Key) && _pos.TryGetValue(kv.Key, out var c) && _pos.TryGetValue(kv.Value, out var p)) Edge(edges, At(p), At(c), _prog.Of(kv.Key));
            foreach (var kv in _pos) NodeWidget(kv.Key, At(kv.Value));
            if (scrollToFrontier && _selected != null && _pos.TryGetValue(_selected, out var sp))
            {
                // bring the selected (first available) node into view, a little left of centre
                var at = At(sp); var vs = _viewport.rect.size;
                float x = Mathf.Clamp(at.x - vs.x * .35f, 0, Mathf.Max(0, _content.sizeDelta.x - vs.x));
                float y = Mathf.Clamp(-at.y - vs.y * .5f, 0, Mathf.Max(0, _content.sizeDelta.y - vs.y));
                _content.anchoredPosition = new Vector2(-x, y);
            }
        }

        static void Edge(RectTransform parent, Vector2 a, Vector2 b, State st, float? bendX = null)
        {
            var col = st == State.Done ? new Color(Style.Gold.r, Style.Gold.g, Style.Gold.b, .7f) : st == State.Available ? new Color(Style.Steel.r, Style.Steel.g, Style.Steel.b, .6f) : new Color(.35f, .35f, .35f, .7f);
            float mx = bendX ?? (a.x + b.x) / 2;
            void Seg(Vector2 p, Vector2 q)
            {
                var r = Rect("e", parent); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                float x0 = Mathf.Min(p.x, q.x), x1 = Mathf.Max(p.x, q.x), y0 = Mathf.Max(p.y, q.y), y1 = Mathf.Min(p.y, q.y);
                r.anchoredPosition = new Vector2(x0 - 1.5f, y0 + 1.5f); r.sizeDelta = new Vector2(x1 - x0 + 3, y0 - y1 + 3);
                Img(r, col).raycastTarget = false;
            }
            Seg(a, new Vector2(mx, a.y)); Seg(new Vector2(mx, a.y), new Vector2(mx, b.y)); Seg(new Vector2(mx, b.y), b);
        }

        static void NodeWidget(string id, Vector2 at)
        {
            var n = _tree.Get(id); var st = _prog.Of(id);
            var r = Rect(id, _content); r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = at; r.sizeDelta = new Vector2(60, 60);
            var frame = Img(r, st == State.Done ? new Color(.9f, .7f, .3f, 1) : st == State.Available ? new Color(.75f, .85f, .95f, 1) : new Color(.45f, .45f, .45f, .9f), Style.Slot);
            if (!Style.Slot) frame.color = st == State.Done ? new Color(.45f, .35f, .15f) : st == State.Available ? new Color(.2f, .27f, .33f) : new Color(.15f, .15f, .15f);
            var icon = Unknown(id) ? null : Icons.For(_tree, n);
            var ir = Place(Rect("icon", r), Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
            if (Unknown(id)) Txt(ir, "?", 30, Style.Dim, TextAlignmentOptions.Center, true);
            else if (icon) { var ii = Img(ir, st == State.Locked ? new Color(0, 0, 0, .85f) : Color.white); ii.sprite = icon; ii.type = Image.Type.Simple; ii.preserveAspect = true; ii.raycastTarget = false; }
            else Txt(ir, Glyph(n), 26, st == State.Locked ? Style.Dim : Style.Gold, TextAlignmentOptions.Center, true);
            if (_prog.Implied.Contains(id)) frame.color = new Color(.7f, .6f, .4f, .8f);
            var lab = Place(Rect("label", r), new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(-(Layout.CW / 2 - 4), -38), new Vector2(Layout.CW / 2 - 4, -3));
            var lt = Txt(lab, Unknown(id) ? "" : Text.Name(_tree, id), 13, st == State.Locked ? Style.Dim : st == State.Done ? Style.Gold : Style.Ink, TextAlignmentOptions.Top);
            lt.fontSize = 13; lt.enableAutoSizing = true; lt.fontSizeMin = 10; lt.fontSizeMax = 13; lt.lineSpacing = -12;
            if (n.Kind == "biome" || n.Kind == "boss") r.localScale = Vector3.one * 1.15f;
            if (id == _selected) Img(Place(Rect("sel", r), Vector2.zero, Vector2.one, new Vector2(-4, -4), new Vector2(4, 4)), new Color(1, 1, 1, .18f)).raycastTarget = false;
            var b = r.gameObject.AddComponent<Button>(); b.transition = Selectable.Transition.ColorTint;
            b.onClick.AddListener(() => { if (!Arranging) { _selected = id; Draw(); ShowDetail(id); } });
            if (Arranging) r.gameObject.AddComponent<Dragger>().Id = id;
        }

        // not discovered yet: shown as "?" to players; the dev sees everything while arranging
        static bool Unknown(string id) => !Arranging && _prog.Of(id) == State.Locked;
        static string Name(string id) => Unknown(id) ? "?" : Text.Name(_tree, id);
        static string Link(string id, string text) => $"<link=\"{id}\"><u>{text}</u></link>";

        /// Select a node from the detail panel: stay on this page if it's here, otherwise go to a page that has it.
        public static void Jump(string id)
        {
            if (_tree.Get(id) == null || !_themes.TryGetValue(id, out var ths)) return;
            if (!ths.Contains(_theme)) _theme = Themes.All.Select(t => t.key).First(ths.Contains);
            _selected = id;
            Refresh();
        }

        static string Glyph(Node n) => n.Kind == "biome" ? "◈" : n.Kind == "tool" ? "⚒" : n.Kind == "gate" ? "⛩" : n.Kind == "boss" ? "☠" : "?";

        // ---------- details ----------
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static void ShowDetail(string id)
        {
            var n = _tree.Get(id);
            if (n == null) { _detailText.text = ""; return; }
            var sb = new StringBuilder(); var st = _prog.Of(id);
            if (Unknown(id))
            {
                // like Minecraft: no spoilers, only which known steps lead there
                sb.Append($"<size=24><b>?</b></size>\n<color=#{Hex(Style.Dim)}>{Text.L("Not discovered yet", "Pas encore découvert")}</color>\n\n");
                sb.Append($"<b>{Text.L("Comes after", "Vient après")}</b>\n");
                foreach (var r in (n.Best?.Req ?? new List<string>()).Distinct())
                    sb.Append($"   <color=#{Hex(_prog.IsDone(r) ? Style.Gold : Style.Dim)}>{(_prog.IsDone(r) ? "✓" : "✗")} {Link(r, Name(r))}</color>\n");
                _detailText.text = sb.ToString();
                return;
            }
            sb.Append($"<size=24><b>{Text.Name(_tree, id)}</b></size>\n");
            var stTxt = _prog.Implied.Contains(id) ? Text.L("Done in the world", "Fait dans le monde")
                : st == State.Done ? Text.L("Done", "Fait") : st == State.Available ? Text.L("Available", "Disponible") : Text.L("Locked", "Verrouillé");
            sb.Append($"<color=#{Hex(st == State.Done ? Style.Gold : st == State.Available ? Style.Steel : Style.Dim)}>{stTxt}</color>\n");
            var desc = Text.Description(_tree, id);
            if (!string.IsNullOrEmpty(desc)) sb.Append($"<i><color=#{Hex(Style.Dim)}>{desc}</color></i>\n");
            var ways = n.Options.Where(o => o.Req.All(r => _tree.Get(r)?.Reachable == true))
                                .OrderBy(o => _prog.Done.Count > 0 && o.Req.All(_prog.IsDone) ? 0 : 1).ThenBy(o => o == n.Best ? 0 : 1).ToList();
            sb.Append($"\n<b>{(ways.Count == 1 ? Text.L("How to get it", "Comment l'obtenir") : Text.L($"{ways.Count} ways to get it", $"{ways.Count} façons de l'obtenir"))}</b>\n");
            foreach (var o in ways.Take(6))
            {
                bool ready = o.Req.All(_prog.IsDone);
                sb.Append($"<color=#{Hex(ready ? Style.Steel : Style.Ink)}>• {Text.Way(_tree, o)}</color>\n");
                var amounts = Text.Amounts(o);
                foreach (var r in o.Req)
                {
                    bool ok = _prog.IsDone(r);
                    sb.Append($"   <color=#{Hex(ok ? Style.Gold : Style.Dim)}>{(ok ? "✓" : "✗")} {Link(r, Name(r))}{(amounts.TryGetValue(r, out var a) ? " ×" + a : "")}</color>\n");
                }
            }
            if (ways.Count > 6) sb.Append($"<color=#{Hex(Style.Dim)}>{Text.L($"…and {ways.Count - 6} more", $"…et {ways.Count - 6} de plus")}</color>\n");
            var un = _tree.Unlocks.TryGetValue(id, out var u) ? u : new List<string>();
            sb.Append($"\n<b>{(un.Count == 0 ? Text.L("Nothing else needs this", "Rien d'autre n'en a besoin") : Text.L($"Unlocks {un.Count}", $"Débloque {un.Count}"))}</b>\n");
            foreach (var x in un.OrderBy(x => _tree.Nodes[x].Depth).Take(30))
                sb.Append($"<color=#{Hex(_prog.IsDone(x) ? Style.Gold : _prog.Of(x) == State.Available ? Style.Steel : Style.Dim)}>{Link(x, Name(x))}</color>   ");
            if (un.Count > 30) sb.Append(Text.L($"… +{un.Count - 30}", $"… +{un.Count - 30}"));
            _detailText.text = sb.ToString();
        }

        // clicking an underlined name in the detail panel selects that node
        class LinkClicks : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData e)
            {
                var t = GetComponent<TMP_Text>();
                int i = TMP_TextUtilities.FindIntersectingLink(t, e.position, e.pressEventCamera);
                if (i >= 0) Jump(t.textInfo.linkInfo[i].GetLinkID());
            }
        }

        // ---------- hand layout ----------
        // Dev arrange: dragging a node moves it with its whole branch; dragging a node of a grid moves the grid.
        class Dragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public string Id; Vector2 _grab; string _key;
            List<(RectTransform rt, Vector2 start)> _moving;
            public void OnBeginDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, e.position, e.pressEventCamera, out _grab);
                var block = Layout.Blocks.FirstOrDefault(b => b.Ids.Contains(Id));
                IEnumerable<string> ids;
                if (block != null) { _key = Layout.BlockKey(block.From); ids = block.Ids.SelectMany(Branch); }
                else { _key = Id; ids = Branch(Id); }
                var set = new HashSet<string>(ids);
                _moving = new List<(RectTransform, Vector2)>();
                foreach (Transform t in _content)
                    if (set.Contains(t.name) || (t.name == "block:" + (block?.From ?? "-"))) _moving.Add(((RectTransform)t, ((RectTransform)t).anchoredPosition));
                foreach (Transform t in _content.Find("edges"))
                    if (t.name == "block:" + (block?.From ?? "-") || set.Any(x => t.name == "block:" + x)) _moving.Add(((RectTransform)t, ((RectTransform)t).anchoredPosition));
                foreach (var (rt, _) in _moving) rt.SetAsLastSibling();
            }
            static IEnumerable<string> Branch(string id)
            {
                yield return id;
                if (_view.Kids.TryGetValue(id, out var ks)) foreach (var k in ks) foreach (var x in Branch(k)) yield return x;
            }
            public void OnDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, e.position, e.pressEventCamera, out var p);
                foreach (var (rt, start) in _moving) rt.anchoredPosition = start + (p - _grab);
            }
            public void OnEndDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, e.position, e.pressEventCamera, out var p);
                var d = p - _grab;
                float dc = Mathf.Round(d.x / Layout.CW * 2) / 2, dr = Mathf.Round(-d.y / Layout.CH * 2) / 2;   // half-cell steps
                if (dc != 0 || dr != 0) Layout.Move(_theme, _key, dc, dr);
                Draw();
            }
        }
    }

    static class Icons
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Sprite ItemIcon(string prefab)
        {
            var go = prefab != null ? ObjectDB.instance?.GetItemPrefab(prefab) : null;
            return go && go.GetComponent<ItemDrop>() is ItemDrop d ? d.m_itemData.GetIcon() : null;
        }
        static Sprite PieceIcon(string prefab)
        {
            var go = prefab != null ? ZNetScene.instance?.GetPrefab(prefab) : null;
            return go && go.GetComponent<Piece>() is Piece p ? p.m_icon : null;
        }
        public static Sprite For(Tree t, Node n)
        {
            if (Cache.TryGetValue(n.Id, out var s)) return s;
            switch (n.Kind)
            {
                case "item": s = ItemIcon(n.Id); break;
                case "piece": case "station": s = PieceIcon(n.Id); break;
                case "level": s = PieceIcon(n.Station); break;
                case "tool": s = ItemIcon(n.Options.Select(o => o.Req.FirstOrDefault()).Where(x => x != null).OrderBy(x => t.Get(x)?.Depth ?? 99).FirstOrDefault()); break;
                case "gate": s = ItemIcon(n.Options.SelectMany(o => o.Req).FirstOrDefault()); break;
                case "mob": case "boss":
                    var cd = ZNetScene.instance?.GetPrefab(n.Prefab)?.GetComponent<CharacterDrop>();
                    var drops = cd ? cd.m_drops.Where(d => d.m_prefab).Select(d => d.m_prefab.name).ToList() : new List<string>();
                    s = ItemIcon(drops.FirstOrDefault(d => d.StartsWith("Trophy")) ?? drops.FirstOrDefault());
                    break;
            }
            Cache[n.Id] = s;
            return s;
        }
    }
}
