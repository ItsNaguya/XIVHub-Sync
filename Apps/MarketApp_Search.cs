using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Search tab (item search + category browser) and Favourites tab
    public partial class MarketApp
    {
        private string _query = "";
        private int _showResults; // 0 nothing, 1 search results, 2 category items
        private bool _showCategories = true;
        private readonly Dictionary<int, int> _quickQty = new();

        private static readonly (int Id, string Label)[] CategoryGroups = { (1, "Weapons & tools"), (2, "Armor & accessories"), (3, "Items"), (4, "Housing") };

        private static readonly (string Group, string[] Jobs)[] JobGroups =
        {
            ("Tanks", new[] { "PLD", "WAR", "DRK", "GNB" }),
            ("Healers", new[] { "WHM", "SCH", "AST", "SGE" }),
            ("Melee DPS", new[] { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR" }),
            ("Physical ranged", new[] { "BRD", "MCH", "DNC" }),
            ("Magical ranged", new[] { "BLM", "SMN", "RDM", "PCT", "BLU" }),
            ("Crafters", new[] { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" }),
            ("Gatherers", new[] { "MIN", "BTN", "FSH" }),
        };

        private static readonly Dictionary<string, string> JobNames = new()
        {
            { "PLD", "Paladin" }, { "WAR", "Warrior" }, { "DRK", "Dark Knight" }, { "GNB", "Gunbreaker" },
            { "WHM", "White Mage" }, { "SCH", "Scholar" }, { "AST", "Astrologian" }, { "SGE", "Sage" },
            { "MNK", "Monk" }, { "DRG", "Dragoon" }, { "NIN", "Ninja" }, { "SAM", "Samurai" }, { "RPR", "Reaper" }, { "VPR", "Viper" },
            { "BRD", "Bard" }, { "MCH", "Machinist" }, { "DNC", "Dancer" },
            { "BLM", "Black Mage" }, { "SMN", "Summoner" }, { "RDM", "Red Mage" }, { "PCT", "Pictomancer" }, { "BLU", "Blue Mage" },
            { "CRP", "Carpenter" }, { "BSM", "Blacksmith" }, { "ARM", "Armorer" }, { "GSM", "Goldsmith" },
            { "LTW", "Leatherworker" }, { "WVR", "Weaver" }, { "ALC", "Alchemist" }, { "CUL", "Culinarian" },
            { "MIN", "Miner" }, { "BTN", "Botanist" }, { "FSH", "Fisher" },
        };

        // =============================================================================================================
        private void DrawSearch()
        {
            EnsureCategories();
            var top = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            bool wide = avail.X >= S(760);

            if (wide)
            {
                float lw = Math.Clamp(avail.X * 0.34f, S(300), S(400));
                // left: search box + categories
                DrawSearchBox(top, lw);
                float y = top.Y + FrameH + S(10);
                ImGui.SetCursorScreenPos(new Vector2(top.X, y));
                float catH = top.Y + avail.Y - y;
                if (UIHelper.BeginSmoothChild("mk_cats", new Vector2(lw, catH), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)) DrawCategoryGroups(lw, catH);
                ImGui.EndChild();
                // right: results
                float rx = top.X + lw + S(14), rw = avail.X - lw - S(14);
                float hy = DrawResultsHead(new Vector2(rx, top.Y), rw);
                ImGui.SetCursorScreenPos(new Vector2(rx, hy));
                if (UIHelper.BeginSmoothChild("mk_results", new Vector2(rw, top.Y + avail.Y - hy))) DrawResults(rw - S(10));
                ImGui.EndChild();
            }
            else
            {
                DrawSearchBox(top, avail.X);
                float y = top.Y + FrameH + S(10);
                ImGui.SetCursorScreenPos(new Vector2(top.X, y));
                if (UIHelper.BeginSmoothChild("mk_search_narrow", new Vector2(avail.X, top.Y + avail.Y - y)))
                {
                    float w = avail.X - S(10);
                    var p = ImGui.GetCursorScreenPos();
                    if (Ui.Button("mk_cattoggle", _showCategories ? "Hide categories" : "Browse categories", p, new Vector2(w, S(30)), Kind.Ghost,
                            _showCategories ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown))
                        _showCategories = !_showCategories;
                    Advance(p, S(38));
                    if (_showCategories) DrawCategoryGroups(w);
                    var hp = ImGui.GetCursorScreenPos();
                    float hy = DrawResultsHead(hp, w);
                    Advance(hp, hy - hp.Y);
                    DrawResults(w);
                }
                ImGui.EndChild();
            }
        }

        private void DrawSearchBox(Vector2 p, float w)
        {
            float bw = S(96);
            bool enter = Input("##mk_query", ref _query, "Item name, e.g. Sandalwood Log", w - bw - S(8), p, 200, ImGuiInputTextFlags.EnterReturnsTrue);
            if (Ui.Button("mk_go", _isSearching ? "..." : "Search", p + new Vector2(w - bw, 0), new Vector2(bw, FrameH), Kind.Primary, FontAwesomeIcon.Search) || enter)
            {
                RunSearch(_query);
                if (_query.Trim().Length > 0) _showCategories = false;
            }
        }

        private void DrawCategoryGroups(float w, float fitH = 0)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            float y = p.Y;
            if (_loadingCategories) { Ui.Text(dl, p, "Loading categories...", Theme.Text3); Advance(p, LH + S(8)); return; }
            if (_categories.Count == 0)
            {
                Empty(p, w, "No categories (the website did not answer).");
                if (Ui.Button("mk_cat_retry", "Try again", new Vector2(p.X, p.Y + EmptyH + S(6)), new Vector2(S(110), S(28)))) _categoriesTried = false;
                Advance(p, EmptyH + S(40));
                return;
            }

            float gap = S(4), pad = S(10), capsH = Ui.TextSize("A", 0.78f).Y;
            var groups = CategoryGroups.Select(g => (g.Id, g.Label, cats: _categories.Where(c => c.category == g.Id).ToList())).Where(g => g.cats.Count > 0).ToList();
            float GroupH(float c, int n) { int pr = Math.Max(1, (int)((w - pad * 2 + gap) / (c + gap))); int rw = (n + pr - 1) / pr; return pad + capsH + S(6) + rw * (c + gap) - gap + pad; }
            float TotalH(float c) => groups.Sum(g => GroupH(c, g.cats.Count) + S(6)) - S(6);
            // largest icon size (up to 34 px) at which every group fits without scrolling
            float cell = S(34);
            if (fitH > 0) while (cell > S(14) && TotalH(cell) > fitH - S(2)) cell -= S(1);
            foreach (var (gid, label, cats) in groups)
            {
                int perRow = Math.Max(1, (int)((w - pad * 2 + gap) / (cell + gap)));
                float h = GroupH(cell, cats.Count);
                var a = new Vector2(p.X, y);
                Ui.Panel(dl, a, a + new Vector2(w, h), Theme.Night2.With(0.6f));
                Ui.Caps(dl, a + new Vector2(pad, pad), label);
                for (int i = 0; i < cats.Count; i++)
                {
                    var c = cats[i];
                    var cp = a + new Vector2(pad + (i % perRow) * (cell + gap), pad + capsH + S(6) + (i / perRow) * (cell + gap));
                    bool sel = _showResults == 2 && _selectedCategory?.id == c.id;
                    bool clicked = Ui.Hit("mk_cat" + c.id, cp, new Vector2(cell, cell), out float hv);
                    Ui.Tooltip(c.name);
                    dl.AddRectFilled(cp, cp + new Vector2(cell, cell), (sel ? Ui.Accent.With(0.28f) : Theme.Night1.With(0.6f + 0.3f * hv)).U32(), S(6));
                    if (sel || hv > 0.01f) dl.AddRect(cp, cp + new Vector2(cell, cell), (sel ? Ui.Accent : Theme.Text2.With(0.4f * hv)).U32(), S(6), 0, S(1));
                    float ip = Math.Max(S(2), cell * 0.1f);
                    GameIcon(c.icon, cp + new Vector2(ip, ip), cell - ip * 2);
                    if (clicked) { LoadCategory(c, 1); if (avail_narrow()) _showCategories = false; }
                }
                y += h + S(6);
            }
            Advance(p, y - p.Y);

            bool avail_narrow() => ImGui.GetWindowWidth() < S(760);
        }

        /// <summary>Title of the result list (and the level/job filter for gear). Returns the y below it.</summary>
        private float DrawResultsHead(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            string title = _showResults == 1 ? (_isSearching ? "Searching..." : $"{Plural(_searchResults.Count, "result")} for \"{_query.Trim()}\"")
                : _showResults == 2 && _selectedCategory != null ? $"{_selectedCategory.name}  ·  {Plural(_categoryTotal > 0 ? _categoryTotal : _categoryItems.Count, "item")}"
                : "Search or pick a category";
            Ui.Text(dl, p + new Vector2(0, (FrameH - LH) / 2), Fit(title, w), _showResults == 0 ? Theme.Text3 : Theme.Text);
            float y = p.Y + FrameH + S(6);

            if (_showResults == 2 && _selectedCategory is { category: 1 or 2 } cat)
            {
                var at = new Vector2(p.X, y);
                Ui.Text(dl, at + new Vector2(0, (FrameH - LH) / 2), "Level", Theme.Text2);
                float lx = at.X + Ui.TextSize("Level").X + S(8), iw = S(60);
                bool reload = false;
                Input("##mk_minlv", ref _filterMinLevel, "min", iw, new Vector2(lx, y), 4, ImGuiInputTextFlags.CharsDecimal);
                reload |= ImGui.IsItemDeactivatedAfterEdit();
                Ui.Text(dl, new Vector2(lx + iw + S(5), y + (FrameH - LH) / 2), "-", Theme.Text2);
                Input("##mk_maxlv", ref _filterMaxLevel, "max", iw, new Vector2(lx + iw + S(16), y), 4, ImGuiInputTextFlags.CharsDecimal);
                reload |= ImGui.IsItemDeactivatedAfterEdit();
                if (cat.category == 2)
                {
                    float jx = lx + iw * 2 + S(30);
                    float jw = Math.Min(S(190), p.X + w - jx);
                    if (jw > S(90))
                    {
                        ImGui.SetCursorScreenPos(new Vector2(jx, y));
                        PushFrame();
                        ImGui.SetNextItemWidth(jw);
                        if (ImGui.BeginCombo("##mk_job", _filterJob.Length > 0 && JobNames.TryGetValue(_filterJob, out var jn) ? jn : "Any job"))
                        {
                            if (ImGui.Selectable("Any job", _filterJob.Length == 0)) { _filterJob = ""; reload = true; }
                            foreach (var (g, jobs) in JobGroups)
                            {
                                ImGui.TextDisabled(g);
                                foreach (var j in jobs)
                                    if (ImGui.Selectable($"  {JobNames[j]} ({j})", _filterJob == j)) { _filterJob = j; reload = true; }
                            }
                            ImGui.EndCombo();
                        }
                        PopFrame();
                    }
                }
                if (reload) LoadCategory(cat, 1);
                y += FrameH + S(8);
            }
            return y;
        }

        private void DrawResults(float w)
        {
            var p = ImGui.GetCursorScreenPos();
            if (_showResults == 1)
            {
                if (_searchError.Length > 0) { Message(p, w, _searchError, Theme.Bad); Advance(p, LH + S(28)); return; }
                if (_isSearching) { Ui.Text(Ui.Dl, p, "Searching...", Theme.Gold); Advance(p, LH + S(6)); return; }
                if (_searchResults.Count == 0) { Empty(p, w, _searched ? "Nothing found. Check the spelling or try part of the name." : "Type a name and press Search.", FontAwesomeIcon.Search); Advance(p, EmptyH); return; }
                foreach (var it in _searchResults.ToList()) ItemRow(it.id, it.name, it.icon, it.level, it.ilvl, it.canBeHq, w);
                return;
            }
            if (_showResults == 2)
            {
                var list = _categoryItems.ToList();
                if (_loadingCategory && list.Count == 0) { Ui.Text(Ui.Dl, p, "Loading...", Theme.Gold); Advance(p, LH + S(6)); return; }
                if (list.Count == 0) { Empty(p, w, "No items with these filters."); Advance(p, EmptyH); return; }
                foreach (var it in list) ItemRow(it.id, it.name, it.icon, it.level, it.ilvl, it.canBeHq, w);
                if (_categoryHasMore && _selectedCategory != null)
                {
                    var bp = ImGui.GetCursorScreenPos();
                    if (Ui.Button("mk_more", _loadingCategory ? "Loading..." : "Load more", bp, new Vector2(w, S(32))) && !_loadingCategory) LoadCategory(_selectedCategory, _categoryPage + 1);
                    Advance(bp, S(40));
                }
                return;
            }
            Empty(p, w, "Search by name or pick a category on the left.", FontAwesomeIcon.Store);
            Advance(p, EmptyH);
        }

        /// <summary>One item: icon, name and level open the prices; star, amount and Add on the right.</summary>
        private void ItemRow(int id, string name, string icon, int? level, int? ilvl, bool? canBeHq, float w, bool showFav = true, bool prices = false)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            string sub = LevelText(level, ilvl);
            float sh = Ui.TextSize("A", 0.85f).Y;
            // favourites show the lowest price instead of the level: NQ line, HQ line (+ average)
            int lines = prices ? 2 : sub.Length > 0 ? 1 : 0;
            float textH = LH + lines * (sh + S(3));
            float h = Math.Max(S(48), textH + S(16));
            float addW = S(64), star = S(30);
            float right = addW + S(8) + StepperW + S(8) + (showFav ? star + S(6) : 0);
            bool compact = w - right < S(150);
            if (compact) right = addW + S(8) + (showFav ? star + S(6) : 0);

            bool open = Ui.Hit("mk_row" + id, p, new Vector2(w - right - S(8), h), out float hv);
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f + 0.25f * hv), (hv > 0.01f ? Ui.Accent : Theme.Crystal).With(0.15f + 0.3f * hv), 8f);
            float ic = S(36);
            GameIcon(icon, p + new Vector2(S(10), (h - ic) / 2), ic);
            float tx = p.X + S(56), tw = w - right - S(70);
            float ty = p.Y + (h - textH) / 2;
            Ui.Text(dl, new Vector2(tx, ty), Fit(name, tw), Theme.Text);
            ty += LH + S(3);
            if (prices) DrawFavPrice(id, new Vector2(tx, ty), tw, sh);
            else if (sub.Length > 0) Ui.Text(dl, new Vector2(tx, ty), Fit(sub, tw, 0.85f), Theme.Text3, 0.85f);
            if (open) { SelectMarketItem(new MarketSearchItem { id = id, name = name, icon = icon, level = level, ilvl = ilvl, canBeHq = canBeHq }); }

            float x = p.X + w - S(8) - addW;
            if (Ui.Button("mk_add" + id, "Add", new Vector2(x, p.Y + (h - S(30)) / 2), new Vector2(addW, S(30)), Kind.Gold, FontAwesomeIcon.Plus))
            {
                int q = _quickQty.TryGetValue(id, out var qq) ? qq : 1;
                AddToCart(id, name, icon, canBeHq, level, ilvl, q, canBeHq == true && AddHq ? true : null);
                _addedAt[id] = DateTime.Now;
            }
            if (_addedAt.TryGetValue(id, out var at) && (DateTime.Now - at).TotalSeconds < 1.6)
                Ui.Tooltip("Added to the cart");
            if (!compact)
            {
                x -= S(8) + StepperW;
                int qty = _quickQty.TryGetValue(id, out var q2) ? q2 : 1;
                if (Stepper("mk_q" + id, ref qty, new Vector2(x, p.Y + (h - S(26)) / 2))) _quickQty[id] = qty;
            }
            if (showFav)
            {
                x -= S(6) + star;
                bool fav = IsFav(id);
                if (Ui.IconButton("mk_fav" + id, FontAwesomeIcon.Star, new Vector2(x, p.Y + (h - star) / 2), star, fav ? "Remove from favourites" : "Add to favourites", fav ? Theme.Gold : Theme.Text3, false))
                    ToggleFav(id, name, icon, canBeHq, level, ilvl);
            }
            Advance(p, h + S(6));
        }

        private readonly Dictionary<int, DateTime> _addedAt = new();

        // ---- favourite prices (website: /api/market/batch, lowest NQ/HQ listing + average, 100 ids per call) --------
        private sealed record FavPrice(int NqPrice, string NqWorld, int HqPrice, string HqWorld, float Avg);
        private Dictionary<int, FavPrice?> _favPrices = new();
        private string _favPriceKey = "", _favPriceError = "";
        private bool _favPricesLoading;
        private DateTime? _favCheckedAt;
        private int _favReq;

        /// <summary>Fetches when the tab opens, the scope changes or the favourites change (or after Refresh).</summary>
        private void EnsureFavPrices(IEnumerable<int> ids)
        {
            var list = ids.Distinct().OrderBy(i => i).ToList();
            var scope = Scope;
            var key = scope + "|" + string.Join(",", list);
            if (key == _favPriceKey) return;
            _favPriceKey = key;
            int req = ++_favReq;
            _favPricesLoading = true;
            _favPriceError = "";
            System.Threading.Tasks.Task.Run(async () =>
            {
                var next = new Dictionary<int, FavPrice?>();
                string error = "";
                try
                {
                    for (int i = 0; i < list.Count; i += 100)
                    {
                        var chunk = list.Skip(i).Take(100).ToList();
                        var json = await _sender.Get($"/api/market/batch?world={HubClient.Q(scope)}&itemIds={string.Join(",", chunk)}", okOnly: false);
                        if (string.IsNullOrEmpty(json)) { error = "Universalis did not answer."; break; }
                        var tok = Newtonsoft.Json.Linq.JToken.Parse(json);
                        if (tok["market"] is not Newtonsoft.Json.Linq.JObject market) { error = tok["error"]?.ToString() is { Length: > 0 } e ? e : "Universalis did not answer."; break; }
                        foreach (var id in chunk)
                        {
                            var m = market[id.ToString()];
                            var listings = (m?["listings"] as Newtonsoft.Json.Linq.JArray)?.ToObject<List<MarketListing>>();
                            if (listings == null || listings.Count == 0) { next[id] = null; continue; }
                            var nq = listings.Where(l => !l.hq).OrderBy(l => l.pricePerUnit).FirstOrDefault();
                            var hq = listings.Where(l => l.hq).OrderBy(l => l.pricePerUnit).FirstOrDefault();
                            float avg = m?["averagePrice"]?.ToObject<float?>() ?? 0;
                            next[id] = new FavPrice(nq?.pricePerUnit ?? 0, nq?.worldName ?? "", hq?.pricePerUnit ?? 0, hq?.worldName ?? "", avg);
                        }
                    }
                }
                catch (Exception ex) { _log.Error(ex, "Favourite prices failed"); error = "The prices could not be read."; }
                if (req != _favReq) return;
                if (error.Length == 0) { _favPrices = next; _favCheckedAt = DateTime.Now; }
                _favPriceError = error;
                _favPricesLoading = false;
            });
        }

        /// <summary>"NQ 72,746 Cerberus" / "HQ 65,470 Cerberus  avg 361,511", or a placeholder.</summary>
        private void DrawFavPrice(int id, Vector2 pos, float width, float lineH)
        {
            var dl = Ui.Dl;
            const float sc = 0.85f;
            if (!_favPrices.TryGetValue(id, out var fp)) { Ui.Text(dl, pos, _favPricesLoading ? "Checking prices..." : "-", Theme.Text3, sc); return; }
            if (fp == null) { Ui.Text(dl, pos, "No listings", Theme.Text3, sc); return; }
            string avg = fp.Avg > 0 ? $"avg {Gil((long)fp.Avg)}" : "";
            var rows = new List<(string q, int price, string world)>();
            if (fp.NqPrice > 0) rows.Add(("NQ", fp.NqPrice, fp.NqWorld));
            if (fp.HqPrice > 0) rows.Add(("HQ", fp.HqPrice, fp.HqWorld));
            float right = pos.X + width;
            for (int i = 0; i < rows.Count; i++)
            {
                float x = pos.X, y = pos.Y + i * (lineH + S(3));
                var parts = new List<(string t, Vector4 c)> { (rows[i].q, Theme.Text2), (Gil(rows[i].price), Theme.Gold), (rows[i].world, Theme.Text3) };
                if (i == rows.Count - 1 && avg.Length > 0) parts.Add(("   " + avg, Theme.Text3));
                foreach (var (t, c) in parts)
                {
                    if (t.Length == 0 || x >= right) continue;
                    var f = Fit(t, right - x, sc);
                    Ui.Text(dl, new Vector2(x, y), f, c, sc);
                    x += Ui.TextSize(f + " ", sc).X;
                }
            }
        }

        // =============================================================================================================
        private void DrawFavorites()
        {
            var top = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("mk_favs", avail))
            {
                float w = avail.X - S(10);
                var p = ImGui.GetCursorScreenPos();
                var favs = _favs.Values.OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase).ToList();
                if (favs.Count == 0)
                {
                    Empty(p, w, "No favourites yet. Press the star on an item to keep it here.", FontAwesomeIcon.Star);
                    Advance(p, EmptyH);
                }
                else
                {
                    EnsureFavPrices(favs.Select(f => f.id));
                    // head: where the prices come from, when they were checked, refresh
                    var dl = Ui.Dl;
                    float hh = S(30);
                    string info = _favPricesLoading ? "Checking prices..." : _favPriceError.Length > 0 ? _favPriceError
                        : $"Lowest price on {ScopeShort(Scope)}{(_favCheckedAt.HasValue ? $"  ·  checked {_favCheckedAt.Value:HH:mm}" : "")}";
                    Ui.Text(dl, p + new Vector2(0, (hh - LH) / 2), Fit(info, w - hh - S(10)), _favPriceError.Length > 0 ? Theme.Bad : Theme.Text2);
                    if (Ui.IconButton("mk_favref", FontAwesomeIcon.Sync, p + new Vector2(w - hh, 0), hh, "Refresh prices") && !_favPricesLoading) _favPriceKey = "";
                    Advance(p, hh + S(8));
                    foreach (var f in favs) ItemRow(f.id, f.name, f.icon, f.level, f.ilvl, f.canBeHq, w, prices: true);
                }
            }
            ImGui.EndChild();
        }
    }
}
