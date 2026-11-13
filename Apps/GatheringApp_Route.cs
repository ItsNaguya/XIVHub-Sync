using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Route (website GatheringRouteTab): gathering.route items as stops, one main gathering point per item, saved lists.
    // The plugin adds the running route: bag counts tick items off, the next stop gets a map flag and a teleport.
    public partial class GatheringApp
    {
        private class RouteAeth { public string name { get; set; } = ""; public double x { get; set; } public double y { get; set; } }
        private class RouteDrop { public int id { get; set; } public string name { get; set; } = ""; public string icon { get; set; } = ""; public bool hidden { get; set; } }
        private class RouteNode
        {
            public int id { get; set; }
            public string name { get; set; } = "";
            public int type { get; set; }
            public string cls { get; set; } = "";
            public int level { get; set; }
            public int zoneid { get; set; }
            public string zoneName { get; set; } = "";
            public string area { get; set; } = "";
            public int? map { get; set; }
            public List<double>? coords { get; set; }
            public List<int> spawns { get; set; } = new();
            public double duration { get; set; }
            public string? nodeType { get; set; }
            public string? folklore { get; set; }
            public RouteAeth? aetheryte { get; set; }
            public List<RouteDrop> items { get; set; } = new();

            public int Map => map ?? 0;
            public double X => coords is { Count: >= 2 } ? coords[0] : 0;
            public double Y => coords is { Count: >= 2 } ? coords[1] : 0;
            public string Where => coords is { Count: >= 2 } ? $"{zoneName} ({X:0.0}, {Y:0.0})" : zoneName;
        }
        private sealed class Stop { public RouteNode Node = null!; public List<int> ItemIds = new(); }

        private List<RouteNode> _routeNodes = new();
        private string _routeKey = "", _routeError = "";
        private bool _routeLoading;
        private int _routeReq;
        private List<Stop> _stops = new();
        private Dictionary<int, List<RouteNode>> _byItem = new();
        private List<GatherRouteItem> _unmapped = new();
        private DateTime _planAt = DateTime.MinValue;
        private int? _selNode;
        private bool _listsOpen, _confirmClear;
        private string _listName = "", _confirmDelList = "";

        // running route (plugin only)
        private bool _running;
        private int _lastTargetNode, _dismissedFor;

        private void EnsureRouteData()
        {
            var key = string.Join(",", _route.Select(i => i.id).Distinct().OrderBy(i => i));
            if (key == _routeKey) return;
            _routeKey = key;
            int req = ++_routeReq;
            if (key.Length == 0) { _routeNodes = new(); _planAt = DateTime.MinValue; return; }
            if (!_sender.HasToken) return;
            _routeLoading = true;
            _routeError = "";
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Get($"/api/gathering/route-data?itemIds={key}", okOnly: false);
                    var tok = string.IsNullOrEmpty(json) ? null : JToken.Parse(json);
                    if (req != _routeReq) return;
                    if (tok?["nodes"] is JArray arr) _routeNodes = arr.ToObject<List<RouteNode>>() ?? new();
                    else { _routeError = tok?["error"]?.ToString() is { Length: > 0 } e ? e : "Could not load the route data."; _routeKey = ""; }
                }
                catch (Exception ex) { _log.Error(ex, "Route data failed"); _routeError = "Could not load the route data."; _routeKey = ""; }
                finally { if (req == _routeReq) { _routeLoading = false; _planAt = DateTime.MinValue; } }
            });
        }

        private static double Key(RouteNode n, double et)
            => EorzeaTimeHelper.Spawn(n.spawns, n.duration > 0 ? n.duration : 2, et) is { } st ? (st.Up ? 0 : st.Secs) : double.MaxValue;

        /// <summary>The website's plan: main stop per item (timed: next up; else the zone shared with most items), then sorted.</summary>
        private void Plan()
        {
            double et = EorzeaTimeHelper.EtMs();
            var nodes = _routeNodes;
            var wanted = _route.Select(i => i.id).ToHashSet();
            var byItem = new Dictionary<int, List<RouteNode>>();
            foreach (var n in nodes)
                foreach (var it in n.items)
                {
                    if (!wanted.Contains(it.id)) continue;
                    if (!byItem.TryGetValue(it.id, out var list)) byItem[it.id] = list = new();
                    if (!list.Any(x => x.zoneid == n.zoneid && x.type == n.type && string.Join(",", x.spawns) == string.Join(",", n.spawns))) list.Add(n);
                }
            var zoneScore = new Dictionary<string, int>();
            foreach (var list in byItem.Values)
                foreach (var z in list.Select(n => n.zoneName).Distinct()) zoneScore[z] = zoneScore.GetValueOrDefault(z) + 1;
            var main = new Dictionary<int, RouteNode>();
            foreach (var (id, list) in byItem)
            {
                var timed = list.Where(n => n.spawns.Count > 0).ToList();
                main[id] = timed.Count > 0
                    ? timed.OrderBy(n => Key(n, et)).First()
                    : list.OrderByDescending(n => zoneScore.GetValueOrDefault(n.zoneName)).ThenBy(n => n.level).First();
            }
            var stops = new List<Stop>();
            foreach (var it in _route)
            {
                if (!main.TryGetValue(it.id, out var n)) continue;
                var s = stops.FirstOrDefault(x => x.Node.id == n.id);
                if (s != null) s.ItemIds.Add(it.id); else stops.Add(new Stop { Node = n, ItemIds = new() { it.id } });
            }
            _stops = stops.OrderBy(s => Key(s.Node, et)).ThenBy(s => s.Node.zoneName, StringComparer.Ordinal).ToList();
            _byItem = byItem;
            _unmapped = _route.Where(i => !byItem.ContainsKey(i.id)).ToList();
            _planAt = DateTime.Now;
        }

        private bool ItemDone(int id) => _route.FirstOrDefault(i => i.id == id) is { } it && Held((uint)id) >= it.Qty;

        /// <summary>The running route's next stop: first unfinished stop that is up (or untimed); else the next timed one, waiting.</summary>
        private (Stop? stop, bool waiting) Target()
        {
            double et = EorzeaTimeHelper.EtMs();
            Stop? firstOpen = null;
            foreach (var s in _stops)
            {
                if (s.ItemIds.All(ItemDone)) continue;
                firstOpen ??= s;
                var st = EorzeaTimeHelper.Spawn(s.Node.spawns, s.Node.duration > 0 ? s.Node.duration : 2, et);
                if (st == null || st.Value.Up) return (s, false);
            }
            return (firstOpen, firstOpen != null);
        }

        private void TickRoute()
        {
            EnsureRouteData();
            if ((DateTime.Now - _planAt).TotalSeconds >= 5) Plan();
            if (!_running) return;
            if (_route.Count == 0) { _running = false; return; }
            if (_routeLoading || _routeNodes.Count == 0) return;
            if (_route.All(i => Held((uint)i.id) >= i.Qty))
            {
                _running = false;
                Notifications.Post("route:done", "Gathering", "Route complete", $"You have everything on your gathering route ({Plural(_route.Count, "item")}).", Name);
                return;
            }
            var (stop, waiting) = Target();
            if (stop == null || waiting || stop.Node.id == _lastTargetNode) return;
            _lastTargetNode = stop.Node.id;
            var n = stop.Node;
            if (n.Map <= 0 || n.X <= 0) return;
            if (_clientState.TerritoryType == TerritoryOfMap(n.Map)) OpenMapAt(n.Map, n.X, n.Y);
            else QueueMapAt(n.Map, n.X, n.Y);
        }

        /// <summary>The running route's next stop for the tablet's sleep view; null when no route runs.</summary>
        public (string Item, int Have, int Need, string Zone, uint AetheryteId, bool Waiting)? RouteNextStop()
        {
            if (!_running || _route.Count == 0) return null;
            var (stop, waiting) = Target();
            if (stop == null) return null;
            if (waiting) return ("", 0, 0, "", 0, true);
            var item = _route.FirstOrDefault(i => stop.ItemIds.Contains(i.id) && !ItemDone(i.id)) ?? _route.First(i => stop.ItemIds.Contains(i.id));
            return (item.name, Held((uint)item.id), item.Qty, stop.Node.Where, AetheryteFor(stop.Node.Map, stop.Node.aetheryte?.name), false);
        }

        private void StartRoute()
        {
            _running = true;
            _lastTargetNode = 0;
            _dismissedFor = 0;
            Plan();
        }

        // =============================================================================================================
        // Drawing
        // =============================================================================================================
        private void DrawRoute()
        {
            EnsureRouteData();
            if ((DateTime.Now - _planAt).TotalSeconds >= 5) Plan();
            var avail = ImGui.GetContentRegionAvail();
            bool wide = avail.X >= S(820) && _route.Count > 0;
            var sel = SelectedNode();
            if (wide)
            {
                float leftW = (float)Math.Floor(avail.X * 0.56f);
                if (UIHelper.BeginSmoothChild("ga_route_l", new Vector2(leftW, avail.Y)))
                {
                    var p = ImGui.GetCursorScreenPos();
                    float y = DrawRouteLeft(p, leftW - S(10));
                    Advance(p, y - p.Y);
                }
                ImGui.EndChild();
                ImGui.SameLine(0, S(12));
                var rs = new Vector2(ImGui.GetContentRegionAvail().X, avail.Y);
                if (UIHelper.BeginSmoothChild("ga_route_r", rs))
                {
                    var p = ImGui.GetCursorScreenPos();
                    float y = sel != null ? DrawNodeDetail(p, rs.X - S(10), sel, true) : p.Y;
                    Advance(p, y - p.Y);
                }
                ImGui.EndChild();
                return;
            }
            if (UIHelper.BeginSmoothChild("ga_route", avail))
            {
                var p = ImGui.GetCursorScreenPos();
                float w = avail.X - S(10);
                float y = DrawRouteLeft(p, w);
                if (sel != null) y = DrawNodeDetail(new Vector2(p.X, y + S(12)), w, sel, w >= S(480));
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private RouteNode? SelectedNode()
        {
            if (_selNode is { } id && _routeNodes.FirstOrDefault(n => n.id == id) is { } n) return n;
            if (_running && Target().stop is { } t) return t.Node;
            return _stops.FirstOrDefault()?.Node;
        }

        private float DrawRouteLeft(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float y = p.Y;
            if (_route.Count == 0)
            {
                Empty(p, w, "Your gathering route is empty. Add items from the Gathering log (Route button).", FontAwesomeIcon.Route);
                y += EmptyH + S(12);
                if (_lists.Count > 0) y = DrawLists(new Vector2(p.X, y), w, false);
                return y;
            }

            // head: title + tools
            var zones = _stops.Select(s => s.Node.zoneName).Distinct().Count();
            float tw = Ui.Caps(dl, new Vector2(p.X, y), "Route");
            Ui.Text(dl, new Vector2(p.X + tw + S(10), y - S(1)), $"{Plural(_route.Count, "item")} · {Plural(zones, "zone")}", Theme.Text3, 0.82f);
            y += CapsH + S(10);
            float bh = S(30);
            var at = new Vector2(p.X, y);
            if (_running)
            {
                if (FlowButton("ga_pause", "Pause route", FontAwesomeIcon.Pause, Kind.Gold, ref at, p.X, p.X + w, bh)) _running = false;
                Ui.Tooltip("Stop following the route (nothing is lost)");
            }
            else
            {
                if (FlowButton("ga_start", "Start route", FontAwesomeIcon.Play, Kind.Primary, ref at, p.X, p.X + w, bh) && _stops.Count > 0) StartRoute();
                Ui.Tooltip("Follow the route: the next stop gets a map flag and a teleport; items tick off from your bags");
            }
            if (FlowButton("ga_lists", _lists.Count > 0 ? $"Lists ({_lists.Count})" : "Lists", FontAwesomeIcon.FolderOpen, _listsOpen ? Kind.Primary : Kind.Ghost, ref at, p.X, p.X + w, bh)) _listsOpen = !_listsOpen;
            if (_confirmClear)
            {
                if (FlowButton("ga_clr_y", "Clear the route: yes", FontAwesomeIcon.Trash, Kind.Danger, ref at, p.X, p.X + w, bh)) { _route = new(); WriteRoute(); _running = false; _confirmClear = false; _selNode = null; }
                if (FlowButton("ga_clr_n", "No", null, Kind.Ghost, ref at, p.X, p.X + w, bh)) _confirmClear = false;
            }
            else if (FlowButton("ga_clr", "Clear", FontAwesomeIcon.Trash, Kind.Danger, ref at, p.X, p.X + w, bh)) _confirmClear = true;
            y = at.Y + bh + S(10);

            if (_listsOpen) y = DrawLists(new Vector2(p.X, y), w, true) + S(10);
            if (_running) y = DrawGuidance(new Vector2(p.X, y), w);
            if (_routeLoading) { Ui.Text(dl, new Vector2(p.X, y), "Finding gathering points...", Theme.Text2); y += LH + S(10); }
            if (_routeError.Length > 0) { Message(new Vector2(p.X, y), w, _routeError, Theme.Bad); y += LH + S(30); }

            (Stop? stop, bool waiting) target = _running ? Target() : (null, false);
            for (int i = 0; i < _stops.Count; i++) y = StopRow(new Vector2(p.X, y), w, i, _stops[i], target.stop == _stops[i]) + S(8);

            if (_unmapped.Count > 0 && !_routeLoading)
            {
                y += S(4);
                float uw = Ui.Caps(dl, new Vector2(p.X, y), "Not from a gathering point", Theme.Text2);
                Ui.Text(dl, new Vector2(p.X + uw + S(10), y - S(1)), _unmapped.Count.ToString(), Theme.Text3, 0.82f);
                y += CapsH + S(4);
                Ui.Text(dl, new Vector2(p.X, y), Fit("Fish, mob drops, vendor or crafted items. Fish: see the Fishing panel.", w, 0.82f), Theme.Text3, 0.82f);
                y += LH + S(6);
                foreach (var it in _unmapped.ToList()) y = RouteItemRow(new Vector2(p.X, y), w, it, new List<RouteNode>(), true) + S(4);
            }
            int total = _route.Sum(i => i.Qty);
            y += S(8);
            Ui.Text(dl, new Vector2(p.X, y), Fit($"{total:N0} to gather in total. Counts: what you have in your bags.", w, 0.8f), Theme.Text3, 0.8f);
            return y + LH;
        }

        /// <summary>Banner while the route runs: the next stop and a teleport when it is in another zone.</summary>
        private float DrawGuidance(Vector2 p, float w)
        {
            var (stop, waiting) = Target();
            if (stop == null) return p.Y;
            var n = stop.Node;
            uint terr = TerritoryOfMap(n.Map);
            bool here = terr != 0 && _clientState.TerritoryType == terr;
            if (_dismissedFor == n.id && !waiting) return p.Y;
            var dl = Ui.Dl;
            float pad = S(12), bh = S(30), h = pad + LH + S(4) + LH + S(10) + bh + pad;
            var col = waiting ? Theme.Warn : Theme.Live;
            Ui.Panel(dl, p, p + new Vector2(w, h), col.With(0.08f), col.With(0.5f));
            var item = _route.FirstOrDefault(i => stop.ItemIds.Contains(i.id) && !ItemDone(i.id));
            string head = waiting ? "Waiting for the next node" : here ? "Next stop: you are in the zone" : "Next stop";
            Ui.Icon(dl, p + new Vector2(pad, pad + S(1)), waiting ? FontAwesomeIcon.Hourglass : FontAwesomeIcon.Route, col, 0.9f);
            Ui.Text(dl, p + new Vector2(pad + S(24), pad), head, col);
            string what = $"{item?.name ?? n.name} at {n.Where}";
            if (EorzeaTimeHelper.Spawn(n.spawns, n.duration > 0 ? n.duration : 2, EorzeaTimeHelper.EtMs()) is { } st)
                what += st.Up ? $"  ·  up, {EorzeaTimeHelper.Format(st.Secs)} left" : $"  ·  spawns in {EorzeaTimeHelper.Format(st.Secs)}";
            Ui.Text(dl, p + new Vector2(pad, pad + LH + S(4)), Fit(what, w - pad * 2), Theme.Text);
            var at = new Vector2(p.X + pad, p.Y + pad + LH * 2 + S(14));
            uint aeth = AetheryteFor(n.Map, n.aetheryte?.name);
            if (!here && aeth != 0 && FlowButton("ga_g_tp", $"Teleport to {AetheryteName(aeth)}", FontAwesomeIcon.PlaneDeparture, Kind.Gold, ref at, p.X + pad, p.X + w - pad, bh))
            {
                TeleportToAetheryte(aeth);
                if (n.Map > 0 && n.X > 0) QueueMapAt(n.Map, n.X, n.Y);
            }
            if (n.Map > 0 && n.X > 0 && FlowButton("ga_g_map", "Map flag", FontAwesomeIcon.MapMarkerAlt, Kind.Ghost, ref at, p.X + pad, p.X + w - pad, bh)) OpenMapAt(n.Map, n.X, n.Y);
            if (!waiting && FlowButton("ga_g_x", "Dismiss", null, Kind.Ghost, ref at, p.X + pad, p.X + w - pad, bh)) _dismissedFor = n.id;
            return p.Y + h + S(10);
        }

        private float DrawLists(Vector2 p, float w, bool canSave)
        {
            var dl = Ui.Dl;
            float pad = S(12), rh = S(38);
            float h = pad + CapsH + S(8) + (canSave ? FrameH + S(10) : 0) + Math.Max(1, _lists.Count) * rh + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.75f));
            Ui.Caps(dl, p + new Vector2(pad, pad), "Saved routes");
            if (canSave && Ui.IconButton("ga_lists_x", FontAwesomeIcon.Times, new Vector2(p.X + w - pad - S(26), p.Y + pad - S(4)), S(26), "Close")) _listsOpen = false;
            float y = p.Y + pad + CapsH + S(8);
            if (canSave)
            {
                float bw = S(90);
                bool enter = Input("##ga_listname", ref _listName, "Save this route as...", w - pad * 2 - bw - S(8), new Vector2(p.X + pad, y), 60, ImGuiInputTextFlags.EnterReturnsTrue);
                if ((Ui.Button("ga_list_save", "Save", new Vector2(p.X + w - pad - bw, y), new Vector2(bw, FrameH), Kind.Gold, FontAwesomeIcon.Save) || enter) && _listName.Trim().Length > 0)
                {
                    _lists.Add(new GatherList { id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(), name = _listName.Trim(), items = Clone(_route) });
                    WriteLists();
                    _listName = "";
                }
                y += FrameH + S(10);
            }
            if (_lists.Count == 0) Ui.Text(dl, new Vector2(p.X + pad, y + (rh - LH) / 2), "No saved lists yet.", Theme.Text3);
            foreach (var list in _lists.ToList())
            {
                float bw = S(70), bh = S(28), by = y + (rh - bh) / 2, rx = p.X + w - pad;
                if (_confirmDelList == list.id)
                {
                    if (Ui.Button("ga_ld_n" + list.id, "No", new Vector2(rx - bw, by), new Vector2(bw, bh))) _confirmDelList = "";
                    if (Ui.Button("ga_ld_y" + list.id, "Delete", new Vector2(rx - bw * 2 - S(6), by), new Vector2(bw, bh), Kind.Danger))
                    {
                        _lists.RemoveAll(l => l.id == list.id);
                        WriteLists();
                        _confirmDelList = "";
                    }
                    rx -= bw * 2 + S(12);
                }
                else
                {
                    if (Ui.Button("ga_ll" + list.id, "Load", new Vector2(rx - bw, by), new Vector2(bw, bh), Kind.Primary))
                    {
                        _route = Clone(list.items);
                        WriteRoute();
                        _listsOpen = false;
                        _selNode = null;
                    }
                    Ui.Tooltip("Load this list (replaces the current route)");
                    if (Ui.IconButton("ga_ldel" + list.id, FontAwesomeIcon.Trash, new Vector2(rx - bw - S(6) - bh, by), bh, "Delete this list", Theme.Bad)) _confirmDelList = list.id;
                    rx -= bw + bh + S(12);
                }
                Ui.Text(dl, new Vector2(p.X + pad, y + (rh - LH) / 2), Fit($"{list.name}  ·  {Plural(list.items?.Count ?? 0, "item")}", rx - p.X - pad * 2), Theme.Text);
                y += rh;
            }
            return p.Y + h;
        }

        private static List<GatherRouteItem> Clone(List<GatherRouteItem>? items)
            => (items ?? new()).Where(i => i != null).Select(i => new GatherRouteItem { id = i.id, name = i.name, icon = i.icon, level = i.level, quantity = i.quantity, extra = i.extra }).ToList();

        private float StopRow(Vector2 p, float w, int index, Stop s, bool isTarget)
        {
            var dl = Ui.Dl;
            var n = s.Node;
            float pad = S(10), headH = pad + LH + S(3) + Ui.TextSize("Ag", 0.85f).Y + pad, itemH = S(34);
            float h = headH + s.ItemIds.Count * (itemH + S(4)) + S(6);
            var st = EorzeaTimeHelper.Spawn(n.spawns, n.duration > 0 ? n.duration : 2, EorzeaTimeHelper.EtMs());
            bool sel = SelectedNode()?.id == n.id, up = st is { Up: true };
            if (!Visible(p.Y, h)) return p.Y + h;
            var line = isTarget ? Theme.Gold.With(0.8f) : sel ? Ui.Accent.With(0.6f) : up ? Theme.Live.With(0.3f) : Theme.CrystalLine;
            Ui.Panel(dl, p, p + new Vector2(w, h), up ? Theme.Live.With(0.05f) : Theme.Night2.With(0.55f), line, 6f);

            // head (click selects the stop for the map)
            float bs = S(28), right = p.X + w - pad;
            uint aeth = AetheryteFor(n.Map, n.aetheryte?.name);
            float pw = PlaceButtons("ga_sp" + n.id, right, p.Y + pad, bs, n.Map, n.X, n.Y, n.Where, aeth);
            string when = st is { } x ? (x.Up ? $"up · {EorzeaTimeHelper.Format(x.Secs)}" : $"in {EorzeaTimeHelper.Format(x.Secs)}") : "";
            float whenW = when.Length > 0 ? Ui.TextSize(when, 0.9f).X : 0;
            if (when.Length > 0) Ui.Text(dl, new Vector2(right - pw - S(8) - whenW, p.Y + pad + S(2)), when, up ? Theme.Live : Theme.Text2, 0.9f);
            float headRight = right - pw - S(16) - whenW;
            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton("ga_sh" + n.id, new Vector2(Math.Max(1, headRight - p.X), headH))) _selNode = n.id;
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            float r = S(12);
            var c = p + new Vector2(pad + r, pad + LH / 2 + S(2));
            dl.AddCircleFilled(c, r, (isTarget ? Theme.Gold : Ui.Accent).With(0.2f).U32());
            dl.AddCircle(c, r, (isTarget ? Theme.Gold : Ui.Accent).With(0.7f).U32(), 0, S(1));
            string num = (index + 1).ToString();
            Ui.Text(dl, c - Ui.TextSize(num, 0.85f) / 2, num, Theme.Text, 0.85f);
            float tx = p.X + pad + r * 2 + S(10), tw = headRight - tx;
            string zone = Fit(n.zoneName + (n.area.Length > 0 ? " · " + n.area : ""), tw);
            Ui.Text(dl, new Vector2(tx, p.Y + pad), zone, Theme.Text);
            Meta(new Vector2(tx, p.Y + pad + LH + S(3)), tw, new[] { $"{n.cls} Lv {n.level}", n.name, n.coords is { Count: >= 2 } ? $"({n.X:0.0}, {n.Y:0.0})" : "", n.nodeType ?? "" }, Theme.Text2);

            float y = p.Y + headH;
            foreach (var id in s.ItemIds)
            {
                var it = _route.FirstOrDefault(i => i.id == id);
                if (it == null) continue;
                var others = (_byItem.GetValueOrDefault(id) ?? new()).Where(o => o.id != n.id).ToList();
                y = RouteItemRow(new Vector2(p.X + pad, y), w - pad * 2, it, others, false, iconFrom: n) + S(4);
            }
            return p.Y + h;
        }

        private float RouteItemRow(Vector2 p, float w, GatherRouteItem it, List<RouteNode> others, bool market, RouteNode? iconFrom = null)
        {
            var dl = Ui.Dl;
            float h = S(34), icon = S(24), bs = S(26);
            string iconStr = iconFrom?.items.FirstOrDefault(d => d.id == it.id)?.icon is { Length: > 0 } ic ? ic : it.icon;
            dl.AddRectFilled(p, p + new Vector2(w, h), Theme.Night1.With(0.45f).U32(), S(4));
            GameIcon(iconStr, p + new Vector2(S(5), (h - icon) / 2), icon);

            float right = p.X + w - S(4), cy = p.Y + (h - bs) / 2;
            if (Ui.IconButton("ga_rx" + it.id, FontAwesomeIcon.Times, new Vector2(right - bs, cy), bs, "Remove from the route", Theme.Bad))
            {
                _route.RemoveAll(i => i.id == it.id);
                WriteRoute();
                return p.Y + h;
            }
            right -= bs + S(4);
            if (market) { if (MarketButton("ga_rm" + it.id, new Vector2(right - bs, cy), bs, it.id, it.name)) { } if (OpenMarket != null) right -= bs + S(4); }
            int q = it.Qty;
            if (Stepper("ga_rq" + it.id, ref q, new Vector2(right - StepperW, cy))) { it.quantity = q; WriteRoute(); }
            right -= StepperW + S(8);
            int have = Held((uint)it.id);
            string hc = $"{have}/{it.Qty}";
            float hw = Ui.TextSize(hc, 0.78f).X + S(10);
            HeldChip(new Vector2(right - hw, p.Y + (h - TagH(0.78f)) / 2), have, it.Qty);
            right -= hw + S(8);

            float tx = p.X + S(5) + icon + S(8), tw = right - tx;
            bool done = have >= it.Qty;
            if (others.Count == 0)
            {
                Ui.Text(dl, new Vector2(tx, p.Y + (h - LH) / 2), Fit(it.name, tw), done ? Theme.Text3 : Theme.Text);
                return p.Y + h;
            }
            string name = Fit(it.name, tw * 0.55f);
            Ui.Text(dl, new Vector2(tx, p.Y + (h - LH) / 2), name, done ? Theme.Text3 : Theme.Text);
            float ax = tx + Ui.TextSize(name).X + S(10), ay = p.Y + (h - Ui.TextSize("A", 0.8f).Y) / 2;
            Ui.Text(dl, new Vector2(ax, ay), "also:", Theme.Text3, 0.8f);
            ax += Ui.TextSize("also: ", 0.8f).X;
            foreach (var o in others.Take(3))
            {
                string t = o.zoneName + (o.spawns.Count > 0 ? " (timed)" : "");
                var ts = Ui.TextSize(t, 0.8f);
                if (ax + ts.X > right) break;
                if (Ui.Hit($"ga_alt{it.id}_{o.id}", new Vector2(ax, ay), ts, out float hv)) _selNode = o.id;
                Ui.Tooltip($"{o.name} · {o.cls} Lv {o.level}: show this point");
                Ui.Text(dl, new Vector2(ax, ay), t, UIHelper.LerpColor(Ui.Accent.With(0.8f), Theme.Text, hv), 0.8f);
                ax += ts.X + S(8);
            }
            if (others.Count > 3 && ax + S(30) < right) Ui.Text(dl, new Vector2(ax, ay), $"+{others.Count - 3}", Theme.Text3, 0.8f);
            return p.Y + h;
        }

        // ---- node detail with the zone map (game texture) -------------------------------------------------------------
        private float DrawNodeDetail(Vector2 p, float w, RouteNode n, bool showMap)
        {
            var dl = Ui.Dl;
            float pad = S(14), y = p.Y + pad;
            var st = EorzeaTimeHelper.Spawn(n.spawns, n.duration > 0 ? n.duration : 2, EorzeaTimeHelper.EtMs());
            var lines = new List<(string text, Vector4 col, float scale)>
            {
                (n.zoneName + (n.area.Length > 0 ? " · " + n.area : ""), Theme.Text, 1.1f),
                (string.Join("  ·  ", new[] { n.name, $"{n.cls} Lv {n.level}", n.nodeType ?? "", n.coords is { Count: >= 2 } ? $"X {n.X:0.0} · Y {n.Y:0.0}" : "" }.Where(s => s.Length > 0)), Theme.Text2, 0.88f),
            };
            if (n.spawns.Count > 0) lines.Add(($"Spawns {Hours(n.spawns)} ET" + (st is { } x ? (x.Up ? $"  ·  up, {EorzeaTimeHelper.Format(x.Secs)} left" : $"  ·  in {EorzeaTimeHelper.Format(x.Secs)}") : ""), st is { Up: true } ? Theme.Live : Theme.Text2, 0.88f));
            if (n.aetheryte != null) lines.Add(($"Nearest aetheryte: {n.aetheryte.name}", Theme.Text2, 0.88f));
            if (!string.IsNullOrEmpty(n.folklore)) lines.Add((n.folklore!, Theme.Warn, 0.85f));
            lines.Add(("Drops: " + string.Join(", ", n.items.Select(i => i.name + (i.hidden ? " (hidden)" : ""))), Theme.Text3, 0.85f));

            float bh = S(30);
            var tex = showMap && n.Map > 0 ? MapTexture(n.Map) : null;
            float mapSide = tex != null ? Math.Min(w - pad * 2, S(520)) : 0;
            float textH = lines.Sum(l => Ui.TextSize("Ag", l.scale).Y + S(5));
            float h = pad + textH + S(6) + bh + S(10) + (mapSide > 0 ? mapSide + S(10) : 0) + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f));
            foreach (var (text, col, scale) in lines)
            {
                Ui.Text(dl, new Vector2(p.X + pad, y), Fit(text, w - pad * 2, scale), col, scale);
                y += Ui.TextSize("Ag", scale).Y + S(5);
            }
            y += S(6);
            var at = new Vector2(p.X + pad, y);
            uint aeth = AetheryteFor(n.Map, n.aetheryte?.name);
            if (aeth != 0 && FlowButton("ga_d_tp" + n.id, $"Teleport to {AetheryteName(aeth)}", FontAwesomeIcon.PlaneDeparture, Kind.Gold, ref at, p.X + pad, p.X + w - pad, bh))
            {
                TeleportToAetheryte(aeth);
                if (n.Map > 0 && n.X > 0) QueueMapAt(n.Map, n.X, n.Y);
            }
            if (n.Map > 0 && n.X > 0 && FlowButton("ga_d_map" + n.id, "Map flag", FontAwesomeIcon.MapMarkerAlt, Kind.Ghost, ref at, p.X + pad, p.X + w - pad, bh)) OpenMapAt(n.Map, n.X, n.Y);
            y = at.Y + bh + S(10);

            if (tex is { } t && mapSide > 0)
            {
                var a = new Vector2(p.X + (w - mapSide) / 2, y);
                var b = a + new Vector2(mapSide, mapSide);
                dl.AddImage(t.tex.Handle, a, b);
                dl.AddRect(a, b, Theme.CrystalLine.U32(), 0, 0, S(1));
                Vector2 Pos(double cx, double cy) => a + new Vector2((float)Math.Clamp((cx - 1) * t.sf / 41, 0, 1), (float)Math.Clamp((cy - 1) * t.sf / 41, 0, 1)) * mapSide;
                if (n.aetheryte != null && n.aetheryte.x > 0)
                {
                    var ap = Pos(n.aetheryte.x, n.aetheryte.y);
                    dl.AddCircleFilled(ap, S(7), Theme.Night0.With(0.8f).U32());
                    Ui.IconCentered(dl, ap, FontAwesomeIcon.Gem.ToIconString(), Theme.Crystal, 0.7f);
                }
                if (n.X > 0)
                {
                    var np = Pos(n.X, n.Y);
                    dl.AddCircle(np, S(14), Theme.Gold.With(0.5f).U32(), 0, S(2));
                    Ui.IconCentered(dl, np - new Vector2(0, S(10)), FontAwesomeIcon.MapMarkerAlt.ToIconString(), Theme.Gold, 1.3f);
                    string lab = $"X {n.X:0.0} Y {n.Y:0.0}";
                    var ls = Ui.TextSize(lab, 0.78f);
                    var lp = new Vector2(Math.Clamp(np.X - ls.X / 2, a.X, b.X - ls.X), Math.Min(np.Y + S(10), b.Y - ls.Y - S(2)));
                    dl.AddRectFilled(lp - S(4, 1), lp + ls + S(4, 1), Theme.Night0.With(0.85f).U32(), S(3));
                    Ui.Text(dl, lp, lab, Theme.Gold, 0.78f);
                }
                y += mapSide + S(10);
            }
            return p.Y + h;
        }

        private readonly Dictionary<int, (string path, float sf)> _mapPaths = new();

        /// <summary>The zone map texture from the game files (ui/map/{id}/{id}_m.tex) and its size factor / 100.</summary>
        private (Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap tex, float sf)? MapTexture(int mapId)
        {
            if (!_mapPaths.TryGetValue(mapId, out var mp))
            {
                mp = ("", 1f);
                var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault((uint)mapId);
                var id = row?.Id.ToString() ?? "";
                if (id.Length > 0) mp = ($"ui/map/{id}/{id.Replace("/", "")}_m.tex", row!.Value.SizeFactor / 100f);
                _mapPaths[mapId] = mp;
            }
            if (mp.path.Length == 0) return null;
            try
            {
                var wrap = _textureProvider.GetFromGame(mp.path).GetWrapOrDefault();
                return wrap == null ? null : (wrap, mp.sf);
            }
            catch { return null; }
        }
    }
}
