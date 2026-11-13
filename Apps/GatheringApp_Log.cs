using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Gathering log (website GatheringSearch): 5-level brackets per class or a name search, add to the route
    public partial class GatheringApp
    {
        private class LogItem
        {
            public int id { get; set; }
            public string name { get; set; } = "";
            public string icon { get; set; } = "";
            public int lvl { get; set; }
            public int stars { get; set; }
            public bool hidden { get; set; }
            public List<string> tags { get; set; } = new();
            public List<int> spawns { get; set; } = new();
            public double duration { get; set; }
            public List<string> zones { get; set; } = new();
            public string? job { get; set; }
            public string? category { get; set; }
        }
        private class LogBracket
        {
            public int startLevel { get; set; }
            public Dictionary<string, List<LogItem>> categories { get; set; } = new();
        }

        private static readonly (string Id, string Name, string[] Cats)[] Jobs = { ("MIN", "Miner", new[] { "Mining", "Quarrying" }), ("BTN", "Botanist", new[] { "Logging", "Harvesting" }) };
        private static readonly (string Name, int Min, int Max)[] LogExpansions =
        {
            ("A Realm Reborn", 1, 50), ("Heavensward", 51, 60), ("Stormblood", 61, 70), ("Shadowbringers", 71, 80), ("Endwalker", 81, 90), ("Dawntrail", 91, 100),
        };

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<LogBracket>> _logCache = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _logLoading = new();
        private string _logError = "";
        private string _logQuery = "", _searchedQuery = "";
        private DateTime _logQueryAt;
        private List<LogItem>? _logResults;
        private bool _logSearching;
        private int _logReq;
        private readonly Dictionary<int, DateTime> _addedAt = new();

        private string LogJob => _logState.job == "BTN" ? "BTN" : "MIN";

        private void EnsureLog(string job)
        {
            if (_logCache.ContainsKey(job) || !_sender.HasToken || !_logLoading.TryAdd(job, true)) return;
            _logError = "";
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Get($"/api/gathering/log?job={job}", okOnly: false);
                    var tok = string.IsNullOrEmpty(json) ? null : JToken.Parse(json);
                    if (tok?["brackets"] is JArray arr) _logCache[job] = arr.ToObject<List<LogBracket>>() ?? new();
                    else _logError = tok?["error"]?.ToString() is { Length: > 0 } e ? e : "Could not load the gathering log.";
                }
                catch (Exception ex) { _log.Error(ex, "Gathering log failed"); _logError = "Could not load the gathering log."; }
                finally { _logLoading.TryRemove(job, out _); }
            });
        }

        private void UpdateLogSearch()
        {
            var q = _logQuery.Trim();
            if (q.Length < 2) { _logResults = null; _searchedQuery = ""; return; }
            if (q == _searchedQuery || (DateTime.Now - _logQueryAt).TotalMilliseconds < 250) return;
            _searchedQuery = q;
            int req = ++_logReq;
            _logSearching = true;
            Task.Run(async () =>
            {
                List<LogItem> list = new();
                try
                {
                    var json = await _sender.Get($"/api/gathering/search?q={HubClient.Q(q)}", okOnly: false);
                    if (!string.IsNullOrEmpty(json) && JToken.Parse(json)["results"] is JArray arr) list = arr.ToObject<List<LogItem>>() ?? new();
                }
                catch (Exception ex) { _log.Debug($"[Aetherhold] gathering search: {ex.Message}"); }
                if (req != _logReq) return;
                _logResults = list;
                _logSearching = false;
            });
        }

        private void DrawLog()
        {
            var job = LogJob;
            EnsureLog(job);
            UpdateLogSearch();
            var avail = ImGui.GetContentRegionAvail();
            bool wide = avail.X >= S(720);
            var brackets = _logCache.GetValueOrDefault(job);
            var current = brackets == null || brackets.Count == 0 ? null : brackets.FirstOrDefault(b => b.startLevel == _logState.bracket) ?? brackets[0];

            if (wide)
            {
                float sideW = S(250);
                if (UIHelper.BeginSmoothChild("ga_log_side", new Vector2(sideW, avail.Y)))
                {
                    var p = ImGui.GetCursorScreenPos();
                    float y = DrawLogSide(p, sideW - S(10), brackets, current);
                    Advance(p, y - p.Y);
                }
                ImGui.EndChild();
                ImGui.SameLine(0, S(12));
            }
            var mainSize = new Vector2(wide ? ImGui.GetContentRegionAvail().X : avail.X, avail.Y);
            if (UIHelper.BeginSmoothChild("ga_log_main", mainSize))
            {
                var p = ImGui.GetCursorScreenPos();
                float w = mainSize.X - S(10), y = p.Y;
                if (!wide) y = DrawLogTopBar(p, w, brackets, current) + S(12);
                y = DrawLogMain(new Vector2(p.X, y), w, job, current);
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private void SetJob(string job) { _logState.job = job; _logState.bracket = 0; WriteLog(); }
        private void SetBracket(int start) { _logQuery = ""; _logResults = null; _logState.bracket = start; WriteLog(); }

        private float DrawJobSeg(Vector2 p, float w)
        {
            float h = S(32), bw = (w - S(6)) / 2;
            for (int i = 0; i < Jobs.Length; i++)
                if (Ui.Button("ga_job" + i, Jobs[i].Name, p + new Vector2(i * (bw + S(6)), 0), new Vector2(bw, h), LogJob == Jobs[i].Id ? Kind.Primary : Kind.Ghost) && LogJob != Jobs[i].Id)
                    SetJob(Jobs[i].Id);
            return p.Y + h;
        }

        private float DrawLogSearch(Vector2 p, float w)
        {
            string before = _logQuery;
            float clearW = _logQuery.Length > 0 ? S(30) : 0;
            Input("##ga_logq", ref _logQuery, "Find any gatherable item...", w - clearW, p, 80);
            if (_logQuery != before) _logQueryAt = DateTime.Now;
            if (clearW > 0 && Ui.IconButton("ga_logq_x", FontAwesomeIcon.Times, p + new Vector2(w - S(28), (FrameH - S(28)) / 2), S(28), "Clear")) { _logQuery = ""; _logResults = null; }
            return p.Y + FrameH;
        }

        private float DrawLogSide(Vector2 p, float w, List<LogBracket>? brackets, LogBracket? current)
        {
            var dl = Ui.Dl;
            float y = DrawJobSeg(p, w) + S(10);
            y = DrawLogSearch(new Vector2(p.X, y), w) + S(12);
            if (brackets == null)
            {
                Ui.Text(dl, new Vector2(p.X, y), _logError.Length > 0 ? Fit(_logError, w) : "Loading...", _logError.Length > 0 ? Theme.Bad : Theme.Text3);
                return y + LH;
            }
            float bh = S(30), gap = S(6), bw = (w - S(12) - gap) / 2;
            foreach (var exp in LogExpansions)
            {
                var list = brackets.Where(b => b.startLevel >= exp.Min && b.startLevel <= exp.Max).ToList();
                if (list.Count == 0) continue;
                bool open = _logState.open.TryGetValue(exp.Name, out var o) ? o : list.Any(b => b.startLevel == current?.startLevel);
                float hh = S(32);
                bool click = Ui.Hit("ga_exp_" + exp.Name, new Vector2(p.X, y), new Vector2(w, hh), out float hv);
                Ui.Panel(dl, new Vector2(p.X, y), new Vector2(p.X + w, y + hh), Theme.Night2.With(0.5f + 0.25f * hv), Theme.CrystalLine, 6f);
                Ui.Icon(dl, new Vector2(p.X + S(10), y + (hh - LH) / 2 + S(1)), open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, Theme.Text2, 0.8f);
                Ui.Text(dl, new Vector2(p.X + S(30), y + (hh - LH) / 2), Fit(exp.Name, w - S(100)), Theme.Text);
                string range = $"{exp.Min}-{exp.Max}";
                Ui.Text(dl, new Vector2(p.X + w - S(10) - Ui.TextSize(range, 0.8f).X, y + (hh - Ui.TextSize(range, 0.8f).Y) / 2), range, Theme.Text3, 0.8f);
                if (click) { _logState.open[exp.Name] = !open; WriteLog(); }
                y += hh + S(6);
                if (!open) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    var pos = new Vector2(p.X + S(6) + (i % 2) * (bw + gap), y + (i / 2) * (bh + gap));
                    bool on = _logResults == null && current?.startLevel == b.startLevel;
                    if (Ui.Button("ga_br" + b.startLevel, $"Lv {b.startLevel}-{Math.Min(100, b.startLevel + 4)}", pos, new Vector2(bw, bh), on ? Kind.Primary : Kind.Ghost)) SetBracket(b.startLevel);
                }
                y += (list.Count + 1) / 2 * (bh + gap) + S(6);
            }
            return y;
        }

        /// <summary>Narrow tablets: class, search and a bracket picker above the list.</summary>
        private float DrawLogTopBar(Vector2 p, float w, List<LogBracket>? brackets, LogBracket? current)
        {
            float half = (w - S(10)) / 2;
            float y = DrawJobSeg(p, Math.Min(w, S(320)));
            y += S(8);
            DrawLogSearch(new Vector2(p.X, y), half);
            if (brackets != null && brackets.Count > 0)
            {
                var opts = brackets.Select(b => (b.startLevel.ToString(), $"Lv {b.startLevel}-{Math.Min(100, b.startLevel + 4)}  ({LogExpansions.FirstOrDefault(e => b.startLevel >= e.Min && b.startLevel <= e.Max).Name})")).ToArray();
                if (Combo("##ga_brc", (current?.startLevel ?? 0).ToString(), opts, half, new Vector2(p.X + half + S(10), y), "Level bracket") is { } v && int.TryParse(v, out var sl)) SetBracket(sl);
            }
            return y + FrameH;
        }

        private float DrawLogMain(Vector2 p, float w, string job, LogBracket? current)
        {
            var dl = Ui.Dl;
            float y = p.Y;
            var jobDef = Jobs.First(j => j.Id == job);
            List<LogItem> shown;
            if (_logResults != null || (_logSearching && _logQuery.Trim().Length >= 2))
            {
                var results = _logResults ?? new();
                y += GroupTitle(new Vector2(p.X, y), $"Results for \"{_logQuery.Trim()}\"", results.Count);
                y = DrawPricesBar(new Vector2(p.X, y), w, results.Select(r => r.id)) ;
                if (results.Count == 0)
                {
                    Empty(new Vector2(p.X, y), w, _logSearching ? "Searching..." : "No gatherable item found. Fish are in the Fishing panel.", FontAwesomeIcon.Search);
                    return y + EmptyH;
                }
                foreach (var it in results) { if (Visible(y, LogRowH)) LogRow(new Vector2(p.X, y), w, it, true); y += LogRowH + S(6); }
                return y;
            }
            if (_logError.Length > 0 && current == null) { Message(new Vector2(p.X, y), w, _logError, Theme.Bad); return y + LH + S(20); }
            if (current == null) { Empty(new Vector2(p.X, y), w, "Loading the gathering log...", FontAwesomeIcon.Sync); return y + EmptyH; }

            y += GroupTitle(new Vector2(p.X, y), $"{jobDef.Name} log · Lv {current.startLevel}-{Math.Min(100, current.startLevel + 4)}");
            shown = jobDef.Cats.SelectMany(c => current.categories.GetValueOrDefault(c) ?? new()).ToList();
            y = DrawPricesBar(new Vector2(p.X, y), w, shown.Select(i => i.id));
            foreach (var cat in jobDef.Cats)
            {
                var items = current.categories.GetValueOrDefault(cat);
                if (items == null || items.Count == 0) continue;
                float tw = Ui.Caps(dl, new Vector2(p.X, y), cat, Theme.Text2);
                Ui.Text(dl, new Vector2(p.X + tw + S(10), y - S(1)), items.Count.ToString(), Theme.Text3, 0.82f);
                y += CapsH + S(8);
                foreach (var it in items) { if (Visible(y, LogRowH)) LogRow(new Vector2(p.X, y), w, it, false); y += LogRowH + S(6); }
                y += S(8);
            }
            Ui.Text(dl, new Vector2(p.X, y), Fit("Counts on the rows: what you have in your bags.", w, 0.8f), Theme.Text3, 0.8f);
            return y + LH;
        }

        // ---- prices (/api/market/batch on the home world; lowest listing) -------------------------------------------
        private Dictionary<int, int> _prices = new();
        private bool _pricesLoading;
        private string _pricesError = "";

        private float DrawPricesBar(Vector2 p, float w, IEnumerable<int> ids)
        {
            if (!_configuration.EnableGatheringPrices) return p.Y;
            var list = ids.Distinct().ToList();
            float bh = S(28);
            var at = p;
            if (FlowButton("ga_prices", _pricesLoading ? "Fetching..." : "Prices", FontAwesomeIcon.Coins, Kind.Ghost, ref at, p.X, p.X + w, bh) && !_pricesLoading && list.Count > 0) FetchPrices(list);
            Ui.Tooltip("Lowest market board price on your home world for the items listed");
            if (_pricesError.Length > 0) Ui.Text(Ui.Dl, new Vector2(at.X + S(4), p.Y + (bh - LH) / 2), Fit(_pricesError, p.X + w - at.X - S(8), 0.9f), Theme.Bad, 0.9f);
            return p.Y + bh + S(10);
        }

        private void FetchPrices(List<int> ids)
        {
            string world = "";
            try { if (_objectTable[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc) world = pc.HomeWorld.Value.Name.ToString(); } catch { }
            if (world.Length == 0) { _pricesError = "Log in to see prices for your home world."; return; }
            _pricesLoading = true;
            _pricesError = "";
            Task.Run(async () =>
            {
                var next = new Dictionary<int, int>(_prices);
                try
                {
                    for (int i = 0; i < ids.Count; i += 100)
                    {
                        var chunk = ids.Skip(i).Take(100).ToList();
                        var json = await _sender.Get($"/api/market/batch?world={HubClient.Q(world)}&itemIds={string.Join(",", chunk)}", okOnly: false);
                        var tok = string.IsNullOrEmpty(json) ? null : JToken.Parse(json);
                        if (tok?["market"] is not JObject market) { _pricesError = tok?["error"]?.ToString() is { Length: > 0 } e ? e : "Universalis did not answer."; break; }
                        foreach (var id in chunk)
                        {
                            var min = (market[id.ToString()]?["listings"] as JArray)?.Select(l => l.Value<int?>("pricePerUnit") ?? 0).Where(v => v > 0).DefaultIfEmpty(0).Min() ?? 0;
                            next[id] = min;
                        }
                    }
                }
                catch (Exception ex) { _log.Error(ex, "Gathering prices failed"); _pricesError = "The prices could not be read."; }
                _prices = next;
                _pricesLoading = false;
            });
        }

        // ---- one item ------------------------------------------------------------------------------------------------
        private static float LogRowH => S(9) * 2 + LH + S(4) + Ui.TextSize("Ag", 0.85f).Y;

        /// <summary>Where an item is gathered: timed node first, else the first gathering point in nodes.json.</summary>
        private readonly Dictionary<int, (int map, double x, double y, string where, uint aeth)> _locCache = new();
        private (int map, double x, double y, string where, uint aeth) LocateItem(int itemId)
        {
            if (_locCache.TryGetValue(itemId, out var hit)) return hit;
            (int, double, double, string, uint) r = (0, 0, 0, "", 0);
            var tn = Nodes.FirstOrDefault(n => n.ItemId == itemId && n.map > 0);
            if (tn != null) r = (tn.map, tn.x, tn.y, $"{tn.zone} ({tn.coords})", AetheryteFor(tn.map, tn.aetheryte));
            else
            {
                var dn = DataNodesMap.Values.FirstOrDefault(n => n.zoneid > 0 && n.map > 0 && ((n.items?.Contains(itemId) ?? false) || (n.hiddenItems?.Contains(itemId) ?? false)));
                if (dn != null)
                {
                    var zone = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>().GetRowOrDefault((uint)dn.zoneid)?.Name.ToString() ?? "";
                    r = (dn.map, dn.x, dn.y, $"{zone} (X: {dn.x:0.0}, Y: {dn.y:0.0})", AetheryteFor(dn.map, null));
                }
            }
            if (DataNodesMap.Count > 0 || r.Item1 > 0) _locCache[itemId] = r;
            return r;
        }

        private void LogRow(Vector2 p, float w, LogItem it, bool search)
        {
            var dl = Ui.Dl;
            float h = LogRowH, pad = S(9), icon = S(32), bs = S(28);
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.55f), Theme.CrystalLine, 6f);
            GameIcon(it.icon, p + new Vector2(pad, (h - icon) / 2), icon);

            // actions (right): Route, market, map, teleport
            float right = p.X + w - pad, by = p.Y + (h - bs) / 2;
            bool inRoute = _route.Any(r => r.id == it.id);
            bool added = _addedAt.TryGetValue(it.id, out var at) && (DateTime.Now - at).TotalSeconds < 1.2;
            string rl = added ? "Added" : "Route";
            float rw = Ui.TextSize(rl).X + Ui.IconSize(FontAwesomeIcon.Route, 0.9f).X + S(30);
            if (Ui.Button("ga_lr" + it.id + (search ? "s" : ""), rl, new Vector2(right - rw, by), new Vector2(rw, bs), inRoute ? Kind.Primary : Kind.Ghost, added ? FontAwesomeIcon.Check : FontAwesomeIcon.Route))
            {
                AddToRoute(it.id, it.name, it.icon, it.lvl);
                _addedAt[it.id] = DateTime.Now;
            }
            Ui.Tooltip(inRoute ? "In your route: adds one more" : "Add to your gathering route");
            float bx = right - rw - S(6) - bs;
            if (MarketButton("ga_lm" + it.id + (search ? "s" : ""), new Vector2(bx, by), bs, it.id, it.name)) { }
            if (OpenMarket != null) bx -= bs + S(4);
            var loc = LocateItem(it.id);
            float pw = PlaceButtons("ga_lp" + it.id + (search ? "s" : ""), bx + bs, by, bs, loc.map, loc.x, loc.y, loc.where, loc.aeth);
            float textRight = bx + bs - pw - S(10);

            float tx = p.X + pad + icon + S(12), tw = textRight - tx, ty = p.Y + pad;
            ImGui.SetCursorScreenPos(new Vector2(tx, ty));
            ImGui.InvisibleButton("ga_lt" + it.id + (search ? "s" : ""), new Vector2(Math.Max(1, tw), h - pad * 2));
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered())
            {
                var tip = it.name;
                if (it.spawns.Count > 0) tip += $"\nTimed node: spawns at {Hours(it.spawns)} ET";
                if (it.zones.Count > 0) tip += $"\nZones: {string.Join(", ", it.zones)}";
                if (it.hidden) tip += "\nHidden item: appears after a node action";
                UIHelper.DrawTooltip(tip);
            }
            string name = Fit(it.name, tw * 0.6f);
            Ui.Text(dl, new Vector2(tx, ty), name, Theme.Text);
            float cx = tx + Ui.TextSize(name).X + S(8), tagY = ty + (LH - TagH()) / 2;
            if (it.stars > 0) { string st = new('★', it.stars); Ui.Text(dl, new Vector2(cx, ty), st, Theme.Warn, 0.85f); cx += Ui.TextSize(st, 0.85f).X + S(6); }
            if (it.hidden && cx < tx + tw - S(60)) cx += Tag(new Vector2(cx, tagY), "Hidden", ColHidden) + S(4);
            foreach (var t in it.tags) if (cx < tx + tw - S(80)) cx += Tag(new Vector2(cx, tagY), t, TypeColor(t)) + S(4);
            if (cx < tx + tw - S(40)) HeldChip(new Vector2(cx, tagY), Held((uint)it.id));

            var parts = new List<string> { $"Lv {it.lvl}" };
            if (it.job != null) parts.Add((it.job == "MIN" ? "Miner" : "Botanist") + (it.category != null ? $" · {it.category}" : ""));
            if (it.spawns.Count > 0 && EorzeaTimeHelper.Spawn(it.spawns, it.duration > 0 ? it.duration : 2, EorzeaTimeHelper.EtMs()) is { } st2)
                parts.Add(st2.Up ? $"up · {EorzeaTimeHelper.Format(st2.Secs)} left" : $"in {EorzeaTimeHelper.Format(st2.Secs)}");
            if (it.zones.Count > 0) parts.Add(string.Join(", ", it.zones.Take(2)) + (it.zones.Count > 2 ? "..." : ""));
            if (_configuration.EnableGatheringPrices && _prices.TryGetValue(it.id, out var pr)) parts.Add(pr > 0 ? $"{pr:N0} gil" : "no listings");
            Meta(new Vector2(tx, ty + LH + S(4)), tw, parts, Theme.Text2);
        }
    }
}
