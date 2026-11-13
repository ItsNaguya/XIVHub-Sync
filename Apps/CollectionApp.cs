using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Collections;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    /// <summary>
    /// Collection (3.0): the website's collection page in the tablet. Owned state comes from the game (refreshed every
    /// 10 s); FFXIV Collect adds patch, rarity and sources. Category, show, sort and the achievement type/category are
    /// shared with the website (state section collections.view), read every frame. Emotes, orchestrion, hairstyles,
    /// facewear and Triple Triad cards are plugin-only categories: picking one stays local.
    /// </summary>
    public partial class CollectionApp : IApp
    {
        public string Name => "Collection";
        public string Icon => FontAwesomeIcon.Book.ToIconString();
        public bool IsVisible { get; set; } = false;
        public bool LargeText => true;

        private readonly HubClient _sender;
        private readonly CollectionService _collectionService;
        private readonly ITextureProvider _textureProvider;

        private sealed record CatInfo(CollectionCategory Cat, string Label, string? Web, string? Collect, FontAwesomeIcon Glyph);
        private static readonly CatInfo[] Cats =
        {
            new(CollectionCategory.Mounts, "Mounts", "mounts", "mounts", FontAwesomeIcon.Horse),
            new(CollectionCategory.Minions, "Minions", "minions", "minions", FontAwesomeIcon.Paw),
            new(CollectionCategory.Achievements, "Achievements", "achievements", "achievements", FontAwesomeIcon.Trophy),
            new(CollectionCategory.Emotes, "Emotes", null, "emotes", FontAwesomeIcon.Smile),
            new(CollectionCategory.Orchestrions, "Orchestrion", null, "orchestrions", FontAwesomeIcon.Music),
            new(CollectionCategory.Hairstyles, "Hairstyles", null, "hairstyles", FontAwesomeIcon.Cut),
            new(CollectionCategory.Facewear, "Facewear", null, "facewear", FontAwesomeIcon.Glasses),
            new(CollectionCategory.TriadCards, "Triad cards", null, "triad/cards", FontAwesomeIcon.Clone),
        };
        private static CatInfo CatDef(CollectionCategory c) => Array.Find(Cats, x => x.Cat == c) ?? Cats[0];

        private CollectionCategory _cat = CollectionCategory.Mounts;
        private readonly Dictionary<CollectionCategory, string> _sub = new(); // expansion chip per category (plugin only)
        private string _query = "";

        public CollectionApp(HubClient sender, CollectionService collectionService, ITextureProvider textureProvider)
        {
            _sender = sender;
            _collectionService = collectionService;
            _textureProvider = textureProvider;
        }

        public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }
        public void Dispose() { }

        // =============================================================================================================
        // Sync with the website: collections.view { cat, sort, show, achType, achCat }
        // =============================================================================================================
        private const string SecView = "collections.view";

        private sealed class ViewState
        {
            public string cat { get; set; } = "mounts";
            public string sort { get; set; } = "patch";
            public string show { get; set; } = "all";
            public string? achType { get; set; }
            public string? achCat { get; set; }
            [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
        }

        private JToken? _viewTok;
        private bool _viewRead;
        private ViewState _view = new();
        private string? _lastWebCat;

        private void SyncIn()
        {
            var t = _sender.GetState(SecView);
            if (_viewRead && ReferenceEquals(t, _viewTok)) return;
            _viewRead = true;
            _viewTok = t;
            ViewState? v = null;
            if (t != null && t.Type == JTokenType.Object) try { v = t.ToObject<ViewState>(); } catch { }
            _view = v ?? new ViewState();
            _view.sort = _view.sort is "patch" or "alpha" or "rare" ? _view.sort : "patch";
            _view.show = _view.show is "all" or "owned" or "missing" ? _view.show : "all";
            // follow the website's category when it changes there (a plugin-only category stays open otherwise)
            if (_view.cat != _lastWebCat)
            {
                _lastWebCat = _view.cat;
                var c = Array.Find(Cats, x => x.Web == _view.cat);
                if (c != null) _cat = c.Cat;
            }
        }

        private void SetView(Action<ViewState> change)
        {
            var v = new ViewState { cat = _view.cat, sort = _view.sort, show = _view.show, achType = _view.achType, achCat = _view.achCat, extra = _view.extra };
            change(v);
            _view = v;
            _lastWebCat = v.cat;
            var obj = new Dictionary<string, object?> { ["cat"] = v.cat, ["sort"] = v.sort, ["show"] = v.show, ["achType"] = v.achType, ["achCat"] = v.achCat };
            if (v.extra != null) foreach (var (k, val) in v.extra) obj.TryAdd(k, val);
            _sender.SetState(SecView, obj);
            _viewTok = _sender.GetState(SecView);
        }

        private void PickCategory(CollectionCategory c)
        {
            _cat = c;
            var web = CatDef(c).Web;
            if (web != null && web != _view.cat) SetView(v => v.cat = web);
        }

        // =============================================================================================================
        // Data (counts and the achievement tree are rebuilt when the service swaps its lists, every 10 s)
        // =============================================================================================================
        private sealed class Stat { public int Owned, Total, Points, PointsTotal; }
        private sealed class AchType { public int Owned, Total; public Dictionary<string, (int Owned, int Total)> Cats = new(); public List<string> Order = new(); }

        private readonly Dictionary<CollectionCategory, (List<CollectionItem> List, Stat Stat)> _stats = new();
        private List<CollectionItem>? _treeList;
        private Dictionary<string, AchType> _tree = new();
        private List<string> _treeOrder = new();

        private Stat StatOf(CollectionCategory c)
        {
            var list = _collectionService.GetItems(c);
            if (_stats.TryGetValue(c, out var s) && ReferenceEquals(s.List, list)) return s.Stat;
            var st = new Stat { Total = list.Count };
            foreach (var it in list)
            {
                st.PointsTotal += it.Points;
                if (it.IsUnlocked) { st.Owned++; st.Points += it.Points; }
            }
            _stats[c] = (list, st);
            return st;
        }

        private void BuildTree()
        {
            var list = _collectionService.GetItems(CollectionCategory.Achievements);
            if (ReferenceEquals(list, _treeList)) return;
            _treeList = list;
            var types = new Dictionary<string, AchType>();
            foreach (var it in list)
            {
                if (!types.TryGetValue(it.Group, out var T)) types[it.Group] = T = new AchType();
                int o = it.IsUnlocked ? 1 : 0;
                T.Total++; T.Owned += o;
                T.Cats.TryGetValue(it.Subcategory, out var C);
                T.Cats[it.Subcategory] = (C.Owned + o, C.Total + 1);
            }
            foreach (var T in types.Values) T.Order = T.Cats.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            _tree = types;
            _treeOrder = types.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private string? AchTypeNow => _view.achType != null && _tree.ContainsKey(_view.achType) ? _view.achType : _treeOrder.FirstOrDefault();
        private string? AchCatNow
        {
            get
            {
                var t = AchTypeNow;
                if (t == null) return null;
                var order = _tree[t].Order;
                return _view.achCat != null && order.Contains(_view.achCat) ? _view.achCat : order.FirstOrDefault();
            }
        }

        /// <summary>Expansion for mounts, minions and orchestrion rolls: from FFXIV Collect's patch, else the game id range.</summary>
        private string SubOf(CollectionItem it)
        {
            if (it.Category is CollectionCategory.Mounts or CollectionCategory.Minions or CollectionCategory.Orchestrions)
            {
                var exp = ExpansionOfPatch(Info(it)?.Patch ?? "");
                if (exp.Length > 0) return exp;
            }
            return it.Subcategory ?? "";
        }

        // the filtered, sorted list is cached until something it depends on changes
        private (object? List, int Info, string Key) _shownKey;
        private List<CollectionItem> _shown = new();

        private List<CollectionItem> Shown()
        {
            var list = _collectionService.GetItems(_cat);
            string key = $"{_cat}|{_query}|{_view.show}|{_view.sort}|{SubSel}|{AchTypeNow}|{AchCatNow}";
            if (ReferenceEquals(_shownKey.List, list) && _shownKey.Info == _infoVersion && _shownKey.Key == key) return _shown;
            _shownKey = (list, _infoVersion, key);

            var q = Norm(_query);
            bool ach = _cat == CollectionCategory.Achievements;
            string? type = AchTypeNow, cat = AchCatNow, sub = SubSel;
            IEnumerable<CollectionItem> res = list.Where(it => _view.show == "all" || (_view.show == "owned") == it.IsUnlocked);
            if (ach)
                res = res.Where(it => q.Length > 0
                    ? Norm(it.Name).Contains(q) || Norm(it.Description).Contains(q)   // a search looks through every achievement
                    : it.Group == type && it.Subcategory == cat);
            else
                res = res.Where(it => (sub == "All" || SubOf(it) == sub) && (q.Length == 0 || Norm(it.Name).Contains(q)
                    || (it.Sources ?? Array.Empty<string>()).Any(s => Norm(s).Contains(q))
                    || (Info(it)?.Sources ?? Array.Empty<string>()).Any(s => Norm(s).Contains(q))));

            res = _view.sort switch
            {
                "alpha" => res.OrderBy(it => it.Name, StringComparer.OrdinalIgnoreCase),
                "rare" => res.OrderBy(it => Info(it)?.OwnedPct ?? 100f).ThenBy(it => it.Name, StringComparer.OrdinalIgnoreCase),
                _ => res.OrderByDescending(it => PatchNum(Info(it)?.Patch ?? "")).ThenByDescending(it => it.Id),
            };
            _shown = res.ToList();
            return _shown;
        }

        private string SubSel => _sub.TryGetValue(_cat, out var s) ? s : "All";

        // =============================================================================================================
        // Draw
        // =============================================================================================================
        public void Draw()
        {
            SyncIn();
            PumpInfo();
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("co_main", avail))
            {
                float w = avail.X - S(10);
                var p = ImGui.GetCursorScreenPos();
                float y = p.Y;
                y = DrawHead(new Vector2(p.X, y), w) + S(10);
                y = DrawCats(new Vector2(p.X, y), w) + S(10);
                y = DrawControls(new Vector2(p.X, y), w) + S(10);
                y = DrawBody(new Vector2(p.X, y), w) + S(10);
                var foot = "Owned state from the game, refreshed every 10 seconds. Patch, rarity and sources from FFXIV Collect.";
                Ui.Text(Ui.Dl, new Vector2(p.X, y), Fit(foot, w, 0.8f), Theme.Text3, 0.8f);
                y += LH + S(6);
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private float DrawHead(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            var def = CatDef(_cat);
            var st = StatOf(_cat);
            float titleH = Ui.TextSize("Ag", 1.3f).Y, pad = S(14);
            float h = pad + titleH + S(8) + TagH() + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h));

            // title with the website's gem
            var gem = p + new Vector2(pad + S(6), pad + titleH / 2);
            float g = S(6);
            dl.AddQuadFilled(gem + new Vector2(0, -g), gem + new Vector2(g, 0), gem + new Vector2(0, g), gem + new Vector2(-g, 0), Ui.Accent.U32());
            float x = p.X + pad + S(20);
            Ui.Text(dl, new Vector2(x, p.Y + pad), "Collections", Theme.Text, 1.3f);
            x += Ui.TextSize("Collections", 1.3f).X + S(10);
            Ui.Text(dl, new Vector2(x, p.Y + pad + titleH - LH - S(1)), "from the game", Theme.Text3);

            // refresh now
            float b = S(34);
            if (Ui.IconButton("co_refresh", FontAwesomeIcon.SyncAlt, new Vector2(p.X + w - pad - b, p.Y + pad), b, "Read the collections from the game again now"))
            {
                _collectionService.RefreshCache();
            }

            // meta row: source, count, points, FFXIV Collect state
            var m = new Vector2(p.X + pad, p.Y + pad + titleH + S(8));
            m.X += TipTag("co_live", m, "LIVE", Theme.Live, $"{def.Label} read from the game right now (every 10 seconds).") + S(8);
            string count = $"{st.Owned:N0} / {st.Total:N0}";
            Ui.Text(dl, m + new Vector2(0, S(2)), count, Theme.Text, 0.95f);
            m.X += Ui.TextSize(count, 0.95f).X + S(6);
            string rest = $"{def.Label.ToLowerInvariant()} · {Pct(st.Owned, st.Total)}%";
            Ui.Text(dl, m + new Vector2(0, S(3)), rest, Theme.Text3, 0.9f);
            m.X += Ui.TextSize(rest, 0.9f).X + S(12);
            if (_cat == CollectionCategory.Achievements)
                m.X += TipTag("co_pts", m, $"{st.Points:N0} pts", Theme.Gold, $"Counted from the completed achievements, of {st.PointsTotal:N0} in the game.") + S(8);
            if (_infoLoading == _cat)
                TipTag("co_info", m, "Loading patch data...", Theme.Text2, "Patch, rarity and sources come from FFXIV Collect (the website's lists).");
            else if (InfoFailed(_cat))
                TipTag("co_info", m, "No patch data", Theme.Warn, (_infoError.Length > 0 ? _infoError + "\n" : "") + "Sorting by patch and rarity falls back to the game order. Retries in 5 minutes.");
            return p.Y + h;
        }

        private float DrawCats(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float gap = S(8);
            int cols = Math.Clamp((int)((w + gap) / (S(150) + gap)), 2, Cats.Length);
            int rows = (Cats.Length + cols - 1) / cols;
            float tw = (w - gap * (cols - 1)) / cols, th = LH + S(26);
            for (int i = 0; i < Cats.Length; i++)
            {
                var c = Cats[i];
                var st = StatOf(c.Cat);
                var a = p + new Vector2((i % cols) * (tw + gap), (i / cols) * (th + gap));
                bool active = c.Cat == _cat;
                bool clicked = Ui.Hit("co_cat" + i, a, new Vector2(tw, th), out float h);
                Ui.Tooltip($"{c.Label}: {st.Owned:N0} of {st.Total:N0}, from the game" + (c.Web == null ? "\nOnly in the plugin (the website shows mounts, minions and achievements)." : ""));
                if (clicked && !active) PickCategory(c.Cat);
                Ui.Panel(dl, a, a + new Vector2(tw, th), active ? Ui.Accent.With(0.16f + 0.08f * h) : Theme.Night2.With(0.6f + 0.2f * h),
                    active ? Ui.Accent.With(0.7f) : Theme.Text3.With(0.2f + 0.3f * h), 6f);
                var col = active ? Theme.Text : UIHelper.LerpColor(Theme.Text2, Theme.Text, h);
                float ix = a.X + S(10), iy = a.Y + S(8);
                Ui.Icon(dl, new Vector2(ix, iy + S(1)), c.Glyph, active ? Ui.Accent : Theme.Text3, 0.9f);
                ix += S(22);
                string n = st.Total > 0 ? $"{st.Owned}/{st.Total}" : "...";
                float nw = Ui.TextSize(n, 0.82f).X;
                Ui.Text(dl, new Vector2(ix, iy), Fit(c.Label, a.X + tw - S(10) - nw - S(6) - ix), col);
                Ui.Text(dl, new Vector2(a.X + tw - S(10) - nw, iy + S(2)), n, Theme.Text3, 0.82f);
                Ui.Progress(dl, new Vector2(a.X + S(10), a.Y + th - S(10)), tw - S(20), st.Total > 0 ? (float)st.Owned / st.Total : 0, active ? Ui.Accent : Theme.Crystal.With(0.6f), 3f);
            }
            return p.Y + rows * th + (rows - 1) * gap;
        }

        private float DrawControls(Vector2 p, float w)
        {
            float gap = S(8), fh = FrameH;
            float segW = Math.Min(S(270), w), sortW = S(200);
            bool oneRow = w >= S(640);
            float searchW = oneRow ? w - segW - sortW - gap * 2 : w;

            string hint = _cat == CollectionCategory.Achievements ? "Search all achievements..." : $"Search {CatDef(_cat).Label.ToLowerInvariant()} or sources...";
            Input("##co_q", ref _query, hint, searchW, p);
            if (_query.Length > 0 && Ui.IconButton("co_qx", FontAwesomeIcon.Times, p + new Vector2(searchW - fh, 0), fh, "Clear search")) _query = "";

            var r = oneRow ? new Vector2(p.X + searchW + gap, p.Y) : new Vector2(p.X, p.Y + fh + gap);
            string[] shows = { "all", "owned", "missing" }, labels = { "All", "Owned", "Missing" };
            float bw = (segW - S(4) * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                var s = shows[i];
                if (Ui.Button("co_show" + s, labels[i], r + new Vector2(i * (bw + S(4)), 0), new Vector2(bw, fh), _view.show == s ? Kind.Primary : Kind.Ghost) && _view.show != s)
                    SetView(v => v.show = s);
            }
            var sp = new Vector2(r.X + segW + gap, r.Y);
            if (!oneRow && sp.X + sortW > p.X + w) sp = new Vector2(p.X, r.Y + fh + gap);
            var pick = Combo("##co_sort", _view.sort, new[] { ("patch", "Newest patch first"), ("alpha", "A to Z"), ("rare", "Rarest first") }, sortW, sp,
                InfoLoaded(_cat) ? "" : "Patch and rarity come from FFXIV Collect; until they load, the game order is used.");
            if (pick != null) SetView(v => v.sort = pick);
            float bottom = Math.Max(sp.Y, r.Y) + fh;

            // expansion chips (mounts, minions, orchestrion)
            if (_cat != CollectionCategory.Achievements)
            {
                var list = _collectionService.GetItems(_cat);
                var subs = list.GroupBy(SubOf).Where(g => g.Key.Length > 0)
                    .Select(g => (Name: g.Key, Owned: g.Count(x => x.IsUnlocked), Total: g.Count()))
                    .OrderBy(g => Array.IndexOf(Expansions, g.Name) is var i && i >= 0 ? i : 99).ThenBy(g => g.Name).ToList();
                if (subs.Count > 1)
                {
                    var at = new Vector2(p.X, bottom + gap);
                    var sel = SubSel;
                    if (sel != "All" && !subs.Any(s => s.Name == sel)) { _sub.Remove(_cat); sel = "All"; }
                    if (ChipToggle("co_sub_all", "All", sel == "All", ref at, p.X, p.X + w)) _sub.Remove(_cat);
                    foreach (var s in subs)
                        if (ChipToggle("co_sub_" + s.Name, $"{s.Name}  {s.Owned}/{s.Total}", sel == s.Name, ref at, p.X, p.X + w)) _sub[_cat] = s.Name;
                    bottom = at.Y + ChipH();
                }
            }
            return bottom;
        }

        private float DrawBody(Vector2 p, float w)
        {
            float y = p.Y;
            var st = StatOf(_cat);
            if (_cat == CollectionCategory.Achievements && st.Owned == 0 && st.Total > 0)
                y += Message(new Vector2(p.X, y), w, "To load your achievements, open the Achievements window in game once per session. The game only loads them when asked.", Theme.Warn) + S(10);

            if (_cat != CollectionCategory.Achievements) return DrawGrid(new Vector2(p.X, y), w);

            BuildTree();
            if (w >= S(720))
            {
                float side = S(250), gap = S(12);
                float treeBottom = DrawTree(new Vector2(p.X, y), side);
                float listBottom = DrawAchList(new Vector2(p.X + side + gap, y), w - side - gap);
                return Math.Max(treeBottom, listBottom);
            }
            // narrow: the same tree as two pickers
            var type = AchTypeNow;
            if (type != null)
            {
                float cw = (w - S(8)) / 2;
                var tp = Combo("##co_atype", type, _treeOrder.Select(t => (t, $"{t} ({Pct(_tree[t].Owned, _tree[t].Total)}%)")).ToArray(), cw, new Vector2(p.X, y));
                if (tp != null) SetView(v => { v.achType = tp; v.achCat = null; });
                var cat = AchCatNow ?? "";
                var T = _tree[type];
                var cp = Combo("##co_acat", cat, T.Order.Select(c => (c, $"{c} ({T.Cats[c].Owned}/{T.Cats[c].Total})")).ToArray(), cw, new Vector2(p.X + cw + S(8), y));
                if (cp != null) SetView(v => { v.achType = type; v.achCat = cp; });
                y += FrameH + S(10);
            }
            return DrawAchList(new Vector2(p.X, y), w);
        }

        private float DrawTree(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            string? openType = AchTypeNow, openCat = AchCatNow;
            float rowH = LH + S(12), subH = LH + S(6), pad = S(10), capsH = Ui.TextSize("A", 0.78f).Y;
            float h = pad + capsH + S(8) + _treeOrder.Count * rowH + (openType != null ? _tree[openType].Order.Count * subH : 0) + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h));
            Ui.Caps(dl, p + new Vector2(pad, pad), "Types");
            float y = p.Y + pad + capsH + S(8);
            foreach (var type in _treeOrder)
            {
                var T = _tree[type];
                bool open = type == openType;
                var a = new Vector2(p.X + S(4), y);
                var size = new Vector2(w - S(8), rowH);
                if (Ui.Hit("co_t_" + type, a, size, out float hv) && !open) SetView(v => { v.achType = type; v.achCat = null; });
                if (open || hv > 0.01f) dl.AddRectFilled(a, a + size, (open ? Ui.Accent.With(0.16f) : Theme.Text.With(0.06f * hv)).U32(), S(5));
                string pct = $"{Pct(T.Owned, T.Total)}%";
                float pw = Ui.TextSize(pct, 0.82f).X;
                Ui.Text(dl, a + new Vector2(S(8), S(6)), Fit(type, size.X - pw - S(22)), open ? Theme.Text : Theme.Text2);
                Ui.Text(dl, a + new Vector2(size.X - S(8) - pw, S(7)), pct, Theme.Text3, 0.82f);
                y += rowH;
                if (!open) continue;
                foreach (var c in T.Order)
                {
                    var (o, t) = T.Cats[c];
                    bool sel = c == openCat;
                    var sa = new Vector2(p.X + S(16), y);
                    var ss = new Vector2(w - S(20), subH);
                    if (Ui.Hit("co_c_" + type + "_" + c, sa, ss, out float sh) && !sel) SetView(v => { v.achType = type; v.achCat = c; });
                    if (sel) dl.AddRectFilled(sa, sa + new Vector2(S(3), ss.Y), Ui.Accent.U32());
                    else if (sh > 0.01f) dl.AddRectFilled(sa, sa + ss, Theme.Text.With(0.05f * sh).U32(), S(4));
                    string n = $"{o}/{t}";
                    float nw = Ui.TextSize(n, 0.8f).X;
                    var col = o == t ? Theme.Live : sel ? Theme.Text : UIHelper.LerpColor(Theme.Text2, Theme.Text, sh);
                    Ui.Text(dl, sa + new Vector2(S(10), S(3)), Fit(c, ss.X - nw - S(22), 0.9f), col, 0.9f);
                    Ui.Text(dl, sa + new Vector2(ss.X - S(6) - nw, S(4)), n, Theme.Text3, 0.8f);
                    y += subH;
                }
            }
            return p.Y + h;
        }

        private float DrawAchList(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float y = p.Y;
            var shown = Shown();
            string head = _query.Trim().Length > 0 ? "Search results" : AchCatNow ?? "";
            string aside = _query.Trim().Length > 0 ? "in all achievements" : AchTypeNow != null ? $"in {AchTypeNow}" : "";
            Ui.Text(dl, new Vector2(p.X, y), Fit(head, w * 0.6f, 1.1f), Theme.Text, 1.1f);
            float hx = p.X + Ui.TextSize(Fit(head, w * 0.6f, 1.1f), 1.1f).X + S(8);
            Ui.Text(dl, new Vector2(hx, y + S(2)), $"{aside}  ·  {shown.Count}", Theme.Text3, 0.9f);
            y += Ui.TextSize("Ag", 1.1f).Y + S(8);
            return DrawItems(new Vector2(p.X, y), w, shown);
        }

        private float DrawGrid(Vector2 p, float w) => DrawItems(p, w, Shown());

        private float DrawItems(Vector2 p, float w, List<CollectionItem> shown)
        {
            if (_collectionService.GetItems(_cat).Count == 0)
            {
                Empty(p, w, "Nothing to show yet: log in to a character in game.", FontAwesomeIcon.Sync);
                return p.Y + EmptyH;
            }
            if (shown.Count == 0)
            {
                var what = _query.Trim().Length > 0 ? $" matching \"{_query.Trim()}\"" : "";
                var only = _view.show != "all" ? $" ({_view.show} only)" : "";
                Empty(p, w, $"Nothing here{what}{only}.", FontAwesomeIcon.Search);
                return p.Y + EmptyH;
            }
            float gap = S(8), minW = S(250);
            int cols = Math.Max(1, (int)((w + gap) / (minW + gap)));
            float cw = (w - gap * (cols - 1)) / cols, ch = Math.Max(S(40), LH + Ui.TextSize("Ag", 0.82f).Y + S(2)) + S(16);
            int rows = (shown.Count + cols - 1) / cols;
            for (int r = 0; r < rows; r++)
            {
                float y = p.Y + r * (ch + gap);
                if (!Visible(y, ch)) continue;
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= shown.Count) break;
                    DrawItem(shown[i], new Vector2(p.X + c * (cw + gap), y), new Vector2(cw, ch));
                }
            }
            return p.Y + rows * ch + (rows - 1) * gap;
        }

        private void DrawItem(CollectionItem it, Vector2 a, Vector2 size)
        {
            var dl = Ui.Dl;
            bool own = it.IsUnlocked;
            var info = Info(it);
            bool hovered = Ui.Hit($"co_it_{it.Category}_{it.Id}", a, size, out float h);
            if (ImGui.IsItemHovered()) DrawItemTooltip(it, info);
            _ = hovered;

            Ui.Panel(dl, a, a + size, own ? Theme.Night2.With(0.85f + 0.1f * h) : Theme.Night1.With(0.55f + 0.2f * h),
                own ? Ui.Accent.With(0.35f + 0.3f * h) : Theme.Text3.With(0.15f + 0.25f * h), 7f);
            float icon = S(40);
            var ip = a + new Vector2(S(8), (size.Y - icon) / 2);
            GameIcon(it.IconId, ip, icon, own ? 1f : 0.4f);
            if (own)
            {
                var cc = ip + new Vector2(icon - S(3), icon - S(3));
                dl.AddCircleFilled(cc, S(7), Theme.Live.U32());
                Ui.IconCentered(dl, cc, FontAwesomeIcon.Check.ToIconString(), Theme.Night0, 0.5f);
            }

            float tx = ip.X + icon + S(10), right = a.X + size.X - S(10);
            if (it.Category == CollectionCategory.Achievements && it.Points > 0)
            {
                string pts = it.Points.ToString();
                float pw = Ui.TextSize(pts, 0.85f).X;
                Ui.Text(dl, new Vector2(right - pw, a.Y + (size.Y - Ui.TextSize(pts, 0.85f).Y) / 2), pts, own ? Theme.Gold : Theme.GoldDeep, 0.85f);
                right -= pw + S(8);
            }
            float ty = a.Y + (size.Y - LH - Ui.TextSize("Ag", 0.82f).Y - S(2)) / 2;
            Ui.Text(dl, new Vector2(tx, ty), Fit(it.Name, right - tx), own ? Theme.Text : Theme.Text2);
            string sub = it.Category == CollectionCategory.Achievements ? it.Description
                : info?.Sources.FirstOrDefault() ?? it.Sources?.FirstOrDefault() ?? (string.IsNullOrEmpty(info?.Patch) ? SubOf(it) : $"Patch {info!.Patch}");
            Ui.Text(dl, new Vector2(tx, ty + LH + S(2)), Fit((sub ?? "").Replace('\n', ' '), right - tx, 0.82f), Theme.Text3, 0.82f);
        }

        private static void DrawItemTooltip(CollectionItem it, CollectInfo? info)
        {
            UIHelper.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextColored(it.IsUnlocked ? Theme.Crystal : Theme.Text, it.Name);
            var meta = new[] { string.IsNullOrEmpty(info?.Patch) ? "" : $"Patch {info!.Patch}", string.IsNullOrEmpty(info?.Owned) ? "" : $"{info!.Owned} of players have it", it.Points > 0 ? $"{it.Points} points" : "" }
                .Where(s => s.Length > 0).ToArray();
            if (meta.Length > 0) ImGui.TextColored(Theme.Text2, string.Join(" · ", meta));
            if (it.Category == CollectionCategory.Achievements) ImGui.TextColored(Theme.Gold, $"{it.Group} › {it.Subcategory}");
            else if (!string.IsNullOrEmpty(it.Subcategory)) ImGui.TextColored(Theme.Gold, it.Subcategory);
            if (!string.IsNullOrEmpty(info?.Extra)) ImGui.TextColored(Theme.Text2, info!.Extra);
            var desc = !string.IsNullOrEmpty(it.Description) ? it.Description : info?.Description ?? "";
            if (desc.Length > 0)
            {
                ImGui.Separator();
                ImGui.TextUnformatted(desc.Length > 320 ? desc[..319] + "…" : desc);
            }
            var sources = (info?.Sources ?? Array.Empty<string>()).Concat(it.Sources ?? Array.Empty<string>()).Distinct().ToArray();
            if (sources.Length > 0)
            {
                ImGui.Separator();
                ImGui.TextColored(Theme.Warn, "How to get it:");
                foreach (var s in sources.Take(8)) ImGui.TextUnformatted("- " + s);
            }
            ImGui.Separator();
            if (it.IsUnlocked) ImGui.TextColored(Theme.Live, "Owned");
            else ImGui.TextColored(Theme.Bad, "Not owned yet");
            ImGui.PopTextWrapPos();
            UIHelper.EndTooltip();
        }
    }
}
