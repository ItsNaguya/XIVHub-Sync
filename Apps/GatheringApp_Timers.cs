using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Node timers (website NodeTimers): filters in gathering.timers, groups by spawn time or A-Z, favourites and alarms
    public partial class GatheringApp
    {
        private static readonly string[] NodeTypes = { "Legendary", "Unspoiled", "Ephemeral" };
        private static readonly (string, string)[] Expansions =
        {
            ("All", "All expansions"), ("Dawntrail", "Dawntrail (100)"), ("Endwalker", "Endwalker (90)"), ("Shadowbringers", "Shadowbringers (80)"),
            ("Stormblood", "Stormblood (70)"), ("Heavensward", "Heavensward (60)"), ("ARR", "A Realm Reborn (50)"),
        };
        private static readonly Dictionary<string, string> TypeTip = new()
        {
            ["Legendary"] = "Legendary node: needs the folklore tome shown on the row.",
            ["Unspoiled"] = "Unspoiled node: timed, no tome needed.",
            ["Ephemeral"] = "Ephemeral node: timed, gives aethersand when collected.",
        };
        private string _nodeQuery = "";

        private void DrawTimers()
        {
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("ga_timers", avail))
            {
                float w = avail.X - S(10);
                var p = ImGui.GetCursorScreenPos();
                float y = DrawTimerFilters(p, w) + S(12);
                if (Nodes.Count == 0)
                {
                    Empty(new Vector2(p.X, y), w, _nodesLoading || !_sender.HasToken ? (_sender.HasToken ? "Loading the node list..." : "Sign in (Settings > Account) to load the node list.") : "The node list could not be loaded. Retrying...", FontAwesomeIcon.Sync);
                    y += EmptyH;
                }
                else y = DrawNodeGroups(new Vector2(p.X, y), w);
                y += S(8);
                Ui.Text(Ui.Dl, new Vector2(p.X, y), Fit($"Node data: game data via Teamcraft, {Nodes.Count:N0} timed node items. Timers use Eorzea time.", w, 0.8f), Theme.Text3, 0.8f);
                y += LH + S(6);
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private List<GatheringNode> FilteredNodes()
        {
            var f = _timerF;
            var q = _nodeQuery.Trim().ToLowerInvariant();
            return Nodes.Where(n =>
            {
                if (f.favOnly) return IsFav(n.id);
                if (IsFav(n.id)) return true; // favourites always show
                if (!f.classes.Contains(n.type)) return false;
                if (!f.types.Contains(n.nodeType)) return false;
                if (f.expansion != "All" && n.expansion != f.expansion) return false;
                if (q.Length > 0 && !n.name.ToLowerInvariant().Contains(q) && !n.zone.ToLowerInvariant().Contains(q) && !(n.area ?? "").ToLowerInvariant().Contains(q)) return false;
                return true;
            }).ToList();
        }

        private float DrawTimerFilters(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float pad = S(12), inner = w - pad * 2, ch = ChipH();
            int favCount = Nodes.Count(n => IsFav(n.id)), alarmCount = Nodes.Count(n => HasAlarm(n.id));
            var f = _timerF;

            var chips = new List<(string id, string label, bool on, string tip, Action act)>
            {
                ("ga_c_min", "Miner", f.classes.Contains("MIN"), "", () => Toggle(f.classes, "MIN")),
                ("ga_c_btn", "Botanist", f.classes.Contains("BTN"), "", () => Toggle(f.classes, "BTN")),
            };
            foreach (var t in NodeTypes) chips.Add(("ga_t_" + t, t, f.types.Contains(t), TypeTip[t], () => Toggle(f.types, t)));
            chips.Add(("ga_fav", favCount > 0 ? $"★ Favourites ({favCount})" : "★ Favourites", f.favOnly, "Only your favourites. Favourites also show when other filters would hide them.", () => f.favOnly = !f.favOnly));
            chips.Add(("ga_sort", f.sort == "time" ? "A-Z" : "By time", false, f.sort == "time" ? "Show every node A-Z (directory)" : "Group by spawn time", () => f.sort = f.sort == "time" ? "az" : "time"));

            // measure the chip rows to size the panel
            float rowsH = ch, x = 0;
            foreach (var c in chips)
            {
                float cw = Ui.TextSize(c.label, 0.85f).X + S(16);
                if (x + cw > inner && x > 0) { rowsH += ch + S(6); x = 0; }
                x += cw + S(6);
            }
            float h = pad + FrameH + S(10) + rowsH + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f));

            float expW = Math.Min(S(210), inner * 0.35f), soundW = S(28);
            float searchW = Math.Max(S(120), inner - expW - soundW - S(16));
            Input("##ga_nq", ref _nodeQuery, "Item, zone or area...", searchW, p + new Vector2(pad, pad));
            if (Combo("##ga_exp", f.expansion, Expansions, expW, p + new Vector2(pad + searchW + S(8), pad), "Expansion") is { } e) { f.expansion = e; WriteTimers(); }
            if (Ui.IconButton("ga_testsound", FontAwesomeIcon.VolumeUp, p + new Vector2(w - pad - soundW, pad + (FrameH - soundW) / 2), soundW,
                $"Test the alarm. {Plural(alarmCount, "alarm")} set; alarms ring in game while the plugin runs."))
                Ring("alarm:test", "Test alarm", "This is how an alarm looks and sounds.", "Test alarm: this is how an alarm looks.", true);

            var at = p + new Vector2(pad, pad + FrameH + S(10));
            bool changed = false;
            foreach (var c in chips)
                if (ChipToggle(c.id, c.label, c.on, ref at, p.X + pad, p.X + w - pad, 0.85f, c.tip)) { c.act(); changed = true; }
            if (changed) WriteTimers();
            return p.Y + h;
        }

        private static void Toggle(List<string> list, string v) { if (!list.Remove(v)) list.Add(v); }

        private float DrawNodeGroups(Vector2 p, float w)
        {
            double et = EorzeaTimeHelper.EtMs();
            var filtered = FilteredNodes();
            var rows = filtered.Select(n => (n, st: EorzeaTimeHelper.Spawn(n.hours, EorzeaTimeHelper.Duration(n), et)))
                .Where(r => r.st.HasValue).Select(r => (r.n, st: r.st!.Value)).ToList();
            var groups = new List<(string key, string title, List<(GatheringNode n, (bool Up, double Secs) st)> rows)>();
            if (_timerF.sort == "az") groups.Add(("all", "All nodes A-Z", rows.OrderBy(r => r.n.name).ToList()));
            else
            {
                var up = rows.Where(r => r.st.Up).OrderByDescending(r => IsFav(r.n.id)).ThenBy(r => r.st.Secs).ToList();
                var next = rows.Where(r => !r.st.Up).OrderBy(r => r.st.Secs).ToList();
                groups.Add(("up", "Up now", up));
                groups.Add(("5", "Spawning within 5 min", next.Where(r => r.st.Secs <= 300).ToList()));
                groups.Add(("15", "In 5-15 min", next.Where(r => r.st.Secs > 300 && r.st.Secs <= 900).ToList()));
                groups.Add(("later", "Later", next.Where(r => r.st.Secs > 900).ToList()));
            }

            float y = p.Y;
            Ui.Text(Ui.Dl, new Vector2(p.X + w - Ui.TextSize($"{filtered.Count} of {Nodes.Count} nodes", 0.8f).X, y), $"{filtered.Count} of {Nodes.Count} nodes", Theme.Text3, 0.8f);
            foreach (var g in groups)
            {
                y += GroupTitle(new Vector2(p.X, y), g.title, g.rows.Count, g.key == "up" ? Theme.Live : null);
                if (g.rows.Count == 0)
                {
                    Ui.Text(Ui.Dl, new Vector2(p.X + S(4), y), g.key == "up" ? "No node in your filters is up right now." : "None", Theme.Text3, 0.9f);
                    y += LH + S(12);
                    continue;
                }
                float rh = NodeRowH;
                foreach (var (n, st) in g.rows)
                {
                    if (Visible(y, rh)) NodeRow(new Vector2(p.X, y), w, n, st.Up, st.Secs);
                    y += rh + S(6);
                }
                y += S(8);
            }
            return y;
        }

        private static float NodeRowH => S(10) * 2 + LH + S(4) + Ui.TextSize("Ag", 0.85f).Y * 2 + S(3);

        private void NodeRow(Vector2 p, float w, GatheringNode n, bool up, double secs)
        {
            var dl = Ui.Dl;
            float h = NodeRowH, pad = S(10), icon = S(36), bs = S(28);
            bool fav = IsFav(n.id), alarm = HasAlarm(n.id);
            Ui.Panel(dl, p, p + new Vector2(w, h), up ? Theme.Live.With(0.06f) : Theme.Night2.With(0.55f), fav ? Theme.GoldLine : up ? Theme.Live.With(0.3f) : Theme.CrystalLine, 6f);
            GameIcon(n.image, p + new Vector2(pad, (h - icon) / 2), icon);

            // right block: timer (+ bar) over the action buttons
            uint aeth = n.map > 0 ? AetheryteFor(n.map, n.aetheryte) : 0;
            float right = p.X + w - pad;
            string timer = up ? $"{EorzeaTimeHelper.Format(secs)} left" : $"in {EorzeaTimeHelper.Format(secs)}";
            var tsz = Ui.TextSize(timer);
            float blockW = Math.Max(tsz.X, bs * 5 + S(16));
            Ui.Text(dl, new Vector2(right - tsz.X, p.Y + pad), timer, up ? Theme.Live : Theme.Text2);
            if (up) Ui.Progress(dl, new Vector2(right - blockW, p.Y + pad + LH + S(2)), blockW, (float)(secs / (EorzeaTimeHelper.Duration(n) * 175)), Theme.Live, 3f);
            float by = p.Y + h - pad - bs + S(2);
            float bx = right - bs;
            if (Ui.IconButton("ga_al" + n.id, alarm ? FontAwesomeIcon.Bell : FontAwesomeIcon.BellSlash, new Vector2(bx, by), bs, alarm ? "Remove alarm" : "Set alarm: rings in game when it spawns", alarm ? Theme.Gold : null, alarm)) ToggleAlarm(n.id);
            bx -= bs + S(4);
            if (Ui.IconButton("ga_fv" + n.id, FontAwesomeIcon.Star, new Vector2(bx, by), bs, fav ? "Remove favourite" : "Add favourite (Home widget, notifications)", fav ? Theme.Gold : Theme.Text3, fav)) ToggleFav(n.id);
            bx -= bs + S(4);
            MarketButton("ga_mk" + n.id, new Vector2(bx, by), bs, (int)n.ItemId, n.name);
            if (OpenMarket != null) bx -= bs + S(4);
            PlaceButtons("ga_pl" + n.id, bx + bs, by, bs, n.map, n.x, n.y, $"{n.zone} ({n.coords})", aeth);

            // text
            float tx = p.X + pad + icon + S(12), tw = right - blockW - S(12) - tx;
            float ty = p.Y + pad;
            // tooltip over the text block
            ImGui.SetCursorScreenPos(new Vector2(tx, ty));
            ImGui.InvisibleButton("ga_nt" + n.id, new Vector2(Math.Max(1, tw), h - pad * 2));
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered())
            {
                var tip = $"{n.name}  ·  {TypeTip.GetValueOrDefault(n.nodeType, n.nodeType)}\n{n.zone}{(string.IsNullOrEmpty(n.area) ? "" : " · " + n.area)} ({n.coords})\nSpawns {Hours(n.hours)} ET, up for {EorzeaTimeHelper.Duration(n)} ET hours";
                if (n.otherItems.Count > 0) tip += $"\nSame node also has: {string.Join(", ", n.otherItems)}";
                UIHelper.DrawTooltip(tip);
            }
            string name = Fit(n.name, tw * 0.6f);
            Ui.Text(dl, new Vector2(tx, ty), name, fav ? Theme.Gold : Theme.Text);
            float cx = tx + Ui.TextSize(name).X + S(8), tagY = ty + (LH - TagH()) / 2;
            cx += Tag(new Vector2(cx, tagY), n.nodeType, TypeColor(n.nodeType)) + S(4);
            if (n.hidden && cx < tx + tw - S(60)) cx += Tag(new Vector2(cx, tagY), "Hidden", ColHidden) + S(4);
            if (n.ItemId > 0 && cx < tx + tw - S(40)) HeldChip(new Vector2(cx, tagY), Held(n.ItemId));

            float l2 = ty + LH + S(4), l3 = l2 + Ui.TextSize("Ag", 0.85f).Y + S(3);
            string price = _configuration.EnableGatheringPrices && _prices.TryGetValue((int)n.ItemId, out var pr) && pr > 0 ? $"{pr:N0} gil" : "";
            Meta(new Vector2(tx, l2), tw, new[] { $"{n.type} Lv {n.level}", n.slot.Length > 0 ? $"Slot {n.slot}" : "", $"{n.zone}{(string.IsNullOrEmpty(n.area) ? "" : " · " + n.area)} ({n.coords})", string.IsNullOrEmpty(n.aetheryte) ? "" : "Aetheryte " + n.aetheryte, price }, Theme.Text2);
            var sub = new List<string>();
            if (!string.IsNullOrEmpty(n.folklore)) sub.Add(n.folklore);
            sub.Add($"{Hours(n.hours)} ET");
            if (n.otherItems.Count > 0) sub.Add("also " + string.Join(", ", n.otherItems.Take(3)) + (n.otherItems.Count > 3 ? "..." : ""));
            Meta(new Vector2(tx, l3), tw, sub, Theme.Text3);
        }
    }
}
