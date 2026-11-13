using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Item prices: head, stats, add-to-cart bar, worlds, listings and sales, price history
    public partial class MarketApp
    {
        private int _detailQty = 1;
        private DateTime _detailAddedAt = DateTime.MinValue;

        private static readonly Dictionary<int, string> WorldNames = new()
        {
            { 21, "Ravana" }, { 22, "Bismarck" }, { 23, "Asura" }, { 24, "Belias" }, { 28, "Pandaemonium" }, { 29, "Shinryu" }, { 30, "Unicorn" },
            { 31, "Yojimbo" }, { 32, "Zeromus" }, { 33, "Twintania" }, { 34, "Brynhildr" }, { 35, "Famfrit" }, { 36, "Lich" }, { 37, "Mateus" },
            { 39, "Omega" }, { 40, "Jenova" }, { 41, "Zalera" }, { 42, "Zodiark" }, { 43, "Alexander" }, { 44, "Anima" }, { 45, "Carbuncle" },
            { 46, "Fenrir" }, { 47, "Hades" }, { 48, "Ixion" }, { 49, "Kujata" }, { 50, "Typhon" }, { 51, "Ultima" }, { 52, "Valefor" },
            { 53, "Exodus" }, { 54, "Faerie" }, { 55, "Lamia" }, { 56, "Phoenix" }, { 57, "Siren" }, { 58, "Garuda" }, { 59, "Ifrit" },
            { 60, "Ramuh" }, { 61, "Titan" }, { 62, "Diabolos" }, { 63, "Gilgamesh" }, { 64, "Leviathan" }, { 65, "Midgardsormr" }, { 66, "Odin" },
            { 67, "Shiva" }, { 68, "Atomos" }, { 69, "Bahamut" }, { 70, "Chocobo" }, { 71, "Moogle" }, { 72, "Tonberry" }, { 73, "Adamantoise" },
            { 74, "Coeurl" }, { 75, "Malboro" }, { 76, "Tiamat" }, { 77, "Ultros" }, { 78, "Behemoth" }, { 79, "Cactuar" }, { 80, "Cerberus" },
            { 81, "Goblin" }, { 82, "Mandragora" }, { 83, "Louisoix" }, { 85, "Spriggan" }, { 86, "Sephirot" }, { 87, "Sophia" }, { 88, "Zurvan" },
            { 90, "Aegis" }, { 91, "Balmung" }, { 92, "Durandal" }, { 93, "Excalibur" }, { 94, "Gungnir" }, { 95, "Hyperion" }, { 96, "Masamune" },
            { 97, "Ragnarok" }, { 98, "Ridill" }, { 99, "Sargatanas" }, { 400, "Sagittarius" }, { 401, "Phantom" }, { 402, "Alpha" }, { 403, "Raiden" },
            { 404, "Marilith" }, { 405, "Seraph" }, { 406, "Halicarnassus" }, { 407, "Maduin" }, { 408, "Cuchulainn" }, { 409, "Kraken" },
            { 410, "Rafflesia" }, { 411, "Golem" },
        };

        private void DrawDetail()
        {
            EnsureDetailLoaded();
            var item = _selected!;
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("mk_detail", avail))
            {
                float w = avail.X - S(10);
                var p = ImGui.GetCursorScreenPos();
                float y = p.Y;
                y = DrawItemHead(new Vector2(p.X, y), w, item) + S(10);
                if (_selected == null) { ImGui.EndChild(); return; } // back pressed

                var d = _data;
                if (_loadingData)
                {
                    Ui.Icon(Ui.Dl, new Vector2(p.X, y), FontAwesomeIcon.Sync, Ui.Accent);
                    Ui.Text(Ui.Dl, new Vector2(p.X + S(26), y), "Asking Universalis...", Theme.Text2);
                    y += LH + S(10);
                }
                else if (_dataError != null) { Message(new Vector2(p.X, y), w, _dataError, Theme.Bad); y += LH + S(30); }

                if (d != null && !_loadingData)
                {
                    bool canHq = item.canBeHq != false;
                    y = DrawStats(new Vector2(p.X, y), w, d, canHq) + S(10);
                    y = DrawAddBar(new Vector2(p.X, y), w, item, canHq) + S(12);
                    y = DrawWorlds(new Vector2(p.X, y), w, d) + S(4);

                    bool two = w >= S(820);
                    float cw = two ? (w - S(12)) / 2 : w;
                    var hq = d.listings.Where(l => l.hq).OrderBy(l => l.pricePerUnit).ToList();
                    var nq = d.listings.Where(l => !l.hq).OrderBy(l => l.pricePerUnit).ToList();
                    var hqS = d.recentHistory.Where(s => s.hq).ToList();
                    var nqS = d.recentHistory.Where(s => !s.hq).ToList();
                    float ly = y, sy = y;
                    if (canHq) ly = DrawTable(new Vector2(p.X, ly), cw, "HQ listings", hq, null, d) + S(10);
                    ly = DrawTable(new Vector2(p.X, ly), cw, "NQ listings", nq, null, d) + S(10);
                    float sx = two ? p.X + cw + S(12) : p.X;
                    if (!two) sy = ly;
                    if (canHq) sy = DrawTable(new Vector2(sx, sy), cw, "HQ sales", null, hqS, d) + S(10);
                    sy = DrawTable(new Vector2(sx, sy), cw, "NQ sales", null, nqS, d) + S(10);
                    y = Math.Max(ly, sy);
                    y = DrawHistory(new Vector2(p.X, y), w, d, canHq) + S(10);
                }
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private float DrawItemHead(Vector2 p, float w, MarketSearchItem item)
        {
            var dl = Ui.Dl;
            float pad = S(12), ic = S(48);
            float bh = S(30);
            bool oneRow = w >= S(760);
            float nameScale = 1.3f;
            float nameH = Ui.TextSize("Ag", nameScale).Y;
            float chipH = ChipH(0.82f) - S(2);
            float infoH = Math.Max(ic, nameH + S(6) + chipH);
            float h = pad + bh + S(10) + infoH + (oneRow ? 0 : S(10) + bh) + S(10) + ChipH() + pad;
            // scope chips may wrap: measure first
            var opts = ScopeOptions();
            {
                var probe = new Vector2(p.X + pad, 0);
                foreach (var o in opts)
                {
                    float cw = Ui.TextSize(o.Short, 0.85f).X + S(16);
                    if (probe.X + cw > p.X + w - pad && probe.X > p.X + pad) { probe = new Vector2(p.X + pad, probe.Y + ChipH() + S(6)); }
                    probe.X += cw + S(6);
                }
                h += probe.Y;
            }
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.75f));

            var a = p + new Vector2(pad, pad);
            if (Ui.Button("mk_back", "Results", a, new Vector2(S(110), bh), Kind.Ghost, FontAwesomeIcon.ArrowLeft)) { _selected = null; _data = null; return p.Y + h; }

            // action buttons: top right on wide screens, own row on narrow ones
            var bRow = oneRow ? new Vector2(p.X + w - pad, a.Y) : new Vector2(p.X + w - pad, a.Y + bh + S(10) + infoH + S(10));
            float rx = bRow.X;
            if (Ui.IconButton("mk_reload", FontAwesomeIcon.Sync, new Vector2(rx - bh, bRow.Y), bh, "Reload prices") && !_loadingData) LoadData(item.id, Scope);
            rx -= bh + S(6);
            bool fav = IsFav(item.id);
            string favLabel = fav ? "Favourite" : "Add favourite";
            float fw = Ui.TextSize(favLabel).X + S(46);
            if (Ui.Button("mk_dfav", favLabel, new Vector2(rx - fw, bRow.Y), new Vector2(fw, bh), fav ? Kind.Gold : Kind.Ghost, FontAwesomeIcon.Star))
                ToggleFav(item.id, item.name, item.icon, item.canBeHq, item.level, item.ilvl);
            rx -= fw + S(6);
            float gw = Ui.TextSize("Garland").X + S(46);
            if (Ui.Button("mk_garland", "Garland", new Vector2(rx - gw, bRow.Y), new Vector2(gw, bh), Kind.Ghost, FontAwesomeIcon.ExternalLinkAlt))
                Dalamud.Utility.Util.OpenLink("https://garlandtools.org/db/#item/" + item.id);
            Ui.Tooltip("Open on Garland Tools");
            rx -= gw + S(6);
            float uw = Ui.TextSize("Universalis").X + S(46);
            if (Ui.Button("mk_univ", "Universalis", new Vector2(rx - uw, bRow.Y), new Vector2(uw, bh), Kind.Ghost, FontAwesomeIcon.ExternalLinkAlt))
                Dalamud.Utility.Util.OpenLink("https://universalis.app/market/" + item.id);
            Ui.Tooltip("Open on Universalis");

            // icon, name, chips
            var ia = a + new Vector2(0, bh + S(10));
            GameIcon(item.icon, ia + new Vector2(0, (infoH - ic) / 2), ic);
            float tx = ia.X + ic + S(14), tw = p.X + w - pad - tx;
            Ui.Text(dl, new Vector2(tx, ia.Y), Fit(item.name, tw, nameScale), Theme.Text, nameScale);
            var cp = new Vector2(tx, ia.Y + nameH + S(6));
            void ChipAt(string text, Vector4 col)
            {
                Ui.Chip(dl, cp, text, col, out var cs);
                cp.X += cs.X + S(6);
            }
            if (item.level is > 0) ChipAt($"Lv. {item.level}", Theme.Text2);
            if (item.ilvl is > 1) ChipAt($"iLvl {item.ilvl}", Theme.Text2);
            ChipAt(item.canBeHq != false ? "HQ possible" : "No HQ", item.canBeHq != false ? Ui.Accent : Theme.Text3);
            if (_data is { lastUploadTime: > 0 } d && !_loadingData) ChipAt($"Updated {AgoMs(d.lastUploadTime)}", Theme.Text2);

            // scope chips
            float sy = ia.Y + infoH + S(10) + (oneRow ? 0 : bh + S(10));
            var at = new Vector2(p.X + pad, sy);
            var scope = Scope;
            foreach (var o in opts)
            {
                if (ChipToggle("mk_sc_" + o.Value, o.Short, o.Value == scope, ref at, p.X + pad, p.X + w - pad)) SetScope(o.Value);
                Ui.Tooltip(o.Label);
            }
            if (!opts.Any(o => o.Value == scope)) ChipToggle("mk_sc_cur", ScopeShort(scope), true, ref at, p.X + pad, p.X + w - pad);
            return p.Y + h;
        }

        private float DrawStats(Vector2 p, float w, MarketData d, bool canHq)
        {
            var hq = d.listings.Where(l => l.hq).ToList();
            var nq = d.listings.Where(l => !l.hq).ToList();
            var hqS = d.recentHistory.Where(s => s.hq).ToList();
            var nqS = d.recentHistory.Where(s => !s.hq).ToList();
            static float Avg(IEnumerable<int> xs) { var l = xs.ToList(); return l.Count > 0 ? (float)l.Average() : 0; }
            float listAvg = d.currentAveragePrice > 0 ? d.currentAveragePrice : Avg(d.listings.Select(l => l.pricePerUnit));
            float listAvgHq = d.currentAveragePriceHQ > 0 ? d.currentAveragePriceHQ : Avg(hq.Select(l => l.pricePerUnit));
            float listAvgNq = d.currentAveragePriceNQ > 0 ? d.currentAveragePriceNQ : Avg(nq.Select(l => l.pricePerUnit));
            float saleAvg = d.averagePrice > 0 ? d.averagePrice : Avg(d.recentHistory.Select(s => s.pricePerUnit));
            float saleAvgHq = d.averagePriceHQ > 0 ? d.averagePriceHQ : Avg(hqS.Select(s => s.pricePerUnit));
            float saleAvgNq = d.averagePriceNQ > 0 ? d.averagePriceNQ : Avg(nqS.Select(s => s.pricePerUnit));
            int units = d.listings.Sum(l => l.quantity);

            var cards = new List<(string label, string value, string sub, bool gold)>();
            string Cheap(List<MarketListing> l, out string value)
            {
                var c = l.OrderBy(x => x.pricePerUnit).FirstOrDefault();
                if (c == null) { value = "-"; return "no listings"; }
                value = Gil(c.pricePerUnit);
                return $"{c.quantity} x on {(c.worldName.Length > 0 ? c.worldName : d.worldName ?? "-")}  ·  total {Gil((long)c.pricePerUnit * c.quantity)}";
            }
            if (canHq) { var s = Cheap(hq, out var v); cards.Add(("Cheapest HQ", v, s, false)); }
            else cards.Add(("Cheapest HQ", "-", "This item has no HQ.", false));
            { var s = Cheap(nq, out var v); cards.Add(("Cheapest NQ", v, s, false)); }
            cards.Add(("Listings avg / unit", Gil((long)listAvg), $"{(canHq ? $"HQ {Gil((long)listAvgHq)} · " : "")}NQ {Gil((long)listAvgNq)} · {Plural(d.listings.Count, "listing")}, {Plural(units, "unit")}", true));
            cards.Add(("Sales avg / unit", Gil((long)saleAvg), $"{(canHq ? $"HQ {Gil((long)saleAvgHq)} · " : "")}NQ {Gil((long)saleAvgNq)}{(d.regularSaleVelocity > 0 ? $" · {d.regularSaleVelocity:0.#} sold/day" : "")}", true));

            int cols = w >= S(900) ? 4 : 2;
            float gap = S(10), cw = (w - gap * (cols - 1)) / cols;
            float capsH = Ui.TextSize("A", 0.78f).Y, valH = Ui.TextSize("0", 1.4f).Y, subH = Ui.TextSize("A", 0.82f).Y;
            float ch = S(12) + capsH + S(6) + valH + S(4) + subH + S(12);
            var dl = Ui.Dl;
            for (int i = 0; i < cards.Count; i++)
            {
                var (label, value, sub, gold) = cards[i];
                var a = p + new Vector2((i % cols) * (cw + gap), (i / cols) * (ch + gap));
                Ui.Panel(dl, a, a + new Vector2(cw, ch), Theme.Night2.With(0.6f));
                Ui.Caps(dl, a + S(12, 12), label, Theme.Text2);
                var vp = a + new Vector2(S(12), S(12) + capsH + S(6));
                Ui.Text(dl, vp, value, gold ? Theme.Gold : Theme.Text, 1.4f);
                if (value != "-") Ui.Text(dl, vp + new Vector2(Ui.TextSize(value, 1.4f).X + S(5), valH - subH - S(2)), "gil", Theme.Text3, 0.82f);
                Ui.Text(dl, vp + new Vector2(0, valH + S(4)), Fit(sub, cw - S(24), 0.82f), Theme.Text3, 0.82f);
            }
            int rows = (cards.Count + cols - 1) / cols;
            return p.Y + rows * ch + (rows - 1) * gap;
        }

        private float DrawAddBar(Vector2 p, float w, MarketSearchItem item, bool canHq)
        {
            var dl = Ui.Dl;
            float bh = S(30);
            float addW = S(84);
            float controls = StepperW + S(12) + (canHq ? CheckW("HQ") + S(12) : 0) + addW;
            bool oneRow = w - controls - S(40) >= S(200);
            float h = oneRow ? bh + S(20) : LH + S(12) + bh + S(20);
            Ui.Panel(dl, p, p + new Vector2(w, h), Ui.Accent.With(0.06f), Ui.Accent.With(0.3f));
            float labelW = oneRow ? w - controls - S(40) : w - S(28);
            Ui.Icon(dl, p + new Vector2(S(14), oneRow ? (h - LH) / 2 : S(10)), FontAwesomeIcon.ShoppingCart, Ui.Accent, 0.9f);
            Ui.Text(dl, p + new Vector2(S(38), oneRow ? (h - LH) / 2 : S(10)), Fit("Add to cart for bulk buying and a travel route", labelW - S(24)), Theme.Text2);

            float cy = oneRow ? p.Y + (h - bh) / 2 : p.Y + LH + S(20);
            float x = oneRow ? p.X + w - S(14) - controls : p.X + S(14);
            Stepper("mk_dq", ref _detailQty, new Vector2(x, cy + (bh - S(26)) / 2));
            x += StepperW + S(12);
            if (canHq)
            {
                bool hqOn = AddHq;
                if (Check("mk_dhq", "HQ", ref hqOn, new Vector2(x, cy + (bh - Math.Max(S(18), LH)) / 2))) SetAddHq(hqOn);
                Ui.Tooltip("Only buy high quality");
                x += CheckW("HQ") + S(12);
            }
            bool added = (DateTime.Now - _detailAddedAt).TotalSeconds < 1.6;
            if (Ui.Button("mk_dadd", added ? "Added" : "Add", new Vector2(x, cy), new Vector2(addW, bh), Kind.Gold, added ? FontAwesomeIcon.Check : FontAwesomeIcon.CartPlus))
            {
                AddToCart(item.id, item.name, item.icon, item.canBeHq, item.level, item.ilvl, _detailQty, canHq && AddHq);
                _detailAddedAt = DateTime.Now;
            }
            return p.Y + h;
        }

        /// <summary>One cell per world in the scope: cheapest price there and Universalis' last upload. Click narrows the scope.</summary>
        private float DrawWorlds(Vector2 p, float w, MarketData d)
        {
            if (d.worldUploadTimes.Count <= 1) return p.Y;
            var dl = Ui.Dl;
            Ui.Caps(dl, p, "Worlds in this scope");
            float y = p.Y + Ui.TextSize("A", 0.78f).Y + S(8);
            var cheapest = d.listings.Where(l => l.worldName.Length > 0).GroupBy(l => l.worldName).ToDictionary(g => g.Key, g => g.Min(l => l.pricePerUnit));
            var worlds = d.worldUploadTimes.Select(kv => (name: WorldNames.TryGetValue(kv.Key, out var n) ? n : $"World {kv.Key}", time: kv.Value))
                .OrderBy(x => cheapest.TryGetValue(x.name, out var c) ? c : int.MaxValue).ThenBy(x => x.name).ToList();
            float gap = S(8);
            int cols = Math.Max(1, (int)((w + gap) / (S(150) + gap)));
            float cw = (w - gap * (cols - 1)) / cols, ch = LH + Ui.TextSize("A", 0.82f).Y * 2 + S(20);
            for (int i = 0; i < worlds.Count; i++)
            {
                var (name, time) = worlds[i];
                var a = new Vector2(p.X + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap));
                bool clicked = Ui.Hit("mk_w" + name, a, new Vector2(cw, ch), out float hv);
                Ui.Tooltip($"Show prices on {name} only");
                bool home = name == _homeWorld;
                Ui.Panel(dl, a, a + new Vector2(cw, ch), Theme.Night2.With(0.55f + 0.3f * hv), (home ? Theme.Gold : Ui.Accent).With(home ? 0.5f : 0.15f + 0.35f * hv), 6f);
                Ui.Text(dl, a + S(10, 8), Fit(name, cw - S(20)), home ? Theme.Gold : Theme.Text);
                float sy = a.Y + S(8) + LH + S(2);
                Ui.Text(dl, new Vector2(a.X + S(10), sy), cheapest.TryGetValue(name, out var c) ? $"from {Gil(c)} gil" : "no listings", cheapest.ContainsKey(name) ? Theme.Gold : Theme.Text3, 0.82f);
                Ui.Text(dl, new Vector2(a.X + S(10), sy + Ui.TextSize("A", 0.82f).Y), $"updated {AgoMs(time)}", Theme.Text3, 0.82f);
                if (clicked) SetScope(name);
            }
            int rows = (worlds.Count + cols - 1) / cols;
            return y + rows * ch + (rows - 1) * gap + S(10);
        }

        /// <summary>Listings (price, qty, total, world, retainer) or sales (price, qty, world, buyer, when); first 50.</summary>
        private float DrawTable(Vector2 p, float w, string title, List<MarketListing>? listings, List<MarketSale>? sales, MarketData d)
        {
            var dl = Ui.Dl;
            int total = listings?.Count ?? sales!.Count;
            Ui.Caps(dl, p, title);
            float capsW = Ui.TextSize(title.ToUpperInvariant(), 0.78f).X + title.Length * S(1.4f);
            Ui.Text(dl, p + new Vector2(capsW + S(8), 0), total > 50 ? $"50 of {total}" : $"{total}", Theme.Text3, 0.78f);
            float y = p.Y + Ui.TextSize("A", 0.78f).Y + S(6);
            if (total == 0)
            {
                Empty(new Vector2(p.X, y), w, listings != null ? "No listings." : "No recent sales.");
                return y + EmptyH;
            }
            float rh = LH + S(8);
            int n = Math.Min(50, total);
            float h = rh * (n + 1) + S(8);
            Ui.Panel(dl, new Vector2(p.X, y), new Vector2(p.X + w, y + h), Theme.Night1.With(0.7f));
            string[] heads = listings != null ? new[] { "Price", "Qty", "Total", "World", "Retainer" } : new[] { "Price", "Qty", "World", "Buyer", "When" };
            float[] frac = listings != null ? new[] { 0.2f, 0.11f, 0.22f, 0.2f, 0.27f } : new[] { 0.2f, 0.11f, 0.2f, 0.3f, 0.19f };
            bool showWorld = d.worldUploadTimes.Count > 1 || (listings?.Any(l => l.worldName.Length > 0) ?? sales!.Any(s => s.worldName.Length > 0));
            var colX = new float[5];
            float cx = p.X + S(10), inner = w - S(20);
            float worldF = showWorld ? 1f : 0f;
            float sum = frac.Select((f, i) => heads[i] == "World" ? f * worldF : f).Sum();
            for (int i = 0; i < 5; i++)
            {
                colX[i] = cx;
                float f = heads[i] == "World" ? frac[i] * worldF : frac[i];
                cx += inner * f / sum;
            }
            float ColW(int i) => (i < 4 ? colX[i + 1] : p.X + w - S(10)) - colX[i] - S(6);
            float ry = y + S(4);
            for (int i = 0; i < 5; i++)
                if (heads[i] != "World" || showWorld) Ui.Text(dl, new Vector2(colX[i], ry + S(4)), heads[i], Theme.Text3, 0.82f);
            ry += rh;
            dl.AddLine(new Vector2(p.X + S(8), ry - S(1)), new Vector2(p.X + w - S(8), ry - S(1)), Theme.CrystalLine.U32(), S(1));
            for (int r = 0; r < n; r++)
            {
                if (r % 2 == 1) dl.AddRectFilled(new Vector2(p.X + S(4), ry), new Vector2(p.X + w - S(4), ry + rh), Theme.Night2.With(0.35f).U32());
                string[] cells;
                Vector4 lastCol = Theme.Text2;
                if (listings != null)
                {
                    var l = listings[r];
                    cells = new[] { Gil(l.pricePerUnit), $"x{l.quantity}", Gil((long)l.pricePerUnit * l.quantity), l.worldName.Length > 0 ? l.worldName : d.worldName ?? "", l.retainerName };
                    if (_activeRetainers.Contains(l.retainerName)) lastCol = Ui.Accent;
                }
                else
                {
                    var s = sales![r];
                    cells = new[] { Gil(s.pricePerUnit), $"x{s.quantity}", s.worldName.Length > 0 ? s.worldName : d.worldName ?? "", s.buyerName, AgoMs(s.timestamp * 1000) };
                }
                for (int i = 0; i < 5; i++)
                {
                    if (heads[i] == "World" && !showWorld) continue;
                    var col = i == 0 ? Theme.Gold : i == 4 ? lastCol : i == 2 && listings != null ? Theme.Text2 : Theme.Text;
                    Ui.Text(dl, new Vector2(colX[i], ry + S(4)), Fit(cells[i], ColW(i)), col);
                }
                ry += rh;
            }
            return y + h;
        }

        private float DrawHistory(Vector2 p, float w, MarketData d, bool canHq)
        {
            var dl = Ui.Dl;
            Ui.Caps(dl, p, "Price history");
            float y = p.Y + Ui.TextSize("A", 0.78f).Y + S(8);
            var sets = new List<(string label, bool hq, Vector4 col)>();
            if (canHq) sets.Add(("HQ", true, Theme.Gold));
            sets.Add(("NQ", false, Ui.Accent));
            bool two = sets.Count == 2 && w >= S(700);
            float gap = S(12), gw = two ? (w - gap) / 2 : w, gh = S(110);
            for (int i = 0; i < sets.Count; i++)
            {
                var (label, hq, col) = sets[i];
                var a = two ? new Vector2(p.X + i * (gw + gap), y) : new Vector2(p.X, y + i * (gh + gap));
                Ui.Panel(dl, a, a + new Vector2(gw, gh), Theme.Night1.With(0.7f));
                var values = d.recentHistory.Where(s => s.hq == hq).OrderBy(s => s.timestamp).Select(s => (float)s.pricePerUnit).ToArray();
                if (values.Length >= 2)
                {
                    var g0 = a + S(8, 26);
                    var g1 = a + new Vector2(gw, gh) - S(8, 8);
                    Sparkline.Draw(g0, g1, values, col, col.With(0.16f));
                    string range = $"{Gil((long)values.Min())} - {Gil((long)values.Max())} gil";
                    Ui.Text(dl, a + new Vector2(gw - S(10) - Ui.TextSize(range, 0.82f).X, S(8)), range, Theme.Text3, 0.82f);
                }
                else
                {
                    var t = "Not enough sales";
                    var ts = Ui.TextSize(t, 0.85f);
                    Ui.Text(dl, a + (new Vector2(gw, gh) - ts) / 2, t, Theme.Text3, 0.85f);
                }
                Ui.Text(dl, a + S(10, 8), $"{label} sales", col, 0.85f);
            }
            return two ? y + gh : y + sets.Count * gh + (sets.Count - 1) * gap;
        }
    }
}
