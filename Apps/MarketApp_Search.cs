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
                if (UIHelper.BeginSmoothChild("mk_cats", new Vector2(lw, top.Y + avail.Y - y))) DrawCategoryGroups(lw - S(10));
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

        private void DrawCategoryGroups(float w)
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

            float cell = S(40), gap = S(6), pad = S(12);
            foreach (var (gid, label) in CategoryGroups)
            {
                var cats = _categories.Where(c => c.category == gid).ToList();
                if (cats.Count == 0) continue;
                int perRow = Math.Max(1, (int)((w - pad * 2 + gap) / (cell + gap)));
                int rows = (cats.Count + perRow - 1) / perRow;
                float capsH = Ui.TextSize("A", 0.78f).Y;
                float h = pad + capsH + S(8) + rows * (cell + gap) - gap + pad;
                var a = new Vector2(p.X, y);
                Ui.Panel(dl, a, a + new Vector2(w, h), Theme.Night2.With(0.6f));
                Ui.Caps(dl, a + new Vector2(pad, pad), label);
                for (int i = 0; i < cats.Count; i++)
                {
                    var c = cats[i];
                    var cp = a + new Vector2(pad + (i % perRow) * (cell + gap), pad + capsH + S(8) + (i / perRow) * (cell + gap));
                    bool sel = _showResults == 2 && _selectedCategory?.id == c.id;
                    bool clicked = Ui.Hit("mk_cat" + c.id, cp, new Vector2(cell, cell), out float hv);
                    Ui.Tooltip(c.name);
                    dl.AddRectFilled(cp, cp + new Vector2(cell, cell), (sel ? Ui.Accent.With(0.28f) : Theme.Night1.With(0.6f + 0.3f * hv)).U32(), S(6));
                    if (sel || hv > 0.01f) dl.AddRect(cp, cp + new Vector2(cell, cell), (sel ? Ui.Accent : Theme.Text2.With(0.4f * hv)).U32(), S(6), 0, S(1));
                    GameIcon(c.icon, cp + S(5, 5), cell - S(10));
                    if (clicked) { LoadCategory(c, 1); if (avail_narrow()) _showCategories = false; }
                }
                y += h + S(8);
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
        private void ItemRow(int id, string name, string icon, int? level, int? ilvl, bool? canBeHq, float w, bool showFav = true)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            string sub = LevelText(level, ilvl);
            float h = Math.Max(S(48), (sub.Length > 0 ? LH * 2 + S(4) : LH) + S(16));
            float addW = S(64), star = S(30);
            float right = addW + S(8) + StepperW + S(8) + (showFav ? star + S(6) : 0);
            bool compact = w - right < S(150);
            if (compact) right = addW + S(8) + (showFav ? star + S(6) : 0);

            bool open = Ui.Hit("mk_row" + id, p, new Vector2(w - right - S(8), h), out float hv);
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f + 0.25f * hv), (hv > 0.01f ? Ui.Accent : Theme.Crystal).With(0.15f + 0.3f * hv), 8f);
            float ic = S(36);
            GameIcon(icon, p + new Vector2(S(10), (h - ic) / 2), ic);
            float tx = p.X + S(56), tw = w - right - S(70);
            if (sub.Length > 0)
            {
                Ui.Text(dl, new Vector2(tx, p.Y + (h - LH * 2 - S(4)) / 2), Fit(name, tw), Theme.Text);
                Ui.Text(dl, new Vector2(tx, p.Y + (h - LH * 2 - S(4)) / 2 + LH + S(4)), Fit(sub, tw, 0.85f), Theme.Text3, 0.85f);
            }
            else Ui.Text(dl, new Vector2(tx, p.Y + (h - LH) / 2), Fit(name, tw), Theme.Text);
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
                else foreach (var f in favs) ItemRow(f.id, f.name, f.icon, f.level, f.ilvl, f.canBeHq, w);
            }
            ImGui.EndChild();
        }
    }
}
