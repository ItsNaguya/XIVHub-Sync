using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Cart tab: cart, saved lists, route options, the travel route (same actions as the website's Cart)
    public partial class MarketApp
    {
        private bool? _cartRouteViewSet;
        private bool _cartRouteView
        {
            get => _cartRouteViewSet ?? _route.Count > 0;
            set => _cartRouteViewSet = value;
        }
        private bool _showConfig, _confirmClear, _showSave, _showLoad;
        private string _saveName = "", _deleteListId = "";

        private void DrawCart()
        {
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("mk_cart", avail))
            {
                float w = avail.X - S(10);
                var p = ImGui.GetCursorScreenPos();
                float y = p.Y;

                if (_cart.Count == 0 && _route.Count == 0)
                {
                    Empty(p, w, "Your cart is empty. Add items from Search, Favourites or Crafting.", FontAwesomeIcon.ShoppingCart);
                    y += EmptyH + S(10);
                    if (_lists.Count > 0)
                    {
                        if (Ui.Button("mk_load0", "Load a saved list", new Vector2(p.X, y), new Vector2(S(190), S(30)), Kind.Ghost, FontAwesomeIcon.FolderOpen)) _showLoad = !_showLoad;
                        y += S(40);
                    }
                    if (_showLoad) y = DrawLoadPanel(new Vector2(p.X, y), w) + S(10);
                    Advance(p, y - p.Y);
                    ImGui.EndChild();
                    return;
                }

                y = DrawCartToolbar(new Vector2(p.X, y), w) + S(10);
                if (_showSave) y = DrawSavePanel(new Vector2(p.X, y), w) + S(10);
                if (_showLoad) y = DrawLoadPanel(new Vector2(p.X, y), w) + S(10);
                y = DrawRouteConfig(new Vector2(p.X, y), w) + S(10);
                if (_routeError != null) { Message(new Vector2(p.X, y), w, _routeError, Theme.Bad); y += LH + S(30); }

                if (_calculating)
                {
                    Ui.Icon(Ui.Dl, new Vector2(p.X, y), FontAwesomeIcon.Sync, Ui.Accent);
                    Ui.Text(Ui.Dl, new Vector2(p.X + S(26), y), "Planning the cheapest way round...", Theme.Text2);
                    y += LH + S(12);
                }
                else if (_cartRouteView) y = DrawRoute(new Vector2(p.X, y), w);
                else
                {
                    foreach (var item in _cart.ToList()) y = CartRow(new Vector2(p.X, y), w, item) + S(6);
                }
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private float DrawCartToolbar(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float bh = S(30);
            string info = _cartRouteView && _route.Count > 0
                ? $"Route  ·  {Plural(_route.Count, "world")}  ·  {Plural(_route.Sum(d => d.stops.Count(s => !s.checkedState)), "stop")} left"
                : $"{Plural(_cart.Count, "item")}  ·  {Plural(_cart.Sum(c => c.quantity), "piece")}  ·  prices from {ScopeShort(Scope)}";
            Ui.Text(dl, p + new Vector2(0, (bh - LH) / 2), Fit(info, w), Theme.Text2);
            var at = new Vector2(p.X, p.Y + bh + S(8));
            float left = p.X, right = p.X + w;

            if (FlowButton("mk_plan", _cartRouteView ? "Recalculate" : "Plan route", FontAwesomeIcon.Route, Kind.Gold, ref at, left, right, bh) && _cart.Count > 0) PlanRoute();
            Ui.Tooltip("Plan where to buy everything in the cart");
            if (_cartRouteView) { if (FlowButton("mk_vcart", "Cart", FontAwesomeIcon.ShoppingCart, Kind.Ghost, ref at, left, right, bh)) _cartRouteView = false; }
            else if (_route.Count > 0 && FlowButton("mk_vroute", "Route", FontAwesomeIcon.MapMarkedAlt, Kind.Ghost, ref at, left, right, bh)) _cartRouteView = true;
            if (_cart.Count > 0 && FlowButton("mk_save", "Save list", FontAwesomeIcon.Save, _showSave ? Kind.Primary : Kind.Ghost, ref at, left, right, bh)) { _showSave = !_showSave; _showLoad = false; }
            if (_lists.Count > 0 && FlowButton("mk_load", "Load list", FontAwesomeIcon.FolderOpen, _showLoad ? Kind.Primary : Kind.Ghost, ref at, left, right, bh)) { _showLoad = !_showLoad; _showSave = false; }
            if (_soldOut.Count > 0)
            {
                if (FlowButton("mk_resetso", "Reset sold out", FontAwesomeIcon.Ban, Kind.Ghost, ref at, left, right, bh)) { _soldOut = new(); WriteSoldOut(); }
                Ui.Tooltip($"{Plural(_soldOut.Count, "listing")} marked sold out");
            }
            if (_confirmClear)
            {
                if (FlowButton("mk_clear_y", "Clear cart: yes", FontAwesomeIcon.Trash, Kind.Danger, ref at, left, right, bh)) { _cart.Clear(); WriteCart(); _confirmClear = false; }
                if (FlowButton("mk_clear_n", "No", null, Kind.Ghost, ref at, left, right, bh)) _confirmClear = false;
            }
            else if ((_cart.Count > 0 || _route.Count > 0) && FlowButton("mk_clear", _cart.Count > 0 ? "Clear" : "Clear route", FontAwesomeIcon.Trash, Kind.Danger, ref at, left, right, bh))
            {
                if (_cart.Count > 0) _confirmClear = true;
                else { _route = new(); _unfulfilled = new(); WriteRoute(); }
            }
            return at.Y + bh;
        }

        private float DrawSavePanel(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float pad = S(12), h = pad + Ui.TextSize("A", 0.78f).Y + S(8) + FrameH + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.75f));
            Ui.Caps(dl, p + new Vector2(pad, pad), "Save shopping list");
            float y = p.Y + pad + Ui.TextSize("A", 0.78f).Y + S(8);
            float bw = S(90);
            bool enter = Input("##mk_savename", ref _saveName, "e.g. Crafter gear 100", w - pad * 2 - bw * 2 - S(16), new Vector2(p.X + pad, y), 60, ImGuiInputTextFlags.EnterReturnsTrue);
            if ((Ui.Button("mk_save_ok", "Save", new Vector2(p.X + w - pad - bw * 2 - S(8), y), new Vector2(bw, FrameH), Kind.Gold, FontAwesomeIcon.Save) || enter) && _saveName.Trim().Length > 0)
            {
                _lists.Add(new ShoppingList { id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), name = _saveName.Trim(), items = Clone(_cart) });
                WriteLists();
                _saveName = "";
                _showSave = false;
            }
            if (Ui.Button("mk_save_no", "Cancel", new Vector2(p.X + w - pad - bw, y), new Vector2(bw, FrameH))) _showSave = false;
            return p.Y + h;
        }

        private float DrawLoadPanel(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float pad = S(12), rh = S(40), capsH = Ui.TextSize("A", 0.78f).Y;
            float h = pad + capsH + S(8) + Math.Max(1, _lists.Count) * rh + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.75f));
            Ui.Caps(dl, p + new Vector2(pad, pad), "Saved shopping lists");
            if (Ui.IconButton("mk_load_x", FontAwesomeIcon.Times, new Vector2(p.X + w - pad - S(26), p.Y + pad - S(4)), S(26), "Close")) _showLoad = false;
            float y = p.Y + pad + capsH + S(8);
            if (_lists.Count == 0) Ui.Text(dl, new Vector2(p.X + pad, y + (rh - LH) / 2), "No saved lists.", Theme.Text3);
            foreach (var list in _lists.ToList())
            {
                float bw = S(70), bh = S(28), by = y + (rh - bh) / 2;
                float rx = p.X + w - pad;
                if (_deleteListId == list.id)
                {
                    if (Ui.Button("mk_ld_n" + list.id, "No", new Vector2(rx - bw, by), new Vector2(bw, bh))) _deleteListId = "";
                    if (Ui.Button("mk_ld_y" + list.id, "Delete", new Vector2(rx - bw * 2 - S(6), by), new Vector2(bw, bh), Kind.Danger))
                    {
                        _lists.RemoveAll(l => l.id == list.id);
                        WriteLists();
                        _deleteListId = "";
                    }
                    rx -= bw * 2 + S(12);
                }
                else
                {
                    if (Ui.Button("mk_ll" + list.id, "Load", new Vector2(rx - bw, by), new Vector2(bw, bh), Kind.Primary))
                    {
                        _cart = Clone(list.items);
                        WriteCart();
                        _cartRouteView = false;
                        _showLoad = false;
                    }
                    Ui.Tooltip("Replaces the current cart");
                    if (Ui.IconButton("mk_ldel" + list.id, FontAwesomeIcon.Trash, new Vector2(rx - bw - S(6) - bh, by), bh, "Delete list", Theme.Bad)) _deleteListId = list.id;
                    rx -= bw + bh + S(12);
                }
                string label = $"{list.name}  ·  {Plural(list.items?.Count ?? 0, "item")}";
                Ui.Text(dl, new Vector2(p.X + pad, y + (rh - LH) / 2), Fit(label, rx - p.X - pad * 2), Theme.Text);
                y += rh;
            }
            return p.Y + h;
        }

        private static List<CartItem> Clone(List<CartItem>? items)
            => (items ?? new()).Select(i => new CartItem { id = i.id, name = i.name, icon = i.icon, quantity = i.quantity, hq = i.hq, canBeHq = i.canBeHq, level = i.level, ilvl = i.ilvl }).ToList();

        private float DrawRouteConfig(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            string Pr(string v) => v == "fastest" ? "Fastest" : v == "cheapest" ? "Cheapest" : "Balanced";
            string St(string v) => v == "bulk" ? "Smart bulk" : "Strict";
            string Qu(string v) => v == "force_hq" ? "HQ everywhere" : v == "ignore_hq" ? "Any quality" : "As in cart";
            string travel = !_cfg.serverTravel ? "Home world only" : _cfg.dcTravel ? "Other data centers" : "Other worlds";
            float hh = S(36);
            bool click = Ui.Hit("mk_cfg_head", p, new Vector2(w, hh), out float hv);
            Ui.Panel(dl, p, p + new Vector2(w, hh), Theme.Night2.With(0.6f + 0.2f * hv), Theme.CrystalLine, 6f);
            Ui.Icon(dl, p + new Vector2(S(12), (hh - LH) / 2), _showConfig ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, Theme.Text2, 0.85f);
            Ui.Text(dl, p + new Vector2(S(34), (hh - LH) / 2), Fit($"Route options  ·  {Pr(_cfg.priority)} · {St(_cfg.strategy)} · {Qu(_cfg.quality)} · {travel}", w - S(46)), Theme.Text2);
            if (click) _showConfig = !_showConfig;
            if (!_showConfig) return p.Y + hh;

            float pad = S(12), capsH = Ui.TextSize("A", 0.78f).Y, ch = ChipH();
            float y = p.Y + hh + S(8);
            float groupW = w >= S(760) ? (w - pad * 2) / 2 : w - pad * 2;
            // measure: 4 groups, two per row on wide screens
            int perRow = w >= S(760) ? 2 : 1;
            int rows = (4 + perRow - 1) / perRow;
            float gh = capsH + S(6) + ch + S(12);
            float h = pad + rows * gh + pad;
            Ui.Panel(dl, new Vector2(p.X, y), new Vector2(p.X + w, y + h), Theme.Night2.With(0.5f));
            bool changed = false;
            for (int g = 0; g < 4; g++)
            {
                var a = new Vector2(p.X + pad + (g % perRow) * groupW, y + pad + (g / perRow) * gh);
                string[] titles = { "Priority", "Buying", "Quality", "Travel" };
                Ui.Caps(dl, a, titles[g]);
                var at = a + new Vector2(0, capsH + S(6));
                float l = a.X, r = a.X + groupW - S(10);
                switch (g)
                {
                    case 0:
                        foreach (var (id, label, tip) in new[] { ("fastest", "Fastest", "Fewest world visits"), ("balanced", "Balanced", "Time against cost"), ("cheapest", "Cheapest", "Lowest price") })
                        {
                            if (ChipToggle("mk_pr" + id, label, _cfg.priority == id, ref at, l, r)) { _cfg.priority = id; changed = true; }
                            Ui.Tooltip(tip);
                        }
                        break;
                    case 1:
                        if (ChipToggle("mk_st_s", "Strict", _cfg.strategy == "strict", ref at, l, r)) { _cfg.strategy = "strict"; changed = true; }
                        Ui.Tooltip("Never buy more than the cart needs");
                        if (ChipToggle("mk_st_b", "Smart bulk", _cfg.strategy == "bulk", ref at, l, r)) { _cfg.strategy = "bulk"; changed = true; }
                        Ui.Tooltip("Buy a bigger stack when reselling the rest pays off");
                        break;
                    case 2:
                        foreach (var (id, label, tip) in new[] { ("keep", "As in cart", "Use the HQ tick of each cart item"), ("force_hq", "HQ everywhere", "High quality for every item"), ("ignore_hq", "Any quality", "Ignore the HQ ticks") })
                        {
                            if (ChipToggle("mk_qu" + id, label, _cfg.quality == id, ref at, l, r)) { _cfg.quality = id; changed = true; }
                            Ui.Tooltip(tip);
                        }
                        break;
                    case 3:
                        bool st = _cfg.serverTravel, dc = _cfg.serverTravel && _cfg.dcTravel;
                        var cp = at + new Vector2(0, (ch - Math.Max(S(18), LH)) / 2);
                        if (Check("mk_tr_s", "Other worlds", ref st, cp)) { _cfg.serverTravel = st; if (!st) _cfg.dcTravel = false; changed = true; }
                        if (Check("mk_tr_d", "Other data centers", ref dc, cp + new Vector2(CheckW("Other worlds") + S(16), 0), _cfg.serverTravel)) { _cfg.dcTravel = dc; changed = true; }
                        break;
                }
            }
            if (changed) WriteConfig();
            return y + h;
        }

        private float CartRow(Vector2 p, float w, CartItem item)
        {
            var dl = Ui.Dl;
            bool canHq = item.canBeHq != false;
            string sub = LevelText(item.level, item.ilvl);
            float h = Math.Max(S(48), (sub.Length > 0 ? LH * 2 + S(4) : LH) + S(16));
            float trash = S(30);
            float right = trash + S(8) + StepperW + S(10) + (canHq ? CheckW("HQ") + S(12) : 0);
            bool open = Ui.Hit("mk_crow" + item.id, p, new Vector2(Math.Max(S(60), w - right - S(8)), h), out float hv);
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f + 0.25f * hv), (hv > 0.01f ? Ui.Accent : Theme.Crystal).With(0.15f + 0.3f * hv), 8f);
            float ic = S(36);
            GameIcon(item.icon, p + new Vector2(S(10), (h - ic) / 2), ic);
            float tx = p.X + S(56), tw = w - right - S(70);
            float ny = sub.Length > 0 ? p.Y + (h - LH * 2 - S(4)) / 2 : p.Y + (h - LH) / 2;
            var name = Fit(item.name, tw - (item.hq == true ? S(36) : 0));
            Ui.Text(dl, new Vector2(tx, ny), name, Theme.Text);
            if (item.hq == true) Ui.Chip(dl, new Vector2(tx + Ui.TextSize(name).X + S(6), ny - S(1)), "HQ", Theme.Gold, out _);
            if (sub.Length > 0) Ui.Text(dl, new Vector2(tx, ny + LH + S(4)), Fit(sub, tw, 0.85f), Theme.Text3, 0.85f);
            if (open) SelectMarketItem(new MarketSearchItem { id = item.id, name = item.name, icon = item.icon, level = item.level, ilvl = item.ilvl, canBeHq = item.canBeHq });

            float x = p.X + w - S(8) - trash;
            if (Ui.IconButton("mk_crm" + item.id, FontAwesomeIcon.Trash, new Vector2(x, p.Y + (h - trash) / 2), trash, "Remove from cart", Theme.Bad))
            {
                _cart.RemoveAll(c => c.id == item.id);
                WriteCart();
            }
            x -= S(10) + StepperW;
            int q = item.quantity;
            if (Stepper("mk_cq" + item.id, ref q, new Vector2(x, p.Y + (h - S(26)) / 2))) { item.quantity = q; WriteCart(); }
            if (canHq)
            {
                x -= S(12) + CheckW("HQ");
                bool hq = item.hq == true;
                if (Check("mk_chq" + item.id, "HQ", ref hq, new Vector2(x, p.Y + (h - Math.Max(S(18), LH)) / 2))) { item.hq = hq; WriteCart(); }
                Ui.Tooltip("High quality only");
            }
            return p.Y + h;
        }

        // =============================================================================================================
        private float DrawRoute(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float y = p.Y;
            var stops = _route.SelectMany(d => d.stops).ToList();
            long grand = _route.Sum(d => (long)d.totalCost);
            long resale = stops.Sum(s => (long)(s.resaleValue ?? 0));
            int left = stops.Count(s => !s.checkedState);

            // totals
            float capsH = Ui.TextSize("A", 0.78f).Y, valH = Ui.TextSize("0", 1.4f).Y, subH = Ui.TextSize("A", 0.82f).Y;
            float ch = S(12) + capsH + S(6) + valH + S(4) + subH + S(12);
            int cards = resale > 0 ? 2 : 1;
            float cw = cards == 2 && w >= S(560) ? (w - S(10)) / 2 : w;
            void Stat(Vector2 a, string label, long value, string sub)
            {
                Ui.Panel(dl, a, a + new Vector2(cw, ch), Theme.Night2.With(0.6f));
                Ui.Caps(dl, a + S(12, 12), label, Theme.Text2);
                var vp = a + new Vector2(S(12), S(12) + capsH + S(6));
                var v = Gil(value);
                Ui.Text(dl, vp, v, Theme.Gold, 1.4f);
                Ui.Text(dl, vp + new Vector2(Ui.TextSize(v, 1.4f).X + S(5), valH - subH - S(2)), "gil", Theme.Text3, 0.82f);
                Ui.Text(dl, vp + new Vector2(0, valH + S(4)), Fit(sub, cw - S(24), 0.82f), Theme.Text3, 0.82f);
            }
            Stat(new Vector2(p.X, y), "Estimated total", grand, $"{Plural(_route.Count, "world")} · {Plural(left, "stop")} left");
            if (resale > 0)
            {
                bool side = cw < w;
                Stat(side ? new Vector2(p.X + cw + S(10), y) : new Vector2(p.X, y + ch + S(10)), "Resale value of extras", resale, "gross, from smart bulk");
                if (!side) y += ch + S(10);
            }
            y += ch + S(12);

            if (_route.Count == 0 && _unfulfilled.Count == 0) { Empty(new Vector2(p.X, y), w, "The route is empty. Press Recalculate."); y += EmptyH + S(10); }

            foreach (var dest in _route.ToList())
            {
                // destination head: world [dc] ---- n items  total
                float hh = LH + S(10);
                Ui.Dot(dl, new Vector2(p.X + S(6), y + hh / 2), 4f, Ui.Accent);
                Ui.Icon(dl, new Vector2(p.X + S(18), y + (hh - LH) / 2), FontAwesomeIcon.Globe, Ui.Accent, 0.9f);
                string world = dest.world, dcs = $"[{dest.dc}]";
                float wx = p.X + S(40);
                Ui.Text(dl, new Vector2(wx, y + (hh - LH) / 2), world, Theme.Text);
                wx += Ui.TextSize(world).X + S(6);
                Ui.Text(dl, new Vector2(wx, y + (hh - LH) / 2), dcs, Theme.Text3);
                wx += Ui.TextSize(dcs).X + S(10);
                string tot = $"{Gil(dest.totalCost)} gil";
                string cnt = Plural(dest.stops.Sum(s => s.quantity), "item");
                float tx = p.X + w - Ui.TextSize(tot).X;
                Ui.Text(dl, new Vector2(tx, y + (hh - LH) / 2), tot, Theme.Gold);
                float cx = tx - S(12) - Ui.TextSize(cnt).X;
                Ui.Text(dl, new Vector2(cx, y + (hh - LH) / 2), cnt, Theme.Text3);
                if (cx - S(10) > wx) dl.AddLine(new Vector2(wx, y + hh / 2), new Vector2(cx - S(10), y + hh / 2), Theme.CrystalLine.U32(), S(1));
                y += hh + S(6);

                foreach (var stop in dest.stops.ToList()) y = StopRow(new Vector2(p.X + S(14), y), w - S(14), stop) + S(6);
                y += S(8);
            }

            if (_unfulfilled.Count > 0)
            {
                float pad = S(12), rh = S(40);
                float h = pad + capsH + S(8) + _unfulfilled.Count * rh + pad;
                Ui.Panel(dl, new Vector2(p.X, y), new Vector2(p.X + w, y + h), Theme.Bad.With(0.06f), Theme.Bad.With(0.4f));
                Ui.Caps(dl, new Vector2(p.X + pad, y + pad), "Could not be bought", Theme.Bad);
                float ry = y + pad + capsH + S(8);
                foreach (var (item, reason) in _unfulfilled)
                {
                    GameIcon(item.icon, new Vector2(p.X + pad, ry + (rh - S(30)) / 2), S(30));
                    string n = $"{item.name}{(item.hq == true ? " (HQ)" : "")} x {item.quantity}";
                    float half = (w - pad * 2 - S(40)) / 2;
                    Ui.Text(dl, new Vector2(p.X + pad + S(40), ry + (rh - LH) / 2), Fit(n, half), Theme.Text);
                    Ui.Text(dl, new Vector2(p.X + pad + S(40) + half + S(10), ry + (rh - LH) / 2), Fit(reason, half - S(10)), Theme.Bad);
                    ry += rh;
                }
                y += h + S(10);
            }
            return y;
        }

        private float StopRow(Vector2 p, float w, RouteStop stop)
        {
            var dl = Ui.Dl;
            bool done = stop.checkedState;
            bool extra = (stop.excessQty ?? 0) > 0;
            float sh = Ui.TextSize("A", 0.85f).Y;
            float h = Math.Max(S(52), LH + S(4) + sh + (extra ? sh + S(2) : 0) + S(16));
            float btn = S(30);
            float rightBtns = (done ? 1 : 2) * (btn + S(6));
            string buy = $"Buy {stop.quantity} @ {Gil(stop.pricePerUnit)}";
            string total = $"{Gil(stop.total)} gil";
            float priceW = Math.Max(Ui.TextSize(buy, 0.88f).X, Ui.TextSize(total).X);
            bool showPrice = w >= S(520);
            float right = rightBtns + (showPrice ? priceW + S(16) : 0);
            float a = done ? 0.55f : 1f;

            Ui.Panel(dl, p, p + new Vector2(w, h), (done ? Theme.Live.With(0.05f) : Theme.Night2.With(0.6f)), (done ? Theme.Live.With(0.35f) : Theme.CrystalLine), 8f);

            // bought tick
            float ck = S(30);
            var cp = p + new Vector2(S(8), (h - ck) / 2);
            bool tick = Ui.Hit("mk_tick" + stop.id + stop.itemId, cp, new Vector2(ck, ck), out float th);
            Ui.Tooltip(done ? "Mark as not bought" : "Mark as bought");
            var cc = cp + new Vector2(ck, ck) / 2;
            if (done) Ui.IconCentered(dl, cc, FontAwesomeIcon.CheckCircle.ToIconString(), Theme.Live, 1.15f);
            else dl.AddCircle(cc, S(9), UIHelper.LerpColor(Theme.Text3, Ui.Accent, th).U32(), 24, S(1.6f));
            if (tick)
            {
                stop.checkedState = !done;
                WriteRoute();
                _sender.SendActionAsync(new { action = "TOGGLE_STOP", itemId = stop.itemId, @checked = stop.checkedState });
            }

            // icon + text (click opens the item)
            float ix = p.X + S(46), ic = S(34);
            bool open = Ui.Hit("mk_sopen" + stop.id + stop.itemId, new Vector2(ix, p.Y), new Vector2(Math.Max(S(40), w - (ix - p.X) - right - S(8)), h), out float oh);
            GameIcon(stop.itemIcon, new Vector2(ix, p.Y + (h - ic) / 2), ic, a);
            float tx = ix + ic + S(10), tw = w - (tx - p.X) - right - S(10);
            float ty = p.Y + (h - (LH + S(4) + sh + (extra ? sh + S(2) : 0))) / 2;
            var name = Fit(stop.itemName, tw - (stop.hq == true ? S(36) : 0));
            Ui.Text(dl, new Vector2(tx, ty), name, UIHelper.LerpColor(Theme.Text, Ui.Accent, oh * 0.5f).With(a));
            if (stop.hq == true) Ui.Chip(dl, new Vector2(tx + Ui.TextSize(name).X + S(6), ty - S(1)), "HQ", Theme.Gold, out _);
            string sub = showPrice ? $"Retainer {stop.retainer}" : $"Retainer {stop.retainer}  ·  {buy}  ·  {total}";
            Ui.Text(dl, new Vector2(tx, ty + LH + S(4)), Fit(sub, tw, 0.85f), Theme.Text2.With(a), 0.85f);
            if (extra) Ui.Text(dl, new Vector2(tx, ty + LH + S(4) + sh + S(2)), Fit($"Resell {stop.excessQty} for ~{Gil(stop.resaleValue ?? 0)} gil", tw, 0.85f), Theme.Gold.With(a), 0.85f);
            if (open) SelectMarketItem(new MarketSearchItem { id = stop.itemId, name = stop.itemName, icon = stop.itemIcon, canBeHq = stop.hq == true ? true : null });

            float x = p.X + w - S(8) - btn;
            if (Ui.IconButton("mk_srm" + stop.id + stop.itemId, FontAwesomeIcon.Trash, new Vector2(x, p.Y + (h - btn) / 2), btn, "Remove from cart (recalculate after)", Theme.Bad))
            {
                _cart.RemoveAll(c => c.id == stop.itemId);
                WriteCart();
            }
            if (!done)
            {
                x -= btn + S(6);
                if (Ui.IconButton("mk_sso" + stop.id + stop.itemId, FontAwesomeIcon.Ban, new Vector2(x, p.Y + (h - btn) / 2), btn, "Sold out: find the next best listing", Theme.Warn))
                {
                    _soldOut = _soldOut.Append(new SoldOut { itemId = stop.itemId, retainer = stop.retainer, world = stop.world }).ToList();
                    WriteSoldOut();
                    PlanRoute(_soldOut);
                }
            }
            if (showPrice)
            {
                float px = x - S(10);
                var bs = Ui.TextSize(buy, 0.88f);
                var ts = Ui.TextSize(total);
                float py = p.Y + (h - bs.Y - ts.Y - S(2)) / 2;
                Ui.Text(dl, new Vector2(px - bs.X, py), buy, Theme.Text2.With(a), 0.88f);
                Ui.Text(dl, new Vector2(px - ts.X, py + bs.Y + S(2)), total, Theme.Gold.With(a));
            }
            return p.Y + h;
        }
    }
}
