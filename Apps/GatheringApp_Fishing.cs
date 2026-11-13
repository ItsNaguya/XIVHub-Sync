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
    // Fishing (website FishingTab): every fish with bait, mooch, hookset and its next catch window (same maths as the
    // website: the game's weather roll from the server's weather rates). Plugin: caught fish, teleport, map, alarms.
    public partial class GatheringApp
    {
        private class Predator { public int id { get; set; } public string name { get; set; } = ""; public string icon { get; set; } = ""; public int count { get; set; } }
        private class FishEntry
        {
            public int id { get; set; }
            public string name { get; set; } = "";
            public string icon { get; set; } = "";
            public double patch { get; set; }
            public bool big { get; set; }
            public bool collectable { get; set; }
            public string? bait { get; set; }
            public List<string> mooch { get; set; } = new();
            public string? hookset { get; set; }
            public string? tug { get; set; }
            public string? bite { get; set; }
            public List<int>? time { get; set; }
            public List<int> weathers { get; set; } = new();
            public List<int> prevWeathers { get; set; } = new();
            public List<string>? ocean { get; set; }
            public string? zone { get; set; }
            public string? spot { get; set; }
            public string? aetheryte { get; set; }
            public string? folklore { get; set; }
            public bool snagging { get; set; }
            public bool fishEyes { get; set; }
            public int? intuition { get; set; }
            public List<Predator> predators { get; set; } = new();
            public int? territory { get; set; }

            public bool Ruled => time != null || weathers.Count > 0 || prevWeathers.Count > 0;
        }
        private class WeatherInfo { public string name { get; set; } = ""; public string icon { get; set; } = ""; }
        private sealed class FishData
        {
            public List<FishEntry> fish = new();
            public Dictionary<string, List<int[]>> weatherRates = new();
            public Dictionary<string, WeatherInfo> weathers = new();
        }

        private static readonly (string, string)[] FishSorts = { ("window", "Window (up first)"), ("az", "A-Z"), ("patch", "Patch (newest)") };
        private static readonly (string, string)[] FishExps = { ("All", "All patches"), ("7", "Dawntrail (7.x)"), ("6", "Endwalker (6.x)"), ("5", "Shadowbringers (5.x)"), ("4", "Stormblood (4.x)"), ("3", "Heavensward (3.x)"), ("2", "A Realm Reborn (2.x)") };

        private FishData? _fish;
        private bool _fishLoading;
        private string _fishError = "", _fishQuery = "";
        private DateTime _fishTried = DateTime.MinValue;
        private readonly Dictionary<int, (double start, double end)?> _wins = new();
        private DateTime _winsAt = DateTime.MinValue;
        private int? _openFish;
        private List<FishEntry> _fishList = new();
        private string _fishListKey = "";

        private bool WantFish => CurrentView == 3 || _alarms.Any(kv => kv.Value && kv.Key.Length > 0 && kv.Key.All(char.IsDigit));

        private void EnsureFish()
        {
            if (_fish != null || _fishLoading || !_sender.HasToken || (DateTime.Now - _fishTried).TotalSeconds < 30) return;
            _fishLoading = true;
            _fishTried = DateTime.Now;
            _fishError = "";
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Get("/api/gathering/fish", okOnly: false);
                    var tok = string.IsNullOrEmpty(json) ? null : JToken.Parse(json);
                    if (tok?["fish"] is JArray arr)
                        _fish = new FishData
                        {
                            fish = arr.ToObject<List<FishEntry>>() ?? new(),
                            weatherRates = tok["weatherRates"]?.ToObject<Dictionary<string, List<int[]>>>() ?? new(),
                            weathers = tok["weathers"]?.ToObject<Dictionary<string, WeatherInfo>>() ?? new(),
                        };
                    else _fishError = tok?["error"]?.ToString() is { Length: > 0 } e ? e : "Could not load the fishing data.";
                }
                catch (Exception ex) { _log.Error(ex, "Fishing data failed"); _fishError = "Could not load the fishing data."; }
                finally { _fishLoading = false; }
            });
        }

        // ---- windows (website gatheringUtils: weatherTarget, weatherAt, nextFishWindow) --------------------------------
        private const double Ratio = 144.0 / 7.0, PeriodMs = 1400 * 1000;

        private static int WeatherTarget(double unixMs)
        {
            long unix = (long)Math.Floor(unixMs / 1000);
            long bell = unix / 175;
            long inc = (bell + 8 - bell % 8) % 24;
            long totalDays = unix / 4200;
            int b = (int)(totalDays * 100 + inc);
            uint step1 = (uint)((b << 11) ^ b);
            uint step2 = (step1 >> 8) ^ step1;
            return (int)(step2 % 100);
        }

        private static int WeatherAt(List<int[]> rates, double unixMs)
        {
            int t = WeatherTarget(unixMs);
            foreach (var r in rates) if (r.Length >= 2 && t < r[1]) return r[0];
            return rates.Count > 0 && rates[^1].Length > 0 ? rates[^1][0] : 0;
        }

        private static (double start, double end)? NextFishWindow(FishEntry f, List<int[]>? rates, double nowMs, int periods = 600)
        {
            bool needsWeather = f.weathers.Count > 0 || f.prevWeathers.Count > 0;
            if (f.time == null && !needsWeather) return null;
            if (needsWeather && (rates == null || rates.Count == 0)) return null;
            var ranges = new List<(int a, int b)>();
            if (f.time is not { Count: >= 2 } || f.time[0] == f.time[1]) ranges.Add((0, 1440));
            else if (f.time[0] < f.time[1]) ranges.Add((f.time[0], f.time[1]));
            else { ranges.Add((f.time[0], 1440)); ranges.Add((0, f.time[1])); }

            double realMsPerEtMin = 60000 / Ratio;
            double periodStart = (Math.Floor(nowMs / PeriodMs) - 9) * PeriodMs;
            int prevW = needsWeather ? WeatherAt(rates!, periodStart - 1) : 0;
            (double start, double end)? cur = null;
            for (int i = 0; i < periods + 9; i++, periodStart += PeriodMs)
            {
                int w = needsWeather ? WeatherAt(rates!, periodStart) : 0;
                bool ok = (f.weathers.Count == 0 || f.weathers.Contains(w)) && (f.prevWeathers.Count == 0 || f.prevWeathers.Contains(prevW));
                prevW = w;
                if (!ok)
                {
                    if (cur is { } c0 && c0.end > nowMs) return cur;
                    cur = null;
                    continue;
                }
                double pStartEt = Math.Round(periodStart * Ratio / 60000 % 1440);
                var pieces = new List<(double s, double e)>();
                foreach (var (a, b) in ranges)
                {
                    double s = Math.Max(a, pStartEt), e = Math.Min(b, pStartEt + 480);
                    if (s < e) pieces.Add((periodStart + (s - pStartEt) * realMsPerEtMin, periodStart + (e - pStartEt) * realMsPerEtMin));
                }
                foreach (var (s, e) in pieces.OrderBy(x => x.s))
                {
                    if (cur is { } c && Math.Abs(s - c.end) < 1000) { cur = (c.start, e); continue; }
                    if (cur is { } c2 && c2.end > nowMs) return cur;
                    cur = (s, e);
                }
            }
            return cur is { } last && last.end > nowMs ? cur : null;
        }

        private static double NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Every 5 s: windows for new fish and fish whose window ended; alarms when a window opens.</summary>
        private void TickFish()
        {
            if (!WantFish) return;
            EnsureFish();
            if (_fish == null || (DateTime.Now - _winsAt).TotalSeconds < 5) return;
            _winsAt = DateTime.Now;
            double now = NowMs;
            foreach (var f in _fish.fish)
            {
                if (_wins.TryGetValue(f.id, out var w) && (w == null || w.Value.end > now)) continue;
                _wins[f.id] = NextFishWindow(f, f.territory is { } t ? _fish.weatherRates.GetValueOrDefault(t.ToString()) : null, now);
            }
            foreach (var f in _fish.fish)
            {
                if (!HasAlarm(f.id.ToString()) || _wins.GetValueOrDefault(f.id) is not { } win) continue;
                if (now < win.start || now >= win.end || now - win.start > 20000) continue;
                if (!_fired.Add($"{f.id}@{win.start}")) continue;
                Ring($"alarm:fish:{f.id}", $"{f.name} can be caught now", f.spot ?? f.zone ?? "", $"Fishing alarm: {f.name} can be caught now{(f.spot != null ? $" at {f.spot}" : "")}.", true);
            }
        }

        private int FishState(FishEntry f, double now) => _wins.GetValueOrDefault(f.id) is { } w ? (now >= w.start ? 0 : 1) : 2;

        private List<FishEntry> FishList()
        {
            double now = NowMs;
            string key = $"{_fishQuery}|{_fishF.hideCaught}|{_fishF.big}|{_fishF.exp}|{_fishF.upOnly}|{_fishF.sort}|{(long)(now / 5000)}|{_fish?.fish.Count}";
            if (key == _fishListKey) return _fishList;
            _fishListKey = key;
            var q = _fishQuery.Trim().ToLowerInvariant();
            var list = (_fish?.fish ?? new()).Where(x =>
            {
                if (_fishF.hideCaught && IsFishCaught((uint)x.id) == true) return false;
                if (_fishF.big && !x.big) return false;
                if (_fishF.exp != "All" && ((int)Math.Floor(x.patch)).ToString() != _fishF.exp) return false;
                if (q.Length > 0 && !new[] { x.name, x.bait, x.spot, x.zone }.Concat(x.mooch).Any(s => s != null && s.ToLowerInvariant().Contains(q))) return false;
                return true;
            });
            if (_fishF.upOnly) list = list.Where(x => FishState(x, now) == 0);
            _fishList = _fishF.sort switch
            {
                "az" => list.OrderBy(x => x.name).ToList(),
                "patch" => list.OrderByDescending(x => x.patch).ThenBy(x => x.name).ToList(),
                _ => list.OrderBy(x => FishState(x, now))
                    .ThenBy(x => _wins.GetValueOrDefault(x.id) is { } w ? (FishState(x, now) == 0 ? w.end : w.start) : 0)
                    .ThenBy(x => x.name).ToList(),
            };
            return _fishList;
        }

        // =============================================================================================================
        // Drawing
        // =============================================================================================================
        private void DrawFishing()
        {
            EnsureFish();
            var avail = ImGui.GetContentRegionAvail();
            if (UIHelper.BeginSmoothChild("ga_fish", avail))
            {
                var p = ImGui.GetCursorScreenPos();
                float w = avail.X - S(10), y = DrawFishFilters(p, w) + S(12);
                if (_fish == null)
                {
                    if (_fishError.Length > 0) { Message(new Vector2(p.X, y), w, _fishError, Theme.Bad); y += LH + S(20); }
                    else { Empty(new Vector2(p.X, y), w, _sender.HasToken ? "Loading the fish list..." : "Sign in (Settings > Account) to load the fish list.", FontAwesomeIcon.Sync); y += EmptyH; }
                }
                else
                {
                    var list = FishList();
                    double now = NowMs;
                    if (list.Count == 0) { Empty(new Vector2(p.X, y), w, "No fish match your filters.", FontAwesomeIcon.Fish); y += EmptyH; }
                    foreach (var f in list)
                    {
                        float h = FishRowH + (_openFish == f.id ? FishDetailH(f, w) : 0);
                        if (Visible(y, h)) FishRow(new Vector2(p.X, y), w, f, now);
                        y += h + S(6);
                    }
                    y += S(6);
                    Ui.Text(Ui.Dl, new Vector2(p.X, y), Fit("Windows from the game's weather forecast and Eorzea time. Ocean fishing fish show their voyage instead.", w, 0.8f), Theme.Text3, 0.8f);
                    y += LH;
                }
                Advance(p, y - p.Y);
            }
            ImGui.EndChild();
        }

        private float DrawFishFilters(Vector2 p, float w)
        {
            var dl = Ui.Dl;
            float pad = S(12), inner = w - pad * 2, ch = ChipH();
            double now = NowMs;
            int upCount = _fish?.fish.Count(x => FishState(x, now) == 0) ?? 0;
            int caught = _fish?.fish.Count(x => IsFishCaught((uint)x.id) == true) ?? 0;
            bool canTell = _clientState.IsLoggedIn;
            float h = pad + FrameH + S(10) + ch + pad;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.6f));

            float comboW = Math.Min(S(190), inner * 0.25f);
            float searchW = Math.Max(S(120), inner - comboW * 2 - S(16));
            string before = _fishQuery;
            Input("##ga_fq", ref _fishQuery, "Fish, bait, spot or zone...", searchW, p + new Vector2(pad, pad));
            if (Combo("##ga_fsort", _fishF.sort, FishSorts, comboW, p + new Vector2(pad + searchW + S(8), pad), "Sort") is { } s) { _fishF.sort = s; WriteFish(); }
            if (Combo("##ga_fexp", _fishF.exp, FishExps, comboW, p + new Vector2(pad + searchW + comboW + S(16), pad), "Patch") is { } e) { _fishF.exp = e; WriteFish(); }

            var at = p + new Vector2(pad, pad + FrameH + S(10));
            float right = p.X + w - pad;
            if (ChipToggle("ga_f_up", $"Up now ({upCount})", _fishF.upOnly, ref at, p.X + pad, right, 0.85f, "Only fish with a time or weather window that is open right now")) { _fishF.upOnly = !_fishF.upOnly; WriteFish(); }
            if (ChipToggle("ga_f_big", "Big fish", _fishF.big, ref at, p.X + pad, right, 0.85f, "Only big fish")) { _fishF.big = !_fishF.big; WriteFish(); }
            if (canTell && ChipToggle("ga_f_hide", "Hide caught", _fishF.hideCaught, ref at, p.X + pad, right, 0.85f, "Hide fish you have caught")) { _fishF.hideCaught = !_fishF.hideCaught; WriteFish(); }
            if (_fish != null)
            {
                string sum = canTell ? $"{caught:N0} of {_fish.fish.Count:N0} caught" : $"{_fish.fish.Count:N0} fish";
                var ss = Ui.TextSize(sum, 0.82f);
                if (at.X + ss.X + S(10) < right) Ui.Text(dl, new Vector2(right - ss.X, at.Y + (ch - ss.Y) / 2), sum, canTell ? Theme.Live : Theme.Text3, 0.82f);
            }
            return p.Y + h;
        }

        private static float FishRowH => S(9) * 2 + LH + S(4) + Ui.TextSize("Ag", 0.85f).Y;

        private (int map, double x, double y, uint aeth) FishPlace(FishEntry f)
        {
            if (f.territory is not { } t || t <= 0) return (0, 0, 0, 0);
            if (_fishPlace.TryGetValue(f.id, out var hit)) return hit;
            (int, double, double, uint) r = (0, 0, 0, 0);
            try
            {
                var terr = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>().GetRowOrDefault((uint)t);
                if (terr != null && terr.Value.Map.RowId != 0)
                {
                    int map = (int)terr.Value.Map.RowId;
                    double x = 0, y = 0;
                    if (!string.IsNullOrEmpty(f.spot))
                    {
                        var spot = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>()
                            .FirstOrDefault(s => s.TerritoryType.RowId == (uint)t && string.Equals(s.PlaceName.ValueNullable?.Name.ToString(), f.spot, StringComparison.OrdinalIgnoreCase));
                        if (spot.RowId != 0 && terr.Value.Map.ValueNullable is { } m)
                        {
                            float c = m.SizeFactor / 100f;
                            double Conv(double v, int offset) => 41.0 / c * (((v + offset) * c + 1024.0) / 2048.0) + 1;
                            x = Conv(spot.X, m.OffsetX);
                            y = Conv(spot.Z, m.OffsetY);
                        }
                    }
                    r = (map, x, y, AetheryteFor(map, f.aetheryte));
                }
            }
            catch (Exception ex) { _log.Debug($"[Aetherhold] fish place: {ex.Message}"); }
            _fishPlace[f.id] = r;
            return r;
        }
        private readonly Dictionary<int, (int map, double x, double y, uint aeth)> _fishPlace = new();

        private void FishRow(Vector2 p, float w, FishEntry f, double now)
        {
            var dl = Ui.Dl;
            float pad = S(9), icon = S(32), bs = S(28), rowH = FishRowH;
            bool open = _openFish == f.id;
            var win = _wins.GetValueOrDefault(f.id);
            bool up = win is { } wn && now >= wn.start;
            float total = rowH + (open ? FishDetailH(f, w) : 0);
            Ui.Panel(dl, p, p + new Vector2(w, total), up ? Theme.Live.With(0.05f) : Theme.Night2.With(0.55f), f.big ? Theme.GoldLine : up ? Theme.Live.With(0.3f) : Theme.CrystalLine, 6f);

            // right: timer + actions
            float right = p.X + w - pad, by = p.Y + (rowH - bs) / 2;
            string label = "Always";
            float frac = 0;
            if (f.ocean != null) label = "Ocean fishing";
            else if (win is { } wv)
            {
                label = up ? $"{EorzeaTimeHelper.Format((wv.end - now) / 1000)} left" : $"in {EorzeaTimeHelper.Format((wv.start - now) / 1000)}";
                if (up) frac = (float)Math.Clamp((wv.end - now) / (wv.end - wv.start), 0, 1);
            }
            else if (f.Ruled) label = f.territory != null ? "No window in 9 days" : "No weather data";

            float bx = right - bs;
            if (Ui.IconButton("ga_fo" + f.id, open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight, new Vector2(bx, by), bs, open ? "Hide details" : "Details")) _openFish = open ? null : f.id;
            if (f.Ruled && f.ocean == null)
            {
                bx -= bs + S(4);
                bool al = HasAlarm(f.id.ToString());
                if (Ui.IconButton("ga_fa" + f.id, al ? FontAwesomeIcon.Bell : FontAwesomeIcon.BellSlash, new Vector2(bx, by), bs, al ? "Remove alarm" : "Set alarm: rings in game when the window opens", al ? Theme.Gold : null, al)) ToggleAlarm(f.id.ToString());
            }
            if (f.ocean == null)
            {
                var pl = FishPlace(f);
                bx -= PlaceButtons("ga_fp" + f.id, bx - S(4), by, bs, pl.map, pl.x, pl.y, $"{f.zone} · {f.spot}", pl.aeth) + S(4);
            }
            var ls = Ui.TextSize(label, 0.9f);
            float lx = bx - S(10) - Math.Max(ls.X, S(90));
            Ui.Text(dl, new Vector2(bx - S(10) - ls.X, p.Y + pad), label, up ? Theme.Live : Theme.Text2, 0.9f);
            if (up) Ui.Progress(dl, new Vector2(lx, p.Y + pad + ls.Y + S(4)), Math.Max(ls.X, S(90)), frac, Theme.Live, 3f);

            // left: caught mark, icon, name, tags, meta
            float x0 = p.X + pad;
            var c = IsFishCaught((uint)f.id);
            if (c.HasValue)
            {
                Ui.Icon(dl, new Vector2(x0, p.Y + (rowH - LH) / 2), c.Value ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, c.Value ? Theme.Live : Theme.Text3, 0.95f);
                ImGui.SetCursorScreenPos(new Vector2(x0, p.Y + pad));
                ImGui.InvisibleButton("ga_fc" + f.id, new Vector2(S(20), rowH - pad * 2));
                ImGui.SetItemAllowOverlap();
                Ui.Tooltip(c.Value ? "Caught" : "Not caught yet");
                x0 += S(24);
            }
            GameIcon(f.icon, new Vector2(x0, p.Y + (rowH - icon) / 2), icon);
            float tx = x0 + icon + S(10), tw = lx - S(10) - tx, ty = p.Y + pad;

            // the text area toggles the details
            ImGui.SetCursorScreenPos(new Vector2(tx, p.Y));
            if (ImGui.InvisibleButton("ga_ft" + f.id, new Vector2(Math.Max(1, tw), rowH))) _openFish = open ? null : f.id;
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            string name = Fit(f.name, tw * 0.6f);
            Ui.Text(dl, new Vector2(tx, ty), name, f.big ? Theme.Gold : Theme.Text);
            float cx = tx + Ui.TextSize(name).X + S(8), tagY = ty + (LH - TagH()) / 2;
            void T(bool on, string text, Vector4 col) { if (on && cx < tx + tw - S(70)) cx += Tag(new Vector2(cx, tagY), text, col) + S(4); }
            T(f.big, "Big fish", ColLegendary);
            T(f.intuition is > 0, "Intuition", ColHidden);
            T(f.snagging, "Snagging", ColUnspoiled);
            T(f.collectable, "Collectable", ColEphemeral);
            string bait = f.bait != null ? f.bait + (f.mooch.Count > 0 ? " → " + string.Join(" → ", f.mooch) : "") : "";
            Meta(new Vector2(tx, ty + LH + S(4)), tw, new[] { f.ocean != null ? "Ocean fishing" : f.spot ?? f.zone ?? "Unknown spot", bait, f.patch > 0 ? $"Patch {f.patch:0.0#}" : "" }, Theme.Text2);

            if (open) DrawFishDetail(new Vector2(p.X + pad, p.Y + rowH), w - pad * 2, f, win, now);
        }

        private List<string> FishDetailLines(FishEntry f, (double start, double end)? win, double now)
        {
            var l = new List<string>();
            if (f.ocean != null) l.Add($"Ocean fishing: {string.Join(", ", f.ocean)}");
            else
            {
                string time = f.time is { Count: >= 2 } t ? $"Time {EtClock(t[0])}-{EtClock(t[1])} ET" : "Any time";
                l.Add(time + "   ·   Weather: " + WeatherText(f));
                if (win is { } w)
                {
                    bool up = now >= w.start;
                    var at = DateTimeOffset.FromUnixTimeMilliseconds((long)(up ? w.end : w.start)).ToLocalTime();
                    l.Add($"{(up ? "Open until" : "Next window")} {at:ddd HH:mm} (your time)");
                }
            }
            l.Add($"Bait: {f.bait ?? "Unknown bait"}{(f.mooch.Count > 0 ? " → " + string.Join(" → ", f.mooch) : "")} → {f.name}");
            var loc = new List<string> { (f.zone ?? "-") + (f.spot != null && f.spot != f.zone ? $" · {f.spot}" : "") };
            if (f.aetheryte != null) loc.Add("Aetheryte " + f.aetheryte);
            if (f.folklore != null) loc.Add(f.folklore);
            l.Add(string.Join("   ·   ", loc));
            var det = new List<string>();
            if (f.hookset != null) det.Add($"{f.hookset} hookset");
            if (f.tug != null || f.bite != null) det.Add($"Bite: {f.tug ?? f.bite}{(f.tug != null && f.bite != null && !string.Equals(f.bite, f.tug, StringComparison.OrdinalIgnoreCase) ? $" ({f.bite})" : "")}");
            if (f.fishEyes) det.Add("Fish Eyes works");
            if (f.intuition is > 0) det.Add($"Fisher's Intuition ({f.intuition} s)");
            if (det.Count > 0) l.Add(string.Join("   ·   ", det));
            if (f.predators.Count > 0) l.Add("Catch first: " + string.Join(", ", f.predators.Select(pr => $"{pr.count}× {pr.name}")));
            return l;
        }

        private float FishDetailH(FishEntry f, float w) => FishDetailLines(f, _wins.GetValueOrDefault(f.id), NowMs).Count * (LH * 0.9f + S(6)) + S(8) + S(30) + S(12);

        private void DrawFishDetail(Vector2 p, float w, FishEntry f, (double start, double end)? win, double now)
        {
            var dl = Ui.Dl;
            dl.AddLine(p, p + new Vector2(w, 0), Theme.CrystalLine.U32(), S(1));
            float y = p.Y + S(8);
            foreach (var line in FishDetailLines(f, win, now))
            {
                Ui.Text(dl, new Vector2(p.X, y), Fit(line, w, 0.9f), Theme.Text2, 0.9f);
                y += LH * 0.9f + S(6);
            }
            var at = new Vector2(p.X, y);
            if (OpenMarket != null && FlowButton("ga_fmk" + f.id, "Market", FontAwesomeIcon.ShoppingCart, Kind.Ghost, ref at, p.X, p.X + w, S(30))) OpenItemInMarket(f.id, f.name);
            if (f.ocean == null && f.territory is { } t && _fish?.weatherRates.GetValueOrDefault(t.ToString()) is { } rates && f.Ruled)
            {
                int cw = WeatherAt(rates, now);
                string nowText = $"Weather there now: {WeatherName(cw)}";
                uint wi = WeatherIcon(cw);
                float ix = at.X + S(4);
                if (wi != 0) { GameIcon(wi, new Vector2(ix, at.Y + S(3)), S(24)); ix += S(30); }
                Ui.Text(dl, new Vector2(ix, at.Y + (S(30) - LH * 0.9f) / 2), nowText, Theme.Text3, 0.9f);
            }
        }

        private static string EtClock(int mins) => $"{mins / 60 % 24:00}:{mins % 60:00}";

        private string WeatherText(FishEntry f)
        {
            string Names(List<int> ids) => string.Join(" / ", ids.Select(WeatherName));
            if (f.weathers.Count == 0 && f.prevWeathers.Count == 0) return "any";
            return (f.prevWeathers.Count > 0 ? Names(f.prevWeathers) + " → " : "") + (f.weathers.Count > 0 ? Names(f.weathers) : "any");
        }

        private string WeatherName(int id)
        {
            if (_fish?.weathers.GetValueOrDefault(id.ToString()) is { name.Length: > 0 } w) return w.name;
            return _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Weather>().GetRowOrDefault((uint)id)?.Name.ToString() ?? $"#{id}";
        }

        private uint WeatherIcon(int id)
        {
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Weather>().GetRowOrDefault((uint)id);
            return row != null && row.Value.Icon > 0 ? (uint)row.Value.Icon : IconId(_fish?.weathers.GetValueOrDefault(id.ToString())?.icon);
        }
    }
}
