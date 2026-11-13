using System;
using System.Collections.Concurrent;
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
        /// <summary>One line of the pipeline, read from crafting.pipeline_v2 items plus live bag counts.</summary>
        private class Mat
        {
            public uint Id; public string Name = ""; public uint Icon; public string Cat = ""; public int Tier;
            public int Req, Eff, Inv, Completed, Have, Stars, Yield = 1, Price;
            public string Zone = "", Times = ""; public string? Method; public uint Aetheryte; public bool Ready;
            public List<(uint Id, int Amt)> Ings = new();
            public bool Crafted => Cat == "precraft" || Cat == "final";
            public bool Done => Have >= Eff;
        }

        private List<Mat>? _rollup;
        private DateTime _countsAt, _completedWriteAt;
        private readonly Dictionary<string, bool> _collapsed = new();
        private readonly Dictionary<uint, uint> _aetheryteOf = new();
        private HashSet<uint>? _vendorItems;
        private Dictionary<uint, uint>? _recipeIdByResult;
        private DateTime _confirmClearAt;

        private readonly ConcurrentDictionary<uint, int> _priceCache = new();
        private static readonly System.Net.Http.HttpClient _httpClient = new() { DefaultRequestHeaders = { { "User-Agent", HubClient.UserAgent } } };
        private volatile bool _isFetchingPrices, _pricesFresh;
        private string? _scopeEdit;

        private static readonly (string Cat, string Title, Vector4 Color)[] Sections =
        {
            ("crystals", "Crystals", Theme.Crystal), ("timed", "Timed nodes", Theme.Live), ("gathering", "Gathering", Theme.CrystalDeep),
            ("tomes", "Vendors, tomes, scrips", Theme.Warn), ("drops", "Other materials", Theme.Text2),
            ("precraft", "Pre-crafts", Theme.Violet), ("final", "Final crafts", Theme.Gold),
        };

        // =============================================================================================================
        // View
        // =============================================================================================================
        private void DrawPipelineView()
        {
            var dl = Ui.Dl;
            var top = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            if (_targets.Count == 0)
            {
                Ui.Panel(dl, top, top + new Vector2(avail.X, S(120)), Theme.Night2.With(0.6f));
                Ui.Text(dl, top + S(18, 18), "Your pipeline is empty.", Theme.Text);
                Ui.Text(dl, top + new Vector2(S(18), S(24) + LH), "Add recipes and send them here: you get every material, where to find it and what it costs.", Theme.Text3, 0.9f);
                if (Ui.Button("cf_goadd", "Add recipes", top + new Vector2(S(18), S(70)), new Vector2(S(150), S(32)), Kind.Primary, FontAwesomeIcon.Search)) SetView(0);
                Advance(top, S(120));
                return;
            }

            if (_rollup == null) BuildRollup();
            if (_pricesFresh) ApplyPrices();
            RefreshCounts();
            var mats = _rollup!;

            // ---- summary: cost, value, profit, market -----------------------------------------------------------
            float sumH = S(16) + LH * 2;
            Ui.Panel(dl, top, top + new Vector2(avail.X, sumH), Theme.Night2.With(0.7f), Theme.GoldLine);
            long cost = mats.Where(m => !m.Crafted).Sum(m => (long)PriceOf(m) * m.Req);
            long value = _targets.Sum(t => (long)_priceCache.GetValueOrDefault((uint)Int(t, "itemId")) * Int(t, "quantity"));
            float x = top.X + S(16);
            void Stat(string label, long v, Vector4 col)
            {
                Ui.Caps(dl, new Vector2(x, top.Y + S(7)), label, Theme.Text3, 0.72f);
                Ui.Text(dl, new Vector2(x, top.Y + S(9) + LH), v == 0 ? "-" : $"{v:N0} g", col);
                x += Math.Max(S(110), Ui.TextSize($"{v:N0} g").X + S(28));
            }
            Stat("Est. cost", cost, Theme.Bad);
            Stat("Est. value", value, Theme.Gold);
            Stat("Profit", value == 0 || cost == 0 ? 0 : value - cost, value - cost >= 0 ? Theme.Live : Theme.Bad);

            float pbW = S(112), scW = S(120), by = top.Y + (sumH - FrameH) / 2;
            float pbX = top.X + avail.X - pbW - S(12);
            if (pbX - scW - S(8) > x)
            {
                _scopeEdit ??= (_sender.GetState(SecScope) as JValue)?.Value as string ?? "";
                if (Input("##cf_scope", ref _scopeEdit, "Europe", scW, new Vector2(pbX - scW - S(8), by))) { }
                if (ImGui.IsItemDeactivatedAfterEdit()) _sender.SetState(SecScope, _scopeEdit.Trim());
                Ui.Tooltip("Market for prices: a world, data center or region (shared with the website)");
            }
            if (Ui.Button("cf_prices", _isFetchingPrices ? "Fetching..." : "Prices", new Vector2(pbX, by), new Vector2(pbW, FrameH), Kind.Primary, FontAwesomeIcon.Coins) && !_isFetchingPrices)
                FetchPrices(mats.Select(m => m.Id).Concat(_targets.Select(t => (uint)Int(t, "itemId"))).Distinct().ToList());
            float y = top.Y + sumH + S(10);

            // ---- all targets crafted --------------------------------------------------------------------------------
            var finals = mats.Where(m => m.Cat == "final").ToList();
            if (finals.Count > 0 && finals.All(m => m.Inv >= m.Req))
            {
                float bh = S(44);
                var bp = new Vector2(top.X, y);
                Ui.Panel(dl, bp, bp + new Vector2(avail.X, bh), Theme.Live.With(0.1f), Theme.Live.With(0.5f));
                Ui.Icon(dl, bp + new Vector2(S(14), (bh - LH) / 2), FontAwesomeIcon.CheckCircle, Theme.Live);
                Ui.Text(dl, bp + new Vector2(S(40), (bh - LH) / 2), "Every target is crafted.", Theme.Text);
                if (Ui.Button("cf_doneclear", "Clear pipeline", bp + new Vector2(avail.X - S(160), (bh - S(30)) / 2), new Vector2(S(148), S(30)), Kind.Gold, FontAwesomeIcon.Broom))
                    ClearPipeline();
                y += bh + S(10);
            }

            float bottom = top.Y + avail.Y;
            bool wide = avail.X >= S(820);
            if (wide)
            {
                float tw = Math.Max(S(300), avail.X * 0.34f);
                ImGui.SetCursorScreenPos(new Vector2(top.X, y));
                if (UIHelper.BeginSmoothChild("cf_targets", new Vector2(tw, bottom - y))) DrawTargets(tw - S(10));
                ImGui.EndChild();
                ImGui.SetCursorScreenPos(new Vector2(top.X + tw + S(12), y));
                if (UIHelper.BeginSmoothChild("cf_mats", new Vector2(avail.X - tw - S(12), bottom - y))) DrawMaterials(avail.X - tw - S(22));
                ImGui.EndChild();
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(top.X, y));
                if (UIHelper.BeginSmoothChild("cf_pipe", new Vector2(avail.X, bottom - y)))
                {
                    DrawTargets(avail.X - S(10));
                    ImGui.Dummy(new Vector2(1, S(8)));
                    DrawMaterials(avail.X - S(10));
                }
                ImGui.EndChild();
            }
        }

        private void DrawTargets(float w)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            Ui.Caps(dl, p + S(2, 2), "Targets");
            float y = p.Y + LH + S(10);
            var items = _dataManager.GetExcelSheet<Item>();
            float rowH = S(18) + LH * 2;
            JObject? remove = null;
            bool changed = false;
            foreach (var t in _targets)
            {
                uint itemId = (uint)Int(t, "itemId");
                int qty = Math.Max(1, Int(t, "quantity", 1));
                var m = _rollup?.FirstOrDefault(r => r.Id == itemId && r.Cat == "final");
                int have = m?.Inv ?? 0;
                var rp = new Vector2(p.X, y);
                bool done = have >= qty;
                Ui.Panel(dl, rp, rp + new Vector2(w, rowH), Theme.Night2.With(0.65f), done ? Theme.Live.With(0.5f) : Theme.CrystalLine, 8f);
                float ic = rowH - S(16);
                var row = items.GetRowOrDefault(itemId);
                if (row.HasValue) GameIcon(row.Value.Icon, rp + S(8, 8), ic);
                float tx = rp.X + ic + S(16), tw = w - ic - S(24) - StepperW - S(34);
                Ui.Text(dl, new Vector2(tx, rp.Y + S(6)), Fit(Str(t, "name"), tw), Theme.Text);
                Ui.Text(dl, new Vector2(tx, rp.Y + S(8) + LH), $"{Str(t, "job")} · Lv {Int(t, "level")} · {Math.Min(have, qty)}/{qty}", done ? Theme.Live : Theme.Text3, 0.85f);
                Ui.Progress(dl, new Vector2(tx, rp.Y + rowH - S(7)), tw, (float)Math.Min(have, qty) / qty, done ? Theme.Live : Ui.Accent, 3f);
                float by = rp.Y + (rowH - S(26)) / 2;
                if (Stepper("cf_tq" + Str(t, "id"), ref qty, new Vector2(rp.X + w - StepperW - S(34), by))) { t["quantity"] = qty; changed = true; }
                if (Ui.IconButton("cf_trm" + Str(t, "id"), FontAwesomeIcon.Times, new Vector2(rp.X + w - S(30), by), S(26), "Remove", Theme.Bad)) remove = t;
                y += rowH + S(6);
            }
            if (remove != null) { _targets.Remove(remove); changed = true; }
            if (changed) { WritePipeline(rebuild: true); BuildRollup(); }

            bool armed = (DateTime.Now - _confirmClearAt).TotalSeconds < 3;
            if (Ui.Button("cf_clear", armed ? "Tap again to clear" : "Clear pipeline", new Vector2(p.X, y + S(4)), new Vector2(w, S(32)), armed ? Kind.Danger : Kind.Ghost, FontAwesomeIcon.Trash))
            {
                if (armed) ClearPipeline();
                else _confirmClearAt = DateTime.Now;
            }
            Advance(p, y - p.Y + S(40));
        }

        private void ClearPipeline()
        {
            _targets.Clear();
            _items = new JObject();
            _confirmClearAt = DateTime.MinValue;
            WritePipeline(rebuild: false);
        }

        private void DrawMaterials(float w)
        {
            var dl = Ui.Dl;
            var mats = _rollup!;
            foreach (var (cat, title, color) in Sections)
            {
                var list = mats.Where(m => m.Cat == cat).ToList();
                if (list.Count == 0) continue;
                if (!SectionHeader(cat, title, list.Count, color, list.All(m => m.Done), w)) continue;

                if (cat == "crystals") { DrawCrystals(list, w); continue; }
                IEnumerable<IGrouping<string, Mat>> groups = cat switch
                {
                    "timed" or "gathering" => list.GroupBy(m => m.Zone).OrderBy(g => g.Key),
                    "precraft" => list.GroupBy(m => $"Tier {m.Tier}").OrderByDescending(g => g.Key),
                    _ => list.GroupBy(_ => ""),
                };
                foreach (var g in groups)
                {
                    if (g.Key.Length > 0)
                    {
                        var gp = ImGui.GetCursorScreenPos();
                        Ui.Text(dl, gp + S(4, 0), g.Key, Theme.Text2, 0.88f);
                        Advance(gp, LH + S(4));
                    }
                    foreach (var m in g) DrawMatRow(m, w);
                }
                ImGui.Dummy(new Vector2(1, S(6)));
            }
        }

        private bool SectionHeader(string key, string title, int count, Vector4 color, bool done, float w)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            float h = LH + S(14);
            bool open = !_collapsed.GetValueOrDefault(key);
            if (Ui.Hit("cf_sec" + key, p, new Vector2(w, h), out float hov)) { open = !open; _collapsed[key] = !open; }
            dl.AddRectFilled(p, p + new Vector2(w, h), Theme.Night1.With(0.5f + 0.3f * hov).U32(), S(6));
            dl.AddRectFilled(p, p + new Vector2(S(3), h), color.U32(), S(2));
            Ui.Icon(dl, p + new Vector2(S(12), (h - LH) / 2 + S(1)), open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, Theme.Text3, 0.75f);
            float tw = Ui.Caps(dl, p + new Vector2(S(32), (h - LH * 0.78f) / 2), title, color);
            Ui.Text(dl, p + new Vector2(S(42) + tw, (h - LH * 0.85f) / 2), count.ToString(), Theme.Text3, 0.85f);
            if (done) Ui.Icon(dl, p + new Vector2(w - S(26), (h - LH) / 2 + S(1)), FontAwesomeIcon.CheckCircle, Theme.Live, 0.9f);
            Advance(p, h + S(6));
            return open;
        }

        private void DrawCrystals(List<Mat> list, float w)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            float ic = S(26), cellH = ic + S(10), x = p.X, y = p.Y;
            foreach (var m in list)
            {
                var txt = $"{m.Have}/{m.Eff}";
                float cw = ic + S(18) + Ui.TextSize(txt, 0.9f).X;
                if (x + cw > p.X + w && x > p.X) { x = p.X; y += cellH + S(6); }
                var cp = new Vector2(x, y);
                Ui.Hit("cf_cr" + m.Id, cp, new Vector2(cw, cellH), out float h);
                Ui.Tooltip(m.Name);
                dl.AddRectFilled(cp, cp + new Vector2(cw, cellH), (m.Done ? Theme.Live.With(0.1f) : Theme.Night2.With(0.6f + 0.3f * h)).U32(), cellH / 2);
                dl.AddRect(cp, cp + new Vector2(cw, cellH), (m.Done ? Theme.Live.With(0.45f) : Theme.CrystalLine).U32(), cellH / 2, 0, S(1));
                GameIcon(m.Icon, cp + S(6, 5), ic);
                Ui.Text(dl, cp + new Vector2(ic + S(10), (cellH - LH * 0.9f) / 2), txt, m.Done ? Theme.Live : Theme.Text, 0.9f);
                x += cw + S(6);
            }
            Advance(p, y - p.Y + cellH + S(8));
        }

        private void DrawMatRow(Mat m, float w)
        {
            var dl = Ui.Dl;
            var p = ImGui.GetCursorScreenPos();
            float rowH = S(14) + LH * 2;
            if (!ImGui.IsRectVisible(new Vector2(w, rowH))) { Advance(p, rowH + S(5)); return; }

            var line = m.Done ? Theme.Live.With(0.45f) : m.Ready ? Ui.Accent.With(0.6f) : Theme.CrystalLine;
            Ui.Panel(dl, p, p + new Vector2(w, rowH), m.Done ? Theme.Live.With(0.06f) : Theme.Night2.With(0.6f), line, 6f);
            float alpha = m.Done ? 0.55f : 1f;
            float ic = rowH - S(14);
            GameIcon(m.Icon, p + S(7, 7), ic, alpha);

            // right side: buttons, method chip, count
            float b = S(26), right = p.X + w - S(8), by = p.Y + (rowH - b) / 2;
            bool gatherable = m.Cat is "gathering" or "timed" or "crystals";
            if (!m.Crafted || m.Cat == "precraft")
            {
                if (Ui.IconButton("cf_cart" + m.Id, FontAwesomeIcon.ShoppingCart, new Vector2(right - b, by), b, "Add to the shopping list"))
                    MarketApp.OnAddToCart?.Invoke(new CartItem { id = (int)m.Id, name = m.Name, icon = IconPath(m.Icon), quantity = Math.Max(1, m.Eff - m.Have), hq = false });
                right -= b + S(4);
            }
            if (gatherable && m.Aetheryte != 0)
            {
                if (Ui.IconButton("cf_tp" + m.Id, FontAwesomeIcon.MapMarkerAlt, new Vector2(right - b, by), b, "Teleport to the nearest aetheryte"))
                    GatheringApp.TeleportToAetheryte(m.Aetheryte);
                right -= b + S(4);
            }
            if (gatherable)
            {
                bool onRoute = _configuration.GatheringActiveRoute.Any(r => r.ItemId == m.Id);
                if (Ui.IconButton("cf_rt" + m.Id, FontAwesomeIcon.Route, new Vector2(right - b, by), b, onRoute ? "On your gathering route" : "Add to the gathering route", null, onRoute) && !onRoute)
                {
                    _configuration.GatheringActiveRoute.Add(new RouteItem { ItemId = m.Id, TargetQuantity = Math.Max(1, m.Eff - m.Have) });
                    _configuration.Save();
                }
                right -= b + S(4);
            }
            if (m.Cat != "final")
            {
                string label = MethodLabel(m);
                var ts = Ui.TextSize(label, 0.8f);
                var cs = ts + S(14, 6);
                var cp = new Vector2(right - cs.X - S(4), p.Y + (rowH - cs.Y) / 2);
                if (Ui.Hit("cf_mt" + m.Id, cp, cs, out float hv)) ToggleMethod(m);
                Ui.Tooltip(m.Crafted ? "Craft it yourself or buy it finished (shared with the website)" : "Gather it or buy it (shared with the website)");
                var mc = label == "Buy" ? Theme.Warn : label == "Craft" ? Theme.Violet : Theme.Live;
                dl.AddRectFilled(cp, cp + cs, mc.With(0.12f + 0.12f * hv).U32(), cs.Y / 2);
                dl.AddRect(cp, cp + cs, mc.With(0.45f).U32(), cs.Y / 2, 0, S(1));
                Ui.Text(dl, cp + S(7, 3), label, mc, 0.8f);
                right = cp.X - S(6);
            }
            var cnt = $"{m.Have}/{m.Eff}";
            var cts = Ui.TextSize(cnt);
            Ui.Text(dl, new Vector2(right - cts.X, p.Y + (rowH - cts.Y) / 2), cnt, m.Done ? Theme.Live : Theme.Text);
            if (m.Done) Ui.Icon(dl, new Vector2(right - cts.X - S(22), p.Y + (rowH - LH) / 2 + S(1)), FontAwesomeIcon.Check, Theme.Live, 0.85f);
            right -= cts.X + S(30);

            // left: name, stars, details
            float tx = p.X + ic + S(16), tw = right - tx;
            var name = m.Name + (m.Stars > 0 ? " " + new string('*', m.Stars) : "");
            Ui.Text(dl, new Vector2(tx, p.Y + S(6)), Fit(name, tw), Theme.Text.With(alpha));
            var bits = new List<string>();
            if (m.Times.Length > 0) bits.Add(m.Times);
            if (m.Cat is "tomes" or "drops" && m.Zone.Length > 0) bits.Add(m.Zone);
            if (m.Crafted && !m.Done) bits.Add(m.Ready ? "Ready to craft" : "Missing materials");
            int price = PriceOf(m);
            if (price > 0 && !m.Crafted) bits.Add($"{price:N0} g each");
            if (bits.Count > 0)
                Ui.Text(dl, new Vector2(tx, p.Y + S(8) + LH), Fit(string.Join(" · ", bits), tw, 0.85f), m.Ready && !m.Done ? Ui.Accent : Theme.Text3, 0.85f);
            Advance(p, rowH + S(5));
        }

        private static string MethodLabel(Mat m) => m.Crafted
            ? (m.Method == "Buy Finished" ? "Buy" : "Craft")
            : (m.Method == "Buy Raw" || (m.Method == null && m.Cat is "tomes" or "drops") ? "Buy" : "Gather");

        private void ToggleMethod(Mat m)
        {
            string next = m.Crafted ? (MethodLabel(m) == "Craft" ? "Buy Finished" : "Craft") : (MethodLabel(m) == "Gather" ? "Buy Raw" : "Gather");
            if (_items[m.Id.ToString()] is JObject o) { o["acquisitionMethod"] = next; WritePipeline(rebuild: false); }
        }

        private int PriceOf(Mat m) => _priceCache.TryGetValue(m.Id, out var p) && p > 0 ? p : m.Price;

        // =============================================================================================================
        // Model: website items -> Mat list; live counts; rebuilding the item tree from game data
        // =============================================================================================================
        private void BuildRollup()
        {
            var list = new List<Mat>();
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            foreach (var prop in _items.Properties())
            {
                if (prop.Value is not JObject o || !uint.TryParse(prop.Name, out var id)) continue;
                var row = itemSheet.GetRowOrDefault(id);
                var m = new Mat
                {
                    Id = id,
                    Name = Str(o, "name") is { Length: > 0 } n ? n : row?.Name.ToString() ?? $"Item {id}",
                    Icon = row.HasValue ? row.Value.Icon : 0u,
                    Cat = Str(o, "category"),
                    Tier = Int(o, "tier"),
                    Req = Int(o, "quantityRequired"),
                    Completed = Int(o, "completed"),
                    Yield = Math.Max(1, Int(o, "yield", 1)),
                    Stars = Int(o, "stars"),
                    Price = Int(o, "unitPrice"),
                    Zone = Str(o, "zone"),
                    Times = o["time"] is JArray ta ? string.Join(", ", ta.Select(x => x.ToString())) : "",
                    Method = Str(o, "acquisitionMethod") is { Length: > 0 } am ? am : null,
                };
                if (o["ingredients"] is JArray ings)
                    foreach (var ing in ings.OfType<JObject>()) m.Ings.Add(((uint)Int(ing, "itemId"), Int(ing, "amount")));
                if (m.Cat is "gathering" or "timed")
                {
                    var node = NodeInfo(id);
                    if (m.Zone.Length == 0) m.Zone = node.Zone;
                    if (m.Times.Length == 0) m.Times = node.Times;
                    m.Aetheryte = node.Aetheryte;
                }
                if (m.Zone.Length == 0 && m.Cat is "gathering" or "timed") m.Zone = "Unknown zone";
                list.Add(m);
            }
            _rollup = list;
            _countsAt = DateTime.MinValue;
        }

        private unsafe int Bag(uint itemId)
        {
            var mgr = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (mgr == null) return 0;
            return mgr->GetInventoryItemCount(itemId, false, false, false, 0) + mgr->GetInventoryItemCount(itemId, true, false, false, 0);
        }

        /// <summary>Bag counts, what is still needed (held pre-crafts lower their ingredients) and ready-to-craft. Once a second.</summary>
        private void RefreshCounts()
        {
            if (_rollup == null || (DateTime.Now - _countsAt).TotalSeconds < 1) return;
            _countsAt = DateTime.Now;
            var byId = _rollup.ToDictionary(m => m.Id);
            foreach (var m in _rollup)
            {
                m.Inv = Bag(m.Id);
                m.Have = Math.Max(m.Inv, m.Completed);
                m.Eff = 0;
            }
            foreach (var t in _targets)
                if (byId.TryGetValue((uint)Int(t, "itemId"), out var fm)) fm.Eff += Math.Max(1, Int(t, "quantity", 1));
            foreach (var m in _rollup.Where(x => x.Cat == "final" && x.Eff == 0)) m.Eff = m.Req;

            foreach (var m in _rollup.Where(x => x.Crafted).OrderByDescending(x => x.Cat == "final" ? 999 : x.Tier))
            {
                int toCraft = m.Method == "Buy Finished" ? 0 : Math.Max(0, m.Eff - m.Have);
                int crafts = (toCraft + m.Yield - 1) / m.Yield;
                bool ready = toCraft > 0;
                foreach (var (ing, amt) in m.Ings)
                {
                    if (byId.TryGetValue(ing, out var im)) im.Eff += amt * crafts;
                    if (Bag(ing) < amt * crafts) ready = false;
                }
                m.Ready = ready;
            }

            // What the bags hold counts as done on the website too (only ever raised, never lowered).
            bool dirty = false;
            foreach (var m in _rollup)
            {
                int c = Math.Min(m.Inv, m.Req);
                if (c > m.Completed && _items[m.Id.ToString()] is JObject o) { o["completed"] = c; m.Completed = c; dirty = true; }
            }
            if (dirty && (DateTime.Now - _completedWriteAt).TotalSeconds > 5)
            {
                _completedWriteAt = DateTime.Now;
                var keep = _rollup;
                WritePipeline(rebuild: false);
                _rollup = keep;
            }
        }

        private void InitRecipeCache()
        {
            if (_recipeIdByResult != null) return;
            _recipeIdByResult = new Dictionary<uint, uint>();
            foreach (var r in _dataManager.GetExcelSheet<Recipe>())
                if (r.ItemResult.RowId != 0 && !_recipeIdByResult.ContainsKey(r.ItemResult.RowId)) _recipeIdByResult[r.ItemResult.RowId] = r.RowId;
        }

        private bool IsVendorItem(uint itemId)
        {
            if (_vendorItems == null)
            {
                _vendorItems = new HashSet<uint>();
                try
                {
                    foreach (var shop in _dataManager.GetSubrowExcelSheet<GilShopItem>())
                        foreach (var s in shop) if (s.Item.RowId != 0) _vendorItems.Add(s.Item.RowId);
                }
                catch (Exception ex) { _log.Debug($"[Aetherhold] vendor list failed: {ex.Message}"); }
            }
            return _vendorItems.Contains(itemId);
        }

        private (string Zone, string Times, int Stars, uint Aetheryte, bool Found) NodeInfo(uint itemId)
        {
            string zone = "", times = ""; int stars = 0; bool found = false;
            var node = GatheringApp.Nodes.FirstOrDefault(n => n.ItemId == itemId);
            if (node != null)
            {
                found = true;
                zone = node.zone ?? "";
                if (node.hours != null && node.hours.Count > 0) times = string.Join(", ", node.hours.Select(h => $"{h:00}:00"));
                stars = node.stats?.stars ?? 0;
            }
            var dn = GatheringApp.DataNodesMap.Values.FirstOrDefault(n => (n.items != null && n.items.Contains((int)itemId)) || (n.hiddenItems != null && n.hiddenItems.Contains((int)itemId)));
            if (dn != null)
            {
                found = true;
                if (zone.Length == 0 && dn.zoneid > 0)
                {
                    var place = _dataManager.GetExcelSheet<PlaceName>().GetRowOrDefault((uint)dn.zoneid);
                    if (place.HasValue) zone = place.Value.Name.ToString();
                }
                if (!_aetheryteOf.ContainsKey(itemId) && dn.map > 0)
                {
                    var map = _dataManager.GetExcelSheet<Map>().GetRowOrDefault((uint)dn.map);
                    uint aet = 0;
                    if (map.HasValue)
                    {
                        var terr = map.Value.TerritoryType.RowId;
                        var a = _dataManager.GetExcelSheet<Aetheryte>().FirstOrDefault(x => x.IsAetheryte && x.Territory.RowId == terr);
                        if (a.RowId != 0) aet = a.RowId;
                    }
                    _aetheryteOf[itemId] = aet;
                }
            }
            return (zone, times, stars, _aetheryteOf.GetValueOrDefault(itemId), found);
        }

        /// <summary>Rebuilds crafting.pipeline_v2 items from the targets with game data (the website's shape), keeping progress, methods and prices.</summary>
        private void RebuildItems()
        {
            var recipes = _dataManager.GetExcelSheet<Recipe>();
            var itemSheet = _dataManager.GetExcelSheet<Item>();
            InitRecipeCache();
            var acc = new Dictionary<uint, JObject>();

            int Walk(uint itemId, int need, uint recipeId, bool isTarget, int depth)
            {
                var item = itemSheet.GetRowOrDefault(itemId);
                if (!item.HasValue || depth > 12) return 0;
                if (!acc.TryGetValue(itemId, out var e))
                {
                    e = new JObject { ["itemId"] = itemId, ["name"] = item.Value.Name.ToString(), ["icon"] = IconPath(item.Value.Icon), ["quantityRequired"] = 0, ["completed"] = 0, ["tier"] = 0 };
                    acc[itemId] = e;
                }
                e["quantityRequired"] = Int(e, "quantityRequired") + need;
                if (recipeId == 0 && !isTarget) recipeId = _recipeIdByResult!.GetValueOrDefault(itemId);
                var r = recipeId != 0 ? recipes.GetRowOrDefault(recipeId) : null;
                if (r.HasValue && item.Value.ItemUICategory.RowId != 59)
                {
                    int yield = Math.Max(1, (int)r.Value.AmountResult);
                    int crafts = (need + yield - 1) / yield;
                    var rl = r.Value.RecipeLevelTable.Value;
                    e["category"] = isTarget ? "final" : "precraft";
                    e["yield"] = yield; e["isCraftable"] = true; e["job"] = JobOf(r.Value); e["level"] = (int)rl.ClassJobLevel;
                    var ings = new JArray();
                    int tier = 0;
                    for (int i = 0; i < r.Value.Ingredient.Count; i++)
                    {
                        uint ing = r.Value.Ingredient[i].RowId;
                        int amt = r.Value.AmountIngredient[i];
                        if (ing == 0 || amt <= 0) continue;
                        ings.Add(new JObject { ["itemId"] = ing, ["amount"] = amt });
                        tier = Math.Max(tier, Walk(ing, amt * crafts, 0, false, depth + 1));
                    }
                    e["ingredients"] = ings;
                    e["tier"] = Math.Max(Int(e, "tier"), tier + 1);
                }
                else if (e["category"] == null)
                {
                    if (item.Value.ItemUICategory.RowId == 59) e["category"] = "crystals";
                    else
                    {
                        var node = NodeInfo(itemId);
                        if (node.Found)
                        {
                            e["category"] = node.Times.Length > 0 ? "timed" : "gathering";
                            if (node.Zone.Length > 0) e["zone"] = node.Zone;
                            if (node.Times.Length > 0) e["time"] = new JArray(node.Times.Split(", "));
                            if (node.Stars > 0) e["stars"] = node.Stars;
                        }
                        else e["category"] = IsVendorItem(itemId) ? "tomes" : "drops";
                    }
                }
                return Int(e, "tier");
            }

            foreach (var t in _targets) Walk((uint)Int(t, "itemId"), Math.Max(1, Int(t, "quantity", 1)), TargetRecipe(t), true, 0);

            var old = _items;
            var fresh = new JObject();
            foreach (var (id, e) in acc)
            {
                if (old[id.ToString()] is JObject o)
                {
                    e["completed"] = Math.Min(Int(o, "completed"), Int(e, "quantityRequired"));
                    if (o["acquisitionMethod"] is JToken am && am.Type == JTokenType.String) e["acquisitionMethod"] = am;
                    if (o["unitPrice"] is JToken up && up.Type != JTokenType.Null) e["unitPrice"] = up;
                }
                fresh[id.ToString()] = e;
            }
            _items = fresh;
        }

        // =============================================================================================================
        // Prices (Universalis, in the market the website uses)
        // =============================================================================================================
        private async void FetchPrices(List<uint> ids)
        {
            if (_isFetchingPrices || ids.Count == 0) return;
            _isFetchingPrices = true;
            string scope = ((_sender.GetState(SecScope) as JValue)?.Value as string ?? "").Trim();
            if (scope.Length == 0) scope = "Europe";
            try
            {
                for (int i = 0; i < ids.Count; i += 50)
                {
                    var chunk = ids.Skip(i).Take(50).ToList();
                    var url = $"https://universalis.app/api/v2/{Uri.EscapeDataString(scope)}/{string.Join(",", chunk)}?listings=0&entries=0";
                    var res = await _httpClient.GetAsync(url);
                    if (!res.IsSuccessStatusCode) continue;
                    var j = JObject.Parse(await res.Content.ReadAsStringAsync());
                    if (j["items"] is JObject map)
                    {
                        foreach (var prop in map.Properties())
                            if (uint.TryParse(prop.Name, out var id)) _priceCache[id] = Int(prop.Value, "minPrice");
                    }
                    else if (chunk.Count == 1) _priceCache[chunk[0]] = Int(j, "minPrice");
                }
                _pricesFresh = true;
            }
            catch (Exception ex) { _log.Debug($"[Aetherhold] Universalis prices failed: {ex.Message}"); }
            finally { _isFetchingPrices = false; }
        }

        /// <summary>Main thread: material prices go into the shared pipeline so the website shows them too.</summary>
        private void ApplyPrices()
        {
            _pricesFresh = false;
            bool dirty = false;
            foreach (var prop in _items.Properties())
            {
                if (prop.Value is not JObject o || Str(o, "category") == "final" || !uint.TryParse(prop.Name, out var id)) continue;
                if (_priceCache.TryGetValue(id, out var p) && p > 0 && Int(o, "unitPrice") != p) { o["unitPrice"] = p; dirty = true; }
            }
            if (dirty) WritePipeline(rebuild: false);
            BuildRollup();
        }
    }
}
