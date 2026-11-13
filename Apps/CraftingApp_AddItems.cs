using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lumina.Excel.Sheets;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    public partial class CraftingApp
    {
        // ---- search state -------------------------------------------------------------------------------------------
        private string _searchQuery = "";
        private int _reqLevelMin, _reqLevelMax, _iLvlMin, _iLvlMax, _rLvlMin, _rLvlMax;
        private int _craftedByJob = -1;
        private bool _collectableOnly;
        private bool _showFilters;
        private int _sortMode; // 0 relevance, 1 name, 2 level, 3 iLvl
        private readonly HashSet<string> _selectedJobs = new();
        private readonly List<Recipe> _searchResults = new();
        private bool _hasSearched;
        private readonly Dictionary<uint, int> _addQuantities = new();
        private uint _justAdded;
        private DateTime _justAddedAt;

        private static readonly string[] SortNames = { "Relevance", "Name", "Level", "iLvl" };
        private static readonly string[] WornBy =
        {
            "PLD", "WAR", "DRK", "GNB", "WHM", "SCH", "AST", "SGE", "MNK", "DRG", "NIN", "SAM", "RPR", "VPR", "BRD", "MCH", "DNC", "BLM", "SMN", "RDM", "PCT",
            "GLA", "MRD", "CNJ", "PGL", "LNC", "ROG", "ARC", "THM", "ACN", "MIN", "BTN", "FSH",
            "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL",
        };

        private void DrawSearchView()
        {
            var avail = ImGui.GetContentRegionAvail();
            var top = ImGui.GetCursorScreenPos();
            bool wide = avail.X >= S(760);
            float draftW = wide ? Math.Max(S(280), avail.X * 0.32f) : avail.X;
            float leftW = wide ? avail.X - draftW - S(12) : avail.X;

            // search bar: input, sort, filters
            float bh = FrameH, sortW = S(128);
            if (Input("##cf_q", ref _searchQuery, "Search recipes by item name", leftW - sortW - bh - S(16), top)) PerformSearch();
            var sortPos = new Vector2(top.X + leftW - sortW - bh - S(8), top.Y);
            ImGui.SetCursorScreenPos(sortPos);
            PushFrame();
            ImGui.SetNextItemWidth(sortW);
            if (ImGui.BeginCombo("##cf_sort", SortNames[_sortMode]))
            {
                for (int i = 0; i < SortNames.Length; i++) if (ImGui.Selectable(SortNames[i], i == _sortMode)) _sortMode = i;
                ImGui.EndCombo();
            }
            PopFrame();
            if (Ui.IconButton("cf_filters", FontAwesomeIcon.Filter, new Vector2(top.X + leftW - bh, top.Y), bh, _showFilters ? "Hide filters" : "Filters", null, _showFilters || FiltersActive))
                _showFilters = !_showFilters;

            float y = top.Y + bh + S(10);
            if (_showFilters) y = DrawFilters(new Vector2(top.X, y), leftW) + S(10);

            float bottom = top.Y + avail.Y;
            float listH = wide ? bottom - y : Math.Max(S(200), (bottom - y) * 0.58f);
            ImGui.SetCursorScreenPos(new Vector2(top.X, y));
            if (UIHelper.BeginSmoothChild("cf_results", new Vector2(leftW, listH))) DrawSearchResults(leftW - S(10));
            ImGui.EndChild();

            var dp = wide ? new Vector2(top.X + leftW + S(12), top.Y) : new Vector2(top.X, y + listH + S(10));
            ImGui.SetCursorScreenPos(dp);
            if (UIHelper.BeginSmoothChild("cf_draft", new Vector2(draftW, Math.Max(S(120), bottom - dp.Y)))) DrawDraft(draftW - S(10));
            ImGui.EndChild();
        }

        private bool FiltersActive => _craftedByJob >= 0 || _collectableOnly || _selectedJobs.Count > 0
            || _reqLevelMin > 0 || _reqLevelMax > 0 || _iLvlMin > 0 || _iLvlMax > 0 || _rLvlMin > 0 || _rLvlMax > 0;

        /// <summary>Filter panel; returns its bottom edge.</summary>
        private float DrawFilters(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            dl.ChannelsSplit(2);
            dl.ChannelsSetCurrent(1);
            float pad = S(12), left = p.X + pad, right = p.X + w - pad;
            bool changed = false;

            float y = p.Y + pad;
            Ui.Caps(dl, new Vector2(left, y), "Crafted by");
            y += LH + S(4);
            var at = new Vector2(left, y);
            if (ChipToggle("cf_cb_any", "Any", _craftedByJob < 0, ref at, left, right)) { _craftedByJob = -1; changed = true; }
            for (int i = 0; i < 8; i++)
                if (ChipToggle("cf_cb_" + i, Jobs[i], _craftedByJob == i, ref at, left, right)) { _craftedByJob = _craftedByJob == i ? -1 : i; changed = true; }
            y = at.Y + ChipH() + S(10);

            // level ranges
            float fw = S(64), x = left;
            void Range(string label, string id, ref int min, ref int max)
            {
                Ui.Text(dl, new Vector2(x, y + (FrameH - LH) / 2), label, Theme.Text2, 0.9f);
                x += Ui.TextSize(label, 0.9f).X + S(8);
                if (IntInput("##" + id + "a", ref min, fw, new Vector2(x, y), 0, 9999)) changed = true;
                x += fw + S(4);
                Ui.Text(dl, new Vector2(x, y + (FrameH - LH) / 2), "-", Theme.Text3);
                x += S(12);
                if (IntInput("##" + id + "b", ref max, fw, new Vector2(x, y), 0, 9999)) changed = true;
                x += fw + S(18);
                if (x + S(200) > right) { x = left; y += FrameH + S(8); }
            }
            Range("Level", "cf_lv", ref _reqLevelMin, ref _reqLevelMax);
            Range("iLvl", "cf_il", ref _iLvlMin, ref _iLvlMax);
            Range("Recipe lvl", "cf_rl", ref _rLvlMin, ref _rLvlMax);
            if (x > left) y += FrameH + S(8);
            if (Check("cf_coll", "Collectables only", ref _collectableOnly, new Vector2(left, y))) changed = true;
            y += Math.Max(S(18), LH) + S(10);

            Ui.Caps(dl, new Vector2(left, y), "Worn by");
            y += LH + S(4);
            at = new Vector2(left, y);
            foreach (var j in WornBy)
            {
                bool on = _selectedJobs.Contains(j);
                if (ChipToggle("cf_wb_" + j, j, on, ref at, left, right, 0.78f)) { if (on) _selectedJobs.Remove(j); else _selectedJobs.Add(j); changed = true; }
            }
            y = at.Y + ChipH(0.78f) + S(10);

            if (FiltersActive && Ui.Button("cf_freset", "Reset filters", new Vector2(left, y), new Vector2(S(140), S(28)), Kind.Ghost, FontAwesomeIcon.Undo))
            {
                _craftedByJob = -1; _collectableOnly = false; _selectedJobs.Clear();
                _reqLevelMin = _reqLevelMax = _iLvlMin = _iLvlMax = _rLvlMin = _rLvlMax = 0;
                changed = true;
            }
            if (FiltersActive) y += S(28) + S(4);

            float bottom = y + pad - S(4);
            dl.ChannelsSetCurrent(0);
            Ui.Panel(dl, p, new Vector2(p.X + w, bottom), Theme.Night2.With(0.55f));
            dl.ChannelsMerge();
            Advance(p, bottom - p.Y);
            if (changed) PerformSearch();
            return bottom;
        }

        private void DrawSearchResults(float w)
        {
            var dl = Ui.Dl;
            if (!_hasSearched)
            {
                var p0 = ImGui.GetCursorScreenPos();
                Ui.Text(dl, p0 + S(4, 8), "Type an item name, or pick a filter.", Theme.Text3);
                Ui.Text(dl, p0 + new Vector2(S(4), S(12) + LH), "Recipes you add go to the draft, then to your pipeline.", Theme.Text3, 0.9f);
                Advance(p0, LH * 2 + S(20));
                return;
            }
            if (_searchResults.Count == 0)
            {
                var p0 = ImGui.GetCursorScreenPos();
                Ui.Text(dl, p0 + S(4, 8), "Nothing found. Try other words or filters.", Theme.Text3);
                Advance(p0, LH + S(16));
                return;
            }

            IEnumerable<Recipe> list = _searchResults;
            if (_sortMode == 1) list = list.OrderBy(r => r.ItemResult.Value.Name.ToString());
            else if (_sortMode == 2) list = list.OrderByDescending(r => r.RecipeLevelTable.Value.ClassJobLevel);
            else if (_sortMode == 3) list = list.OrderByDescending(r => r.ItemResult.Value.LevelItem.RowId);

            float rowH = S(14) + LH * 2;
            foreach (var r in list)
            {
                var item = r.ItemResult.Value;
                var p = ImGui.GetCursorScreenPos();
                if (!ImGui.IsRectVisible(new Vector2(w, rowH))) { Advance(p, rowH); continue; }

                bool added = _justAdded == r.RowId && (DateTime.Now - _justAddedAt).TotalSeconds < 1.2;
                Ui.Panel(dl, p, p + new Vector2(w, rowH), Theme.Night2.With(0.7f), added ? Theme.Live.With(0.6f) : Theme.CrystalLine, 8f);
                float ic = rowH - S(14);
                GameIcon(item.Icon, p + S(7, 7), ic);
                int step = Math.Max(1, _addQuantities.GetValueOrDefault(r.RowId, 1));
                float btnW = S(82), right = p.X + w - S(8);
                float textW = w - ic - S(24) - btnW - StepperW - S(16);
                var rl = r.RecipeLevelTable.Value;
                Ui.Text(dl, new Vector2(p.X + ic + S(16), p.Y + S(6)), Fit(item.Name.ToString(), textW), Theme.Text);
                var sub = $"{JobOf(r)} · Lv {rl.ClassJobLevel}{(rl.Stars > 0 ? " " + new string('*', rl.Stars) : "")} · iLvl {item.LevelItem.RowId}{(item.IsCollectable ? " · Collectable" : "")}";
                Ui.Text(dl, new Vector2(p.X + ic + S(16), p.Y + S(8) + LH), Fit(sub, textW, 0.85f), Theme.Text3, 0.85f);

                float by = p.Y + (rowH - S(28)) / 2;
                if (Stepper("cf_rq" + r.RowId, ref step, new Vector2(right - btnW - S(8) - StepperW, by + S(1)))) _addQuantities[r.RowId] = step;
                if (Ui.Button("cf_add" + r.RowId, added ? "Added" : "Add", new Vector2(right - btnW, by), new Vector2(btnW, S(28)), Kind.Primary, added ? FontAwesomeIcon.Check : FontAwesomeIcon.Plus))
                {
                    AddToDraft(r, step);
                    _justAdded = r.RowId;
                    _justAddedAt = DateTime.Now;
                }
                Advance(p, rowH + S(6));
            }
        }

        private void AddToDraft(Recipe r, int qty)
        {
            var item = r.ItemResult.Value;
            var same = _draft.FirstOrDefault(d => Int(d["item"], "id") == (int)item.RowId);
            if (same != null) same["quantity"] = Int(same, "quantity", 1) + qty;
            else
            {
                var rl = r.RecipeLevelTable.Value;
                _draft.Add(new JObject
                {
                    ["item"] = new JObject
                    {
                        ["id"] = item.RowId, ["name"] = item.Name.ToString(), ["icon"] = IconPath(item.Icon), ["recipeId"] = r.RowId,
                        ["job"] = JobOf(r), ["level"] = (int)rl.ClassJobLevel, ["stars"] = (int)rl.Stars, ["ilvl"] = item.LevelItem.RowId,
                        ["equipLevel"] = (int)item.LevelEquip, ["isCollectable"] = item.IsCollectable,
                    },
                    ["quantity"] = qty,
                });
            }
            WriteDraft();
        }

        private void DrawDraft(float w)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            Ui.Caps(dl, p + S(2, 2), "Draft");
            var cnt = $"{_draft.Count} {(_draft.Count == 1 ? "craft" : "crafts")}";
            Ui.Text(dl, new Vector2(p.X + w - Ui.TextSize(cnt, 0.85f).X, p.Y + S(2)), cnt, Theme.Text3, 0.85f);
            float y = p.Y + LH + S(10);

            float sendH = S(36);
            bool canSend = _draft.Count > 0;
            if (canSend && Ui.Button("cf_send", "Send to pipeline", new Vector2(p.X, y), new Vector2(w, sendH), Kind.Gold, FontAwesomeIcon.PaperPlane))
            {
                var recipes = _dataManager.GetExcelSheet<Recipe>();
                InitRecipeCache();
                foreach (var d in _draft)
                {
                    uint rid = (uint)Math.Max(0, Int(d["item"], "recipeId"));
                    var r = recipes.GetRowOrDefault(rid);
                    if (!r.HasValue || r.Value.ItemResult.RowId != (uint)Int(d["item"], "id"))
                        rid = _recipeIdByResult!.GetValueOrDefault((uint)Int(d["item"], "id"));
                    AddTarget(rid, Math.Max(1, Int(d, "quantity", 1)), write: false);
                }
                WritePipeline(rebuild: true);
                _draft.Clear();
                WriteDraft();
                SetView(1);
            }
            if (canSend) y += sendH + S(10);

            if (_draft.Count == 0)
            {
                Ui.Text(dl, new Vector2(p.X, y), "Your draft is empty.", Theme.Text3);
                Ui.Text(dl, new Vector2(p.X, y + LH + S(2)), "Add recipes from the search.", Theme.Text3, 0.9f);
                Advance(p, y - p.Y + LH * 2 + S(8));
                return;
            }

            float rowH = S(10) + Math.Max(S(28), LH * 2 + S(2));
            JObject? remove = null;
            bool dirty = false;
            var items = _dataManager.GetExcelSheet<Item>();
            foreach (var d in _draft)
            {
                var it = d["item"];
                int id = Int(it, "id");
                var row = items.GetRowOrDefault((uint)id);
                var rp = new Vector2(p.X, y);
                Ui.Panel(dl, rp, rp + new Vector2(w, rowH), Theme.Night2.With(0.6f), Theme.CrystalLine, 6f);
                float ic = rowH - S(12);
                if (row.HasValue) GameIcon(row.Value.Icon, rp + S(6, 6), ic);
                float tx = rp.X + ic + S(14), tw = w - ic - S(20) - StepperW - S(34);
                Ui.Text(dl, new Vector2(tx, rp.Y + S(5)), Fit(Str(it, "name"), tw), Theme.Text);
                Ui.Text(dl, new Vector2(tx, rp.Y + S(7) + LH), $"{Str(it, "job")} · Lv {Int(it, "level")}", Theme.Text3, 0.85f);
                int q = Math.Max(1, Int(d, "quantity", 1));
                float by = rp.Y + (rowH - S(26)) / 2;
                if (Stepper("cf_dq" + id, ref q, new Vector2(rp.X + w - StepperW - S(34), by))) { d["quantity"] = q; dirty = true; }
                if (Ui.IconButton("cf_drm" + id, FontAwesomeIcon.Times, new Vector2(rp.X + w - S(30), by), S(26), "Remove", Theme.Bad)) remove = d;
                y += rowH + S(6);
            }
            if (remove != null) { _draft.Remove(remove); dirty = true; }
            if (dirty) WriteDraft();
            Advance(p, y - p.Y);
        }

        private void PerformSearch()
        {
            _searchResults.Clear();
            if (string.IsNullOrWhiteSpace(_searchQuery) && !FiltersActive) { _hasSearched = false; return; }
            var sheet = _dataManager.GetExcelSheet<Recipe>();
            string q = _searchQuery.Trim().ToLowerInvariant();
            int max(int v) => v <= 0 ? int.MaxValue : v;

            foreach (var r in sheet)
            {
                var item = r.ItemResult.Value;
                if (r.ItemResult.RowId == 0 || r.RecipeLevelTable.RowId == 0) continue;
                var name = item.Name.ToString();
                if (name.Length == 0) continue;
                if (q.Length > 0 && !name.ToLowerInvariant().Contains(q)) continue;
                var rl = r.RecipeLevelTable.Value;
                if (rl.ClassJobLevel < _reqLevelMin || rl.ClassJobLevel > max(_reqLevelMax)) continue;
                if (r.RecipeLevelTable.RowId < _rLvlMin || r.RecipeLevelTable.RowId > max(_rLvlMax)) continue;
                if (item.LevelItem.RowId < _iLvlMin || item.LevelItem.RowId > max(_iLvlMax)) continue;
                if (_craftedByJob >= 0 && r.CraftType.RowId != _craftedByJob) continue;
                if (_collectableOnly && !item.IsCollectable) continue;
                if (_selectedJobs.Count > 0 && !WornByMatch(item)) continue;
                _searchResults.Add(r);
                if (_searchResults.Count >= 60) break;
            }
            // relevance: names that start with the query first
            if (q.Length > 0)
            {
                var sorted = _searchResults.OrderBy(r => r.ItemResult.Value.Name.ToString().ToLowerInvariant().StartsWith(q) ? 0 : 1).ToList();
                _searchResults.Clear();
                _searchResults.AddRange(sorted);
            }
            _hasSearched = true;
        }

        private bool WornByMatch(Item item)
        {
            var cjc = item.ClassJobCategory.Value;
            if (item.ClassJobCategory.RowId == 0) return false;
            foreach (var j in _selectedJobs)
            {
                var prop = cjc.GetType().GetProperty(j);
                var v = prop?.GetValue(cjc);
                if (v is bool b && b) return true;
                if (v is byte by && by > 0) return true;
            }
            return false;
        }
    }
}
