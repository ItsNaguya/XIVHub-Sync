using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // ---- shapes shared with the website (/api/gathering/* and the gathering.* sections) ------------------------------
    public class GatheringNodeStats
    {
        public int? perception { get; set; }
        public int? gathering { get; set; }
        public int? stars { get; set; }
    }

    /// <summary>One timed node item from /api/gathering/nodes (id = "tc_{node}_{item}", the website's favourite/alarm key).</summary>
    public class GatheringNode
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string image { get; set; } = "";
        public string type { get; set; } = "";
        public string nodeType { get; set; } = "";
        public string expansion { get; set; } = "";
        public int level { get; set; }
        public string zone { get; set; } = "";
        public string area { get; set; } = "";
        public string coords { get; set; } = "";
        public double x { get; set; }
        public double y { get; set; }
        public int map { get; set; }
        public List<int> hours { get; set; } = new();
        /// <summary>ET hours the node stays up (2 or 4); 0 = by nodeType.</summary>
        public double duration { get; set; }
        public string slot { get; set; } = "";
        public bool hidden { get; set; }
        public string scrips { get; set; } = "";
        public string aetheryte { get; set; } = "";
        public string folklore { get; set; } = "";
        public List<string> otherItems { get; set; } = new();
        public GatheringNodeStats? stats { get; set; }
        [JsonProperty("itemId")] public int apiItemId { get; set; }

        [JsonIgnore] public uint ItemId { get; set; }
    }

    /// <summary>/data/nodes.json (every gathering point); Crafting reads it for locations.</summary>
    public class DataNode
    {
        public List<int> items { get; set; } = new();
        public List<int> hiddenItems { get; set; } = new();
        public bool legendary { get; set; }
        public bool ephemeral { get; set; }
        public List<int> time { get; set; } = new();
        public List<int> spawns { get; set; } = new();
        public int type { get; set; }
        public int zoneid { get; set; }
        public int map { get; set; }
        public float x { get; set; }
        public float y { get; set; }
    }

    /// <summary>gathering.route entry (website GatheringRouteItem).</summary>
    public class GatherRouteItem
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = "";
        [JsonProperty("icon")] public string icon { get; set; } = "";
        [JsonProperty("level")] public int level { get; set; }
        [JsonProperty("quantity", NullValueHandling = NullValueHandling.Ignore)] public int? quantity { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
        [JsonIgnore] public int Qty => Math.Max(1, quantity ?? 1);
    }

    /// <summary>gathering.lists entry.</summary>
    public class GatherList
    {
        [JsonProperty("id")] public string id { get; set; } = "";
        [JsonProperty("name")] public string name { get; set; } = "";
        [JsonProperty("items")] public List<GatherRouteItem> items { get; set; } = new();
    }

    /// <summary>gathering.timers (website defaults).</summary>
    public class TimerFilters
    {
        [JsonProperty("classes")] public List<string> classes { get; set; } = new() { "MIN", "BTN" };
        [JsonProperty("types")] public List<string> types { get; set; } = new() { "Legendary", "Unspoiled", "Ephemeral" };
        [JsonProperty("expansion")] public string expansion { get; set; } = "Dawntrail";
        [JsonProperty("favOnly")] public bool favOnly { get; set; }
        [JsonProperty("sort")] public string sort { get; set; } = "time";
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    /// <summary>gathering.log = { job, bracket, open: { [expansion]: bool } }.</summary>
    public class LogState
    {
        [JsonProperty("job")] public string job { get; set; } = "MIN";
        [JsonProperty("bracket")] public int bracket { get; set; }
        [JsonProperty("open")] public Dictionary<string, bool> open { get; set; } = new();
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    /// <summary>gathering.fishing (website defaults).</summary>
    public class FishFilters
    {
        [JsonProperty("hideCaught")] public bool hideCaught { get; set; }
        [JsonProperty("sort")] public string sort { get; set; } = "window";
        [JsonProperty("big")] public bool big { get; set; }
        [JsonProperty("upOnly")] public bool upOnly { get; set; }
        [JsonProperty("exp")] public string exp { get; set; } = "All";
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    public static class EorzeaTimeHelper
    {
        private const double EORZEA_MULTIPLIER = 144.0 / 7.0;

        public static DateTime GetEorzeaTime()
        {
            long localEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long eorzeaEpoch = (long)(localEpoch * EORZEA_MULTIPLIER);
            return DateTimeOffset.FromUnixTimeMilliseconds(eorzeaEpoch).UtcDateTime;
        }

        /// <summary>Eorzea ms since the epoch (the website's etMs).</summary>
        public static double EtMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * EORZEA_MULTIPLIER;

        public static double Duration(GatheringNode node) => node.duration > 0 ? node.duration : node.nodeType == "Ephemeral" ? 4 : 2;

        /// <summary>Website spawnState: up + real seconds left, or down + real seconds to the next spawn; null without hours.</summary>
        public static (bool Up, double Secs)? Spawn(IReadOnlyList<int>? hours, double durationH, double etMs)
        {
            if (hours == null || hours.Count == 0) return null;
            double now = etMs / 3_600_000 % 24, best = double.MaxValue;
            foreach (var h in hours)
            {
                double diff = now - h;
                if (diff < 0) diff += 24;
                if (diff < durationH) return (true, (durationH - diff) * 3600 / EORZEA_MULTIPLIER);
                double until = h - now;
                if (until <= 0) until += 24;
                best = Math.Min(best, until);
            }
            return (false, best * 3600 / EORZEA_MULTIPLIER);
        }

        public static bool IsNodeActive(GatheringNode node, int currentEorzeaHour)
        {
            if (node.hours == null || node.hours.Count == 0) return true;
            return Spawn(node.hours, Duration(node), EtMs())?.Up ?? true;
        }

        public static double GetActiveSecondsLeft(GatheringNode node, DateTime eTime)
            => node.hours == null || node.hours.Count == 0 ? 999999 : Spawn(node.hours, Duration(node), EtMs()) is { Up: true } s ? s.Secs : 0;

        public static double GetRealSecondsLeft(GatheringNode node, DateTime eTime)
            => Spawn(node.hours, Duration(node), EtMs()) is { Up: false } s ? s.Secs : 0;

        /// <summary>"12m 05s", "3h 20m", "2d 4h" (website formatSecs).</summary>
        public static string Format(double total)
        {
            long s = Math.Max(0, (long)Math.Floor(total));
            long d = s / 86400, h = s % 86400 / 3600, m = s % 3600 / 60;
            if (d > 0) return $"{d}d {h}h";
            if (h > 0) return $"{h}h {m:00}m";
            return $"{m}m {s % 60:00}s";
        }
    }

    /// <summary>
    /// Gathering &amp; Fishing on the tablet, the website's Gathering tool: node timers, gathering log, route and fishing.
    /// Everything the website saves lives in its gathering.* sections (read every frame, so both sides follow each
    /// other); the plugin adds what only the game knows: bag counts, caught fish, gatherer stats, map flags, teleports,
    /// a running route with its next stop, and alarms that ring in game.
    /// </summary>
    public partial class GatheringApp : IApp
    {
        public string Name => "Gathering & Fishing";
        public string Icon => ((char)FontAwesomeIcon.Leaf).ToString();
        public bool HasSettings => true;
        public bool LargeText => true;

        /// <summary>Opens an item in the Market app (set by PluginUI): id, name, icon id, can be HQ.</summary>
        public static Action<int, string, string, bool>? OpenMarket;

        public void DrawSettings()
        {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(Theme.Crystal, "Favourite nodes");
            ImGui.Dummy(new Vector2(0, 5));
            bool notif = _configuration.EnableNodeNotifications;
            if (UIHelper.DrawPremiumSwitchWithText("chk_notif", "Notify me when a favourite node spawns", ref notif))
            {
                _configuration.EnableNodeNotifications = notif;
                _configuration.Save();
            }
            if (notif)
            {
                ImGui.Indent(30f);
                int earlyMins = _configuration.EarlyNodeNotificationMinutes;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt("Early reminder (minutes)", ref earlyMins, 0, 3, earlyMins == 0 ? "On time" : "%d min"))
                {
                    _configuration.EarlyNodeNotificationMinutes = earlyMins;
                    _configuration.Save();
                }
                ImGui.Unindent(30f);
            }
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(Theme.Crystal, "Alarms");
            ImGui.TextWrapped("Bells set here or on the website ring in game when the node or fish window opens.");
            ImGui.Dummy(new Vector2(0, 5));
            bool audio = _configuration.EnableNodeAudio;
            if (UIHelper.DrawPremiumSwitchWithText("chk_notif_audio", "Play a sound (<se.1>)", ref audio))
            {
                _configuration.EnableNodeAudio = audio;
                _configuration.Save();
            }
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(Theme.Crystal, "Prices");
            bool prices = _configuration.EnableGatheringPrices;
            if (UIHelper.DrawPremiumSwitchWithText("chk_gather_prices", "Show market prices in the gathering log", ref prices))
            {
                _configuration.EnableGatheringPrices = prices;
                _configuration.Save();
            }
        }

        private const string SecView = "gathering.view", SecTimers = "gathering.timers", SecFavs = "gathering.favorites", SecAlarms = "gathering.alarms",
            SecRoute = "gathering.route", SecLists = "gathering.lists", SecLog = "gathering.log", SecFish = "gathering.fishing";
        private static readonly string[] Views = { "timers", "log", "route", "fishing" };

        private readonly HubClient _sender;
        private readonly IPluginLog _log;
        private readonly Configuration _configuration;
        private readonly IGameGui _gameGui;
        private readonly IChatGui _chatGui;
        private readonly IClientState _clientState;
        private readonly ITextureProvider _textureProvider;
        private readonly IDataManager _dataManager;
        private readonly IObjectTable _objectTable;
        private readonly ICondition _condition;

        public static List<GatheringNode> Nodes = new();
        public static Dictionary<string, DataNode> DataNodesMap = new();
        private Dictionary<string, GatheringNode> _nodeById = new();
        private Dictionary<string, List<string>> _legacy = new();

        public GatheringApp(
            HubClient sender,
            IPluginLog log,
            Configuration configuration,
            IGameGui gameGui,
            IChatGui chatGui,
            IClientState clientState,
            ITextureProvider textureProvider,
            IDataManager dataManager,
            Dalamud.Plugin.IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager,
            IObjectTable objectTable,
            ICondition condition)
        {
            _sender = sender;
            _log = log;
            _configuration = configuration;
            _gameGui = gameGui;
            _chatGui = chatGui;
            _clientState = clientState;
            _textureProvider = textureProvider;
            _dataManager = dataManager;
            _objectTable = objectTable;
            _condition = condition;
            LoadFishMapping();
        }

        public void Dispose()
        {
            if (OpenMarket != null) OpenMarket = null;
        }

        // =============================================================================================================
        // Public surface for the shell (Home widget, sleep view)
        // =============================================================================================================
        public int ActiveFavoriteCount { get; private set; }

        /// <summary>Favourite nodes with their state (home widget): up now first, then by real seconds until they spawn.</summary>
        public List<(GatheringNode Node, bool Up, double Seconds)> FavoriteTimers()
        {
            var list = new List<(GatheringNode, bool, double)>();
            double et = EorzeaTimeHelper.EtMs();
            foreach (var (id, on) in _favs)
            {
                if (!on || !_nodeById.TryGetValue(id, out var node)) continue;
                if (EorzeaTimeHelper.Spawn(node.hours, EorzeaTimeHelper.Duration(node), et) is not { } st) continue;
                list.Add((node, st.Up, st.Secs));
            }
            return list.OrderBy(x => x.Item2 ? 0 : 1).ThenBy(x => x.Item3).ToList();
        }

        public void Update()
        {
            SyncIn();
            if (Nodes.Count == 0 && !_nodesLoading && _sender.HasToken && (DateTime.Now - _nodesTried).TotalSeconds > 30) LoadNodes();
            MigrateOnce();
            if ((DateTime.Now - _lastTick).TotalSeconds >= 1)
            {
                _lastTick = DateTime.Now;
                _heldCache.Clear();
                TickFavoritesAndAlarms();
                TickRoute();
                TickFish();
            }
            OpenPendingMapLink();
        }
        private DateTime _lastTick = DateTime.MinValue;

        public void Draw()
        {
            UpdatePlayer();
            DrawHeader();
            switch (CurrentView)
            {
                case 1: DrawLog(); break;
                case 2: DrawRoute(); break;
                case 3: DrawFishing(); break;
                default: DrawTimers(); break;
            }
        }

        // =============================================================================================================
        // Sync with the website (re-read when HubClient swaps a section's token; own writes keep the new one)
        // =============================================================================================================
        private static readonly JsonSerializer ReadSer = JsonSerializer.Create(new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
        private JToken? _favTok, _alarmTok, _routeTok, _listsTok, _timersTok, _logTok, _fishTok;
        private Dictionary<string, bool> _favs = new(), _alarms = new();
        private List<GatherRouteItem> _route = new();
        private List<GatherList> _lists = new();
        private TimerFilters _timerF = new();
        private LogState _logState = new();
        private FishFilters _fishF = new();

        private static T? Read<T>(JToken? t) where T : class
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            try { return t.ToObject<T>(ReadSer); } catch { return null; }
        }

        private static Dictionary<string, bool> ReadFlags(JToken? t)
        {
            var d = new Dictionary<string, bool>();
            if (t is JObject o)
                foreach (var p in o.Properties())
                    if (p.Value.Type == JTokenType.Boolean) d[p.Name] = p.Value.Value<bool>();
            return d;
        }

        private void SyncIn()
        {
            var t = _sender.GetState(SecFavs);
            if (!ReferenceEquals(t, _favTok)) { _favTok = t; _favs = ReadFlags(t); }
            t = _sender.GetState(SecAlarms);
            if (!ReferenceEquals(t, _alarmTok)) { _alarmTok = t; _alarms = ReadFlags(t); }
            t = _sender.GetState(SecRoute);
            if (!ReferenceEquals(t, _routeTok)) { _routeTok = t; _route = Read<List<GatherRouteItem>>(t)?.Where(i => i != null && i.id > 0).ToList() ?? new(); _planAt = DateTime.MinValue; }
            t = _sender.GetState(SecLists);
            if (!ReferenceEquals(t, _listsTok)) { _listsTok = t; _lists = Read<List<GatherList>>(t)?.Where(l => l != null).ToList() ?? new(); }
            t = _sender.GetState(SecTimers);
            if (!ReferenceEquals(t, _timersTok)) { _timersTok = t; _timerF = Read<TimerFilters>(t) ?? new(); _timerF.classes ??= new(); _timerF.types ??= new(); }
            t = _sender.GetState(SecLog);
            if (!ReferenceEquals(t, _logTok)) { _logTok = t; _logState = Read<LogState>(t) ?? new(); _logState.open ??= new(); }
            t = _sender.GetState(SecFish);
            if (!ReferenceEquals(t, _fishTok)) { _fishTok = t; _fishF = Read<FishFilters>(t) ?? new(); }
        }

        private JToken? Write(string section, object value, JToken? old)
        {
            _sender.SetState(section, value);
            return _sender.GetState(section) ?? old;
        }

        private void WriteFavs() => _favTok = Write(SecFavs, _favs, _favTok);
        private void WriteAlarms() => _alarmTok = Write(SecAlarms, _alarms, _alarmTok);
        private void WriteRoute() { _routeTok = Write(SecRoute, _route, _routeTok); _planAt = DateTime.MinValue; }
        private void WriteLists() => _listsTok = Write(SecLists, _lists, _listsTok);
        private void WriteTimers() => _timersTok = Write(SecTimers, _timerF, _timersTok);
        private void WriteLog() => _logTok = Write(SecLog, _logState, _logTok);
        private void WriteFish() => _fishTok = Write(SecFish, _fishF, _fishTok);

        private int CurrentView
        {
            get
            {
                int i = Array.IndexOf(Views, (_sender.GetState(SecView) as JValue)?.Value as string);
                return i < 0 ? _localView : i;
            }
        }
        private int _localView;

        private void SetView(int i)
        {
            _localView = i;
            _sender.SetState(SecView, Views[i]);
        }

        private bool IsFav(string id) => _favs.TryGetValue(id, out var v) && v;
        private bool HasAlarm(string id) => _alarms.TryGetValue(id, out var v) && v;
        private void ToggleFav(string id) { _favs[id] = !IsFav(id); WriteFavs(); }
        private void ToggleAlarm(string id) { _alarms[id] = !HasAlarm(id); WriteAlarms(); }

        /// <summary>Website addToGatheringRoute: one line per item, the amount adds up.</summary>
        private void AddToRoute(int id, string name, string icon, int level, int qty = 1)
        {
            if (id <= 0) return;
            var existing = _route.FirstOrDefault(i => i.id == id);
            if (existing != null) existing.quantity = existing.Qty + qty;
            else _route.Add(new GatherRouteItem { id = id, name = name, icon = icon, level = level, quantity = qty });
            WriteRoute();
        }

        /// <summary>
        /// 2.1 kept favourites and the route in the plugin only. Once the website's state is loaded, they move there if
        /// the website has none yet (old node ids are mapped with the server's legacy table). The old config is kept.
        /// </summary>
        private bool _migratedFavs, _migratedRoute;
        private void MigrateOnce()
        {
            if (!_sender.StateLoaded) return;
            if (!_migratedFavs && Nodes.Count > 0)
            {
                _migratedFavs = true;
                var old = _configuration.GatheringFavorites ?? new();
                if (old.Count > 0 && !_favs.Values.Any(v => v))
                {
                    foreach (var id in old)
                    {
                        if (_nodeById.ContainsKey(id)) _favs[id] = true;
                        else if (_legacy.TryGetValue(id, out var ids)) foreach (var n in ids) _favs[n] = true;
                    }
                    if (_favs.Count > 0) WriteFavs();
                }
            }
            if (!_migratedRoute)
            {
                _migratedRoute = true;
                var old = _configuration.GatheringActiveRoute ?? new();
                if (old.Count > 0 && _sender.GetState(SecRoute) == null)
                {
                    var items = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                    foreach (var r in old.Where(r => !r.IsCompleted))
                    {
                        var row = items.GetRowOrDefault(r.ItemId);
                        if (row == null || _route.Any(x => x.id == (int)r.ItemId)) continue;
                        _route.Add(new GatherRouteItem { id = (int)r.ItemId, name = row.Value.Name.ToString(), icon = IconUrl(row.Value.Icon), level = 0, quantity = Math.Max(1, r.TargetQuantity) });
                    }
                    if (_route.Count > 0) WriteRoute();
                }
            }
        }

        // =============================================================================================================
        // Data
        // =============================================================================================================
        private bool _nodesLoading;
        private DateTime _nodesTried = DateTime.MinValue;

        private void LoadNodes()
        {
            _nodesLoading = true;
            _nodesTried = DateTime.Now;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.FetchGatheringNodesAsync();
                    if (!string.IsNullOrEmpty(json) && JToken.Parse(json) is JObject o && o["data"] is JArray arr)
                    {
                        var list = arr.ToObject<List<GatheringNode>>() ?? new();
                        foreach (var n in list) { n.ItemId = (uint)Math.Max(0, n.apiItemId); n.hours ??= new(); n.otherItems ??= new(); }
                        _legacy = o["legacy"]?.ToObject<Dictionary<string, List<string>>>() ?? new();
                        _nodeById = list.GroupBy(n => n.id).ToDictionary(g => g.Key, g => g.First());
                        Nodes = list;
                    }
                    if (DataNodesMap.Count == 0)
                    {
                        var dn = await _sender.FetchDataNodesAsync();
                        if (!string.IsNullOrEmpty(dn)) DataNodesMap = JsonConvert.DeserializeObject<Dictionary<string, DataNode>>(dn) ?? new();
                    }
                }
                catch (Exception ex) { _log.Error(ex, "Failed to load gathering nodes"); }
                finally { _nodesLoading = false; }
            });
        }

        // ---- bag counts (bags + crystals, NQ and HQ), cached for a second -------------------------------------------
        private readonly Dictionary<uint, int> _heldCache = new();

        private unsafe int Held(uint itemId)
        {
            if (itemId == 0) return 0;
            if (_heldCache.TryGetValue(itemId, out var c)) return c;
            int n = 0;
            try
            {
                var m = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
                if (m != null) n = m->GetInventoryItemCount(itemId, false, false, false, 0) + m->GetInventoryItemCount(itemId, true, false, false, 0);
            }
            catch { }
            _heldCache[itemId] = n;
            return n;
        }

        // ---- caught fish (the game's caught bits) ------------------------------------------------------------------
        private readonly Dictionary<uint, (bool spear, uint row)> _fishMapping = new();

        private void LoadFishMapping()
        {
            try
            {
                foreach (var f in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishParameter>())
                    if (f.Item.RowId != 0) _fishMapping[f.Item.RowId] = (false, f.RowId);
                foreach (var f in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingItem>())
                    if (f.Item.RowId != 0) _fishMapping[f.Item.RowId] = (true, f.RowId);
            }
            catch (Exception ex) { _log.Debug($"[Aetherhold] fish mapping: {ex.Message}"); }
        }

        /// <summary>Null when the game can't tell (not logged in).</summary>
        private unsafe bool? IsFishCaught(uint itemId)
        {
            if (!_fishMapping.TryGetValue(itemId, out var m)) return null;
            try
            {
                var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
                if (ps == null || !_clientState.IsLoggedIn) return null;
                return m.spear ? ps->IsSpearfishCaught(m.row) : ps->IsFishCaught(m.row);
            }
            catch { return null; }
        }

        // ---- player: gatherer stats for the header strip ----------------------------------------------------------
        private string _gatherer = "";
        private int _gLevel, _gGath, _gPerc, _gGp, _gMaxGp;

        private unsafe void UpdatePlayer()
        {
            _gatherer = "";
            try
            {
                if (_objectTable[0] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc) return;
                uint job = pc.ClassJob.RowId;
                if (job < 16 || job > 18) return;
                _gatherer = job == 16 ? "MIN" : job == 17 ? "BTN" : "FSH";
                _gLevel = pc.Level;
                _gGp = (int)pc.CurrentGp;
                _gMaxGp = (int)pc.MaxGp;
                var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
                if (ui != null) { _gGath = ui->PlayerState.Attributes[72]; _gPerc = ui->PlayerState.Attributes[73]; }
            }
            catch { }
        }

        // =============================================================================================================
        // Favourite notifications and alarms (ring in game whatever the tablet shows)
        // =============================================================================================================
        private readonly Dictionary<string, DateTime> _lastNotified = new();
        private readonly HashSet<string> _fired = new();

        private void TickFavoritesAndAlarms()
        {
            if (Nodes.Count == 0) return;
            double et = EorzeaTimeHelper.EtMs();
            int active = 0;
            foreach (var (id, on) in _favs)
            {
                if (!on || !_nodeById.TryGetValue(id, out var node)) continue;
                if (EorzeaTimeHelper.Spawn(node.hours, EorzeaTimeHelper.Duration(node), et) is not { } st) continue;
                if (st.Up) active++;
                if (!_configuration.EnableNodeNotifications || HasAlarm(id)) continue;
                double until = st.Up ? 0 : st.Secs;
                if (until > _configuration.EarlyNodeNotificationMinutes * 60) continue;
                if (_lastNotified.TryGetValue(id, out var at) && (DateTime.Now - at).TotalMinutes <= 20) continue;
                _lastNotified[id] = DateTime.Now;
                string when = st.Up ? "is up now" : $"spawns in {Math.Max(1, (int)Math.Ceiling(until / 60))} min";
                Ring($"node:{id}", $"{node.name} {when}", $"{node.zone} ({node.coords})", $"Your favourite node '{node.name}' {when} at {node.zone}!", false);
            }
            ActiveFavoriteCount = active;

            // alarms: once per spawn, when it opens (website rule)
            long nowS = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var (id, on) in _alarms)
            {
                if (!on || !_nodeById.TryGetValue(id, out var node)) continue;
                if (EorzeaTimeHelper.Spawn(node.hours, EorzeaTimeHelper.Duration(node), et) is not { Up: true } st) continue;
                double openedAgo = EorzeaTimeHelper.Duration(node) * 175 - st.Secs;
                string key = $"{id}@{Math.Round((nowS + st.Secs) / 60)}";
                if (openedAgo >= 20 || !_fired.Add(key)) continue;
                Ring($"alarm:{id}", $"{node.name} is up", $"{node.zone} ({node.coords})", $"Gathering alarm: {node.name} is up in {node.zone} ({node.coords}).", true);
            }
        }

        private void Ring(string key, string title, string body, string chat, bool alarm)
        {
            Notifications.Post(key, alarm ? "Alarm" : "Gathering", title, body, Name);
            try { _chatGui.Print(new Dalamud.Game.Text.XivChatEntry { Message = $"[Aetherhold] {chat}", Type = Dalamud.Game.Text.XivChatType.Echo }); } catch { }
            if (!_configuration.EnableNodeAudio) return;
            try { unsafe { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(1); } } catch { }
        }

        // =============================================================================================================
        // Map flags and teleports
        // =============================================================================================================
        private uint _pendingMapTerritory, _pendingMap;
        private float _pendingX, _pendingY;

        /// <summary>Puts a flag at map coordinates (X/Y as the game shows them) on the map's own territory.</summary>
        private void OpenMapAt(int mapId, double x, double y)
        {
            try
            {
                var mapRow = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault((uint)mapId);
                if (mapRow == null || mapRow.Value.TerritoryType.RowId == 0) return;
                var terr = mapRow.Value.TerritoryType.Value;
                uint main = terr.Map.RowId != 0 ? terr.Map.RowId : mapRow.Value.RowId;
                _gameGui.OpenMapWithMapLink(new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(mapRow.Value.TerritoryType.RowId, main, (float)x, (float)y));
            }
            catch (Exception ex) { _log.Error(ex, $"Map link failed for map {mapId}"); }
        }

        /// <summary>Opens the flag once the player is out of a loading screen (after a teleport).</summary>
        private void QueueMapAt(int mapId, double x, double y)
        {
            var mapRow = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault((uint)mapId);
            if (mapRow == null) return;
            _pendingMapTerritory = mapRow.Value.TerritoryType.RowId;
            _pendingMap = (uint)mapId;
            _pendingX = (float)x; _pendingY = (float)y;
        }

        private void OpenPendingMapLink()
        {
            if (_pendingMap == 0 || _condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas]) return;
            if (_clientState.TerritoryType != _pendingMapTerritory) return;
            var m = (int)_pendingMap;
            _pendingMap = 0;
            OpenMapAt(m, _pendingX, _pendingY);
        }

        private uint TerritoryOfMap(int mapId)
            => mapId > 0 ? _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault((uint)mapId)?.TerritoryType.RowId ?? 0 : 0;

        private readonly Dictionary<string, uint> _aethCache = new();

        /// <summary>The aetheryte named <paramref name="name"/> in the map's territory, else the territory's first one.</summary>
        private uint AetheryteFor(int mapId, string? name)
        {
            string key = mapId + "|" + name;
            if (_aethCache.TryGetValue(key, out var hit)) return hit;
            uint id = 0;
            try
            {
                uint terr = TerritoryOfMap(mapId);
                if (terr != 0)
                {
                    var all = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().Where(a => a.IsAetheryte && a.Territory.RowId == terr).ToList();
                    if (!string.IsNullOrEmpty(name))
                        id = all.FirstOrDefault(a => string.Equals(a.PlaceName.ValueNullable?.Name.ToString(), name, StringComparison.OrdinalIgnoreCase)).RowId;
                    if (id == 0) id = GetAetheryteIdForMap(mapId);
                }
            }
            catch (Exception ex) { _log.Debug($"[Aetherhold] aetheryte lookup: {ex.Message}"); }
            _aethCache[key] = id;
            return id;
        }

        /// <summary>First real aetheryte of the map's territory (not the "closest" placeholder rows).</summary>
        private uint GetAetheryteIdForMap(int mapId)
        {
            try
            {
                uint terr = TerritoryOfMap(mapId);
                if (terr == 0) return 0;
                foreach (var a in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>())
                {
                    if (!a.IsAetheryte || a.Territory.RowId != terr) continue;
                    var name = a.PlaceName.ValueNullable?.Name.ToString() ?? "";
                    if (name.Length == 0 || name.Contains("Closest") || name.Contains("Nächster") || name.Contains("plus proche") || name.Contains("最寄り") || name == "Ätheryt") continue;
                    return a.RowId;
                }
            }
            catch (Exception ex) { _log.Error(ex, "Failed to get Aetheryte for map " + mapId); }
            return 0;
        }

        private string AetheryteName(uint id)
            => id == 0 ? "" : _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRowOrDefault(id)?.PlaceName.ValueNullable?.Name.ToString() ?? "";

        public static unsafe void TeleportToAetheryte(uint aetheryteId)
        {
            try
            {
                var telepo = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
                if (telepo != null && aetheryteId != 0) telepo->Teleport(aetheryteId, 0);
            }
            catch { }
        }

        /// <summary>Map pin + teleport buttons (right-aligned ending at <paramref name="right"/>); returns their width.</summary>
        private float PlaceButtons(string id, float right, float y, float size, int mapId, double x, double yy, string where, uint aeth, bool flagAfterTeleport = true)
        {
            float w = 0;
            if (aeth != 0)
            {
                w += size;
                if (Ui.IconButton(id + "_tp", FontAwesomeIcon.PlaneDeparture, new Vector2(right - w, y), size, $"Teleport to {AetheryteName(aeth)}", Theme.Gold))
                {
                    TeleportToAetheryte(aeth);
                    if (flagAfterTeleport && mapId > 0 && x > 0) QueueMapAt(mapId, x, yy);
                }
                w += S(4);
            }
            if (mapId > 0 && x > 0)
            {
                w += size;
                if (Ui.IconButton(id + "_map", FontAwesomeIcon.MapMarkerAlt, new Vector2(right - w, y), size, $"Show on the map: {where}")) OpenMapAt(mapId, x, yy);
                w += S(4);
            }
            return w;
        }

        private bool MarketButton(string id, Vector2 pos, float size, int itemId, string name)
        {
            if (OpenMarket == null || itemId <= 0) return false;
            if (!Ui.IconButton(id, FontAwesomeIcon.ShoppingCart, pos, size, "Open in Market")) return false;
            OpenItemInMarket(itemId, name);
            return true;
        }

        private void OpenItemInMarket(int itemId, string name)
        {
            if (OpenMarket == null || itemId <= 0) return;
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().GetRowOrDefault((uint)itemId);
            OpenMarket(itemId, row?.Name.ToString() ?? name, row?.Icon.ToString() ?? "", row?.CanBeHq ?? false);
        }

        // =============================================================================================================
        // Header: panels + Eorzea time and gatherer stats
        // =============================================================================================================
        private void DrawHeader()
        {
            var p = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X, h = S(34), gap = S(8);
            string strip = StripText();
            float stripW = Ui.TextSize(strip, 0.9f).X + S(24);
            bool oneRow = w - stripW - gap >= S(560);
            float tabsW = oneRow ? w - stripW - gap : w;
            float bw = (tabsW - gap * 3) / 4;
            int view = CurrentView;
            string[] labels = { "Node timers", "Gathering log", _route.Count > 0 ? $"Route ({_route.Count})" : "Route", "Fishing" };
            FontAwesomeIcon[] icons = { FontAwesomeIcon.Clock, FontAwesomeIcon.Book, FontAwesomeIcon.Route, FontAwesomeIcon.Fish };
            bool showIcons = bw >= S(150);
            for (int i = 0; i < 4; i++)
                if (Ui.Button("ga_tab" + i, labels[i], p + new Vector2(i * (bw + gap), 0), new Vector2(bw, h), i == view ? Kind.Primary : Kind.Ghost, showIcons ? icons[i] : null) && i != view)
                    SetView(i);

            var sp = oneRow ? p + new Vector2(w - stripW, 0) : p + new Vector2(0, h + S(8));
            var dl = Ui.Dl;
            Ui.Panel(dl, sp, sp + new Vector2(stripW, h), Theme.Night2.With(0.6f), Theme.GoldLine, 6f);
            Ui.Text(dl, sp + new Vector2(S(12), (h - Ui.TextSize(strip, 0.9f).Y) / 2), strip, Theme.Text, 0.9f);
            ImGui.SetCursorScreenPos(sp);
            ImGui.InvisibleButton("ga_strip", new Vector2(stripW, h));
            if (_gatherer.Length > 0) Ui.Tooltip($"{_gatherer} Lv {_gLevel}: gathering, perception and GP from the game");
            else Ui.Tooltip("Eorzea time");
            Advance(p, (oneRow ? h : h * 2 + S(8)) + S(10));
        }

        private string StripText()
        {
            var et = EorzeaTimeHelper.GetEorzeaTime();
            string s = $"ET {et:HH:mm}";
            if (_gatherer.Length > 0)
                s += _gatherer == "FSH" ? $"   ·   FSH {_gLevel}   GP {_gGp}/{_gMaxGp}" : $"   ·   {_gatherer} {_gLevel}   Gath {_gGath}   Perc {_gPerc}   GP {_gGp}/{_gMaxGp}";
            return s;
        }
    }
}
