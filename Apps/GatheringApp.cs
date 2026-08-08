using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using System.Text.RegularExpressions;

namespace XIVHubCompanion.Apps
{
    public class GatheringNodeStats
    {
        public int? perception { get; set; }
        public int? gathering { get; set; }
        public int? stars { get; set; }
    }

    public class GatheringNode
    {
        public string id { get; set; }
        public string name { get; set; }
        public string image { get; set; }
        public string type { get; set; }
        public string nodeType { get; set; }
        public string expansion { get; set; }
        public int level { get; set; }
        public string zone { get; set; }
        public string coords { get; set; }
        public List<int> hours { get; set; } = new();
        public string slot { get; set; }
        public string scrips { get; set; }
        public string aetheryte { get; set; }
        public string folklore { get; set; }
        public GatheringNodeStats stats { get; set; }

        [JsonIgnore]
        public uint ItemId { get; set; }
        [JsonIgnore]
        public ISharedImmediateTexture Texture { get; set; }
        [JsonIgnore]
        public uint IconId { get; set; }
    }

    public class AquaticNode
    {
        public string name { get; set; }
        public string fishType { get; set; }
        public string bestSpot { get; set; }
        public string bestZone { get; set; }
        public int? bait { get; set; }
        public string hookset { get; set; }
        public string biteType { get; set; }
        public string tug { get; set; }
        public double? patch { get; set; }
        public bool collectable { get; set; }
        public string folklore { get; set; }
        
        [Newtonsoft.Json.JsonProperty("time")]
        public List<double> _timeRaw { get; set; }
        
        [Newtonsoft.Json.JsonIgnore]
        public List<int> time { 
            get { return _timeRaw?.Select(x => (int)x).ToList(); } 
            set { _timeRaw = value?.Select(x => (double)x).ToList(); } 
        }
        
        public List<int> weathers { get; set; }
        public List<int> previousWeathers { get; set; }
        public List<int> mooch { get; set; }
        public List<int> moochPath { get; set; }
        public bool? snagging { get; set; }
        public bool? fishEyes { get; set; }
        public int? intuitionLength { get; set; }
        public bool? bigFish { get; set; }

        [JsonIgnore]
        public uint ItemId { get; set; }
        [JsonIgnore]
        public ISharedImmediateTexture Texture { get; set; }
        [JsonIgnore]
        public string BaitName { get; set; }
        [JsonIgnore]
        public List<string> MoochNames { get; set; } = new();
    }

    public class GatheringLogItem
    {
        public uint itemId { get; set; }
        public int ilvl { get; set; }
        public int lvl { get; set; }
        public int stars { get; set; }
        public int hidden { get; set; }
        
        [JsonIgnore]
        public string Name { get; set; }
        [JsonIgnore]
        public ISharedImmediateTexture Texture { get; set; }
    }

    public class GatheringLogBracket
    {
        public int id { get; set; }
        public int startLevel { get; set; }
        public List<GatheringLogItem> items { get; set; } = new();
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

        public static bool IsNodeActive(GatheringNode node, int currentEorzeaHour)
        {
            if (node.hours == null || node.hours.Count == 0) return true;
            int duration = node.nodeType == "Ephemeral" ? 4 : 2;
            foreach (var h in node.hours)
            {
                int diff = currentEorzeaHour - h;
                if (diff < 0) diff += 24;
                if (diff >= 0 && diff < duration) return true;
            }
            return false;
        }

        public static double GetActiveSecondsLeft(GatheringNode node, DateTime eTime)
        {
            if (node.hours == null || node.hours.Count == 0) return 999999;
            double currentEtHourFloat = eTime.TimeOfDay.TotalHours;
            int duration = node.nodeType == "Ephemeral" ? 4 : 2;
            foreach (var h in node.hours)
            {
                double diff = currentEtHourFloat - h;
                if (diff < 0) diff += 24.0;
                if (diff >= 0 && diff < duration)
                {
                    double remainingEtHours = duration - diff;
                    return (remainingEtHours * 3600.0) / EORZEA_MULTIPLIER;
                }
            }
            return 0;
        }

        public static double GetRealSecondsLeft(GatheringNode node, DateTime eTime)
        {
            if (node.hours == null || node.hours.Count == 0) return 0;
            double currentEtHourFloat = eTime.TimeOfDay.TotalHours;
            var sortedHours = new List<int>(node.hours);
            sortedHours.Sort();
            
            int? nextH = null;
            foreach (var h in sortedHours)
            {
                if (h > currentEtHourFloat)
                {
                    nextH = h;
                    break;
                }
            }
            
            if (nextH == null) nextH = sortedHours[0];
            
            double diff = nextH.Value - currentEtHourFloat;
            if (diff < 0) diff += 24.0;
            
            return (diff * 3600.0) / EORZEA_MULTIPLIER;
        }
        public static int GetNextSpawnMinutes(DateTime currentET, List<int> spawnHours)
        {
            if (spawnHours == null || spawnHours.Count == 0) return 0;
            int currentHour = currentET.Hour;
            int currentMinute = currentET.Minute;
            
            int? nextHour = null;
            var sortedHours = spawnHours.OrderBy(x => x).ToList();
            foreach (var h in sortedHours)
            {
                if (h > currentHour || (h == currentHour && currentMinute == 0))
                {
                    nextHour = h;
                    break;
                }
            }
            
            int daysAdded = 0;
            if (nextHour == null)
            {
                nextHour = sortedHours[0];
                daysAdded = 1;
            }
            
            int totalETMinutesNow = currentHour * 60 + currentMinute;
            int totalETMinutesNext = (daysAdded * 24 * 60) + (nextHour.Value * 60);
            
            return totalETMinutesNext - totalETMinutesNow;
        }
    }

    public class GatheringApp : IApp
    {
        public string Name => "Gathering & Fishing";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.Leaf).ToString();
        public bool HasSettings => true;
        public void DrawSettings() 
        {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), "Notifications");
            ImGui.Dummy(new Vector2(0, 5));
            
            bool notif = _configuration.EnableNodeNotifications;
            if (UIHelper.DrawPremiumSwitchWithText("chk_notif", "Enable Node Chat Notifications", ref notif))
            {
                _configuration.EnableNodeNotifications = notif;
                _configuration.Save();
            }
            if (notif)
            {
                ImGui.Indent(30f);
                
                bool audio = _configuration.EnableNodeAudio;
                if (UIHelper.DrawPremiumSwitchWithText("chk_notif_audio", "Play Audio Alert (<se.1>)", ref audio))
                {
                    _configuration.EnableNodeAudio = audio;
                    _configuration.Save();
                }
                
                ImGui.Dummy(new Vector2(0, 5));
                
                int earlyMins = _configuration.EarlyNodeNotificationMinutes;
                ImGui.SetNextItemWidth(150f);
                if (ImGui.SliderInt("Early Reminder (Minutes)", ref earlyMins, 0, 3, earlyMins == 0 ? "On Time" : "%d min"))
                {
                    _configuration.EarlyNodeNotificationMinutes = earlyMins;
                    _configuration.Save();
                }
                ImGui.Unindent(30f);
            }
        }

        private Dictionary<uint, (DateTime start, DateTime end)?> _fishUptimesCache = new();
        private DateTime _lastFishUptimeUpdate = DateTime.MinValue;

        private void UpdateFishUptimesCache()
        {
            if ((DateTime.Now - _lastFishUptimeUpdate).TotalSeconds < 30) return;
            
            var territories = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (territories == null) return;
            
            DateTime now = DateTime.UtcNow;

            foreach (var f in AquaticNodes)
            {
                if ((f.time == null || f.time.Count == 0) && (f.weathers == null || f.weathers.Count == 0))
                {
                    _fishUptimesCache[f.ItemId] = null;
                    continue;
                }

                if (string.IsNullOrEmpty(f.bestZone)) continue;
                var cleanZone = f.bestZone.Replace("'", "").Replace(" ", "");
                
                Lumina.Excel.Sheets.TerritoryType? terr = null;
                foreach (var territory in territories)
                {
                    if (territory.Map.RowId == 0) continue;
                    var placeName = territory.PlaceName.Value;
                    var cleanPlace = placeName.Name.ToString().Replace("'", "").Replace(" ", "");
                    
                    if (cleanPlace.Contains(cleanZone, StringComparison.OrdinalIgnoreCase))
                    {
                        terr = territory;
                        break;
                    }
                }

                if (terr.HasValue)
                {
                    _fishUptimesCache[f.ItemId] = WeatherPredictor.GetNextUptime(terr.Value, f.time, f.weathers, null, now, 14);
                }
            }
            
            _lastFishUptimeUpdate = DateTime.Now;
        }

        private readonly DataSender _sender;
        private readonly IPluginLog _log;
        private readonly Configuration _configuration;
        private readonly IGameGui _gameGui;
        private readonly Dalamud.Plugin.Services.IChatGui _chatGui;
        private readonly IClientState _clientState;
        private readonly Dalamud.Plugin.Services.ICommandManager _commandManager;
                private readonly Dalamud.Plugin.Services.IObjectTable _objectTable;
        private readonly Dalamud.Plugin.Services.ICondition _condition;
        private bool _shouldOpenMapLink = false;
        private uint _mapLinkTargetItemId = 0;
        private bool _showIntegratedZoneGuidance = false;
        private bool _isStartRouteGuidance = false;
        private readonly ITextureProvider _textureProvider;
        private readonly IDataManager _dataManager;
        private readonly Dalamud.Plugin.IDalamudPluginInterface _pluginInterface;

        public static List<GatheringNode> Nodes = new();
        private List<List<GatheringLogBracket>> _logPages = new();
        private bool _isDataLoaded = false;
        private bool _isLoading = false;

        public List<AquaticNode> AquaticNodes { get; private set; } = new();
        private Dictionary<uint, (bool isSpear, uint rowId)> _fishMapping = new();
        
        // Filters
        private HashSet<string> _filterClass = new() { "MIN", "BTN" };
        private HashSet<string> _filterType = new() { "Legendary", "Unspoiled", "Ephemeral" };
        private string _filterExpansion = "Dawntrail";
        private string _searchQuery = "";
        private bool _showFavoritesOnly = false;
        private HashSet<string> _favorites = new();
        
        private string _viewMode = "timed";
        
        // Item Search State
        private int _searchSelectedTab = 0; // 0 = MIN, 1 = BTN
        private int _searchSelectedBracket = 0; // Index in unified brackets
        private class UnifiedBracket
        {
            public int StartLevel { get; set; }
            public List<GatheringLogItem> Category1 { get; set; } = new();
            public List<GatheringLogItem> Category2 { get; set; } = new();
        }
        private List<UnifiedBracket> _currentBrackets = new();
        
        private class ExpansionBracketGroup
        {
            public string Name { get; set; }
            public List<UnifiedBracket> Brackets { get; set; } = new();
        }
        private List<ExpansionBracketGroup> _expansionGroups = new();

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
        public static Dictionary<string, DataNode> DataNodesMap = new();

        // Gathering Route State
        private uint _itemToAddRoute = 0;
        private int _routeTargetQuantity = 100;

        public GatheringApp(
            DataSender sender, 
            IPluginLog log, 
            Configuration configuration, 
            IGameGui gameGui, 
            Dalamud.Plugin.Services.IChatGui chatGui,
            IClientState clientState, 
            ITextureProvider textureProvider, 
            IDataManager dataManager,
            Dalamud.Plugin.IDalamudPluginInterface pluginInterface,
            Dalamud.Plugin.Services.ICommandManager commandManager,
            Dalamud.Plugin.Services.IObjectTable objectTable,
            Dalamud.Plugin.Services.ICondition condition)
        {
            _sender = sender;
            _log = log;
            _configuration = configuration;
            _gameGui = gameGui;
            _chatGui = chatGui;
            _clientState = clientState;
            _commandManager = commandManager;
            _objectTable = objectTable;
            _condition = condition;
            _textureProvider = textureProvider;
            _dataManager = dataManager;
            _pluginInterface = pluginInterface;

            if (_configuration.GatheringFavorites != null)
            {
                foreach (var fav in _configuration.GatheringFavorites)
                {
                    _favorites.Add(fav);
                }
            }
            
            _ = LoadDataAsync();
        }

        public int ActiveFavoriteCount { get; private set; } = 0;
        private Dictionary<string, DateTime> _lastNotified = new();

        public void Update()
        {
            if (Nodes == null || Nodes.Count == 0) return;

            DateTime eTime = EorzeaTimeHelper.GetEorzeaTime();
            int currentHour = eTime.Hour;

            int activeCount = 0;

            foreach (var favId in _favorites)
            {
                var node = Nodes.FirstOrDefault(n => n.id == favId);
                if (node == null) continue;

                bool isUpNow = EorzeaTimeHelper.IsNodeActive(node, currentHour);
                if (isUpNow) activeCount++;

                if (_configuration.EnableNodeNotifications)
                {
                    double realSecs = isUpNow ? 0 : EorzeaTimeHelper.GetRealSecondsLeft(node, eTime);
                    int targetSecs = _configuration.EarlyNodeNotificationMinutes * 60;
                    
                    if (realSecs <= targetSecs && realSecs >= 0)
                    {
                        // Check if we already notified for this cycle
                        // We use the current real time, but prevent notifying for the next 20 real minutes
                        // since nodes spawn every 35 real minutes.
                        if (!_lastNotified.ContainsKey(favId) || (DateTime.Now - _lastNotified[favId]).TotalMinutes > 20)
                        {
                            _lastNotified[favId] = DateTime.Now;
                            
                            string minText = _configuration.EarlyNodeNotificationMinutes == 0 ? "is active now" : $"spawns in {_configuration.EarlyNodeNotificationMinutes} minute(s)";
                            
                            _chatGui.Print(new Dalamud.Game.Text.XivChatEntry
                            {
                                Message = $"[XIV Hub] ⭐ Your favorited node '{node.name}' {minText} at {node.zone}!",
                                Type = Dalamud.Game.Text.XivChatType.Echo
                            });
                            
                            if (_configuration.EnableNodeAudio)
                            {
                                try
                                {
                                    unsafe
                                    {
                                        FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(1);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }

            ActiveFavoriteCount = activeCount;
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var itemsSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                var regex = new System.Text.RegularExpressions.Regex(@"(\d+)\.png$");

                var nodesJson = await _sender.FetchGatheringNodesAsync();
                if (nodesJson != null)
                {
                    var response = JsonConvert.DeserializeObject<GatheringApiResponse>(nodesJson);
                    if (response != null && response.success)
                    {
                        Nodes = response.data;
                        var itemsList = itemsSheet.ToList();
                        foreach (var n in Nodes)
                        {
                            var itemRow = itemsList.FirstOrDefault(x => x.Name.ToString().Equals(n.name, StringComparison.OrdinalIgnoreCase));
                            if (itemRow.RowId != 0)
                            {
                                n.ItemId = itemRow.RowId;
                                n.IconId = itemRow.Icon;
                                n.Texture = _textureProvider.GetFromGameIcon(new GameIconLookup(n.IconId));
                            }
                        }
                    }
                }

                // Load Data Nodes for Direct Search
                string dataNodesJson = await _sender.FetchDataNodesAsync();
                if (dataNodesJson != null)
                {
                    DataNodesMap = JsonConvert.DeserializeObject<Dictionary<string, DataNode>>(dataNodesJson);
                }

                // Setup Fish Mapping
                var fishSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishParameter>();
                if (fishSheet != null) {
                    foreach(var fish in fishSheet) {
                        if (fish.Item.RowId != 0) _fishMapping[fish.Item.RowId] = (false, fish.RowId);
                    }
                }
                var spearSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingItem>();
                if (spearSheet != null) {
                    foreach(var fish in spearSheet) {
                        if (fish.Item.RowId != 0) _fishMapping[fish.Item.RowId] = (true, fish.RowId);
                    }
                }

                string aquaticJson = await _sender.FetchAquaticNodesAsync();
                if (aquaticJson != null) {
                    var settings = new JsonSerializerSettings {
                        FloatParseHandling = FloatParseHandling.Decimal
                    };
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, AquaticNode>>(aquaticJson, settings);
                    var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();

                    foreach (var kvp in dict) {
                        if (uint.TryParse(kvp.Key, out uint id)) {
                            var node = kvp.Value;
                            node.ItemId = id;
                            if (itemSheet != null) { var texRow = itemSheet.GetRow(id); if (texRow.RowId != 0) node.Texture = _textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(texRow.Icon)); }
                            if (string.IsNullOrEmpty(node.name) && itemSheet != null) {
                                var iRow = itemSheet.GetRow(id);
                                if (iRow.RowId != 0) {
                                    node.name = iRow.Name.ToString();
                                }
                            }
                            if (string.IsNullOrEmpty(node.name)) node.name = $"Unknown Fish ({id})";

                            if (itemSheet != null)
                            {
                                if (node.bait.HasValue && node.bait.Value != 0)
                                {
                                    var bRow = itemSheet.GetRow((uint)node.bait.Value);
                                    if (bRow.RowId != 0) node.BaitName = bRow.Name.ToString();
                                }
                                if (node.mooch != null)
                                {
                                    foreach(var m in node.mooch)
                                    {
                                        var mRow = itemSheet.GetRow((uint)m);
                                        if (mRow.RowId != 0) node.MoochNames.Add(mRow.Name.ToString());
                                    }
                                }
                            }

                            AquaticNodes.Add(node);
                        }
                    }
                    AquaticNodes.Sort((a,b) => string.Compare(a.name ?? "", b.name ?? ""));
                }

                _isDataLoaded = true;

                // Load Log Pages
                string pagesJson = await _sender.FetchGatheringLogPagesAsync();
                if (!string.IsNullOrEmpty(pagesJson))
                {
                    _logPages = JsonConvert.DeserializeObject<List<List<GatheringLogBracket>>>(pagesJson) ?? new();
                    
                    // Enrich items with names and textures
                    foreach (var cat in _logPages)
                    {
                        foreach (var bracket in cat)
                        {
                            foreach (var item in bracket.items)
                            {
                                var itemRow = itemsSheet.GetRowOrDefault(item.itemId);
                                if (itemRow.HasValue)
                                {
                                    item.Name = itemRow.Value.Name.ToString();
                                    item.Texture = _textureProvider.GetFromGameIcon(new GameIconLookup(itemRow.Value.Icon));
                                }
                            }
                        }
                    }
                    
                    RebuildBrackets();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load gathering data");
            }
            finally
            {
                // we leave this empty or restore _isLoading if needed
            }
        }

        private void RebuildBrackets()
        {
            if (_logPages.Count < 4) return;
            
            int cat1Idx = _searchSelectedTab == 0 ? 0 : 2;
            int cat2Idx = _searchSelectedTab == 0 ? 1 : 3;
            
            var bMap = new Dictionary<int, UnifiedBracket>();
            
            foreach (var b in _logPages[cat1Idx])
            {
                int lvl = (int)Math.Floor((b.startLevel - 1) / 5.0) * 5 + 1;
                if (!bMap.ContainsKey(lvl)) bMap[lvl] = new UnifiedBracket { StartLevel = lvl };
                bMap[lvl].Category1.AddRange(b.items);
            }
            
            foreach (var b in _logPages[cat2Idx])
            {
                int lvl = (int)Math.Floor((b.startLevel - 1) / 5.0) * 5 + 1;
                if (!bMap.ContainsKey(lvl)) bMap[lvl] = new UnifiedBracket { StartLevel = lvl };
                bMap[lvl].Category2.AddRange(b.items);
            }
            
            _currentBrackets = bMap.Values.OrderBy(x => x.StartLevel).ToList();
            if (_searchSelectedBracket >= _currentBrackets.Count) _searchSelectedBracket = 0;

            _expansionGroups.Clear();
            var exps = new List<(string name, int min, int max)>
            {
                ("DAWNTRAIL (91-100)", 91, 100),
                ("ENDWALKER (81-90)", 81, 90),
                ("SHADOWBRINGERS (71-80)", 71, 80),
                ("STORMBLOOD (61-70)", 61, 70),
                ("HEAVENSWARD (51-60)", 51, 60),
                ("A REALM REBORN (1-50)", 1, 50)
            };
            
            foreach (var exp in exps)
            {
                var group = new ExpansionBracketGroup { Name = exp.name };
                group.Brackets = _currentBrackets.Where(b => b.StartLevel >= exp.min && b.StartLevel <= exp.max).OrderByDescending(b => b.StartLevel).ToList();
                if (group.Brackets.Count > 0)
                {
                    _expansionGroups.Add(group);
                }
            }
        }

        private class GatheringApiResponse
        {
            public bool success { get; set; }
            public List<GatheringNode> data { get; set; }
        }

        public void Draw()
        {
            if (_isLoading)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Loading gathering data...");
                return;
            }

            var eTime = EorzeaTimeHelper.GetEorzeaTime();
            int currentHour = eTime.Hour;

            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), $"Eorzea Time: {eTime.ToString("HH:mm")}");
            ImGui.Separator();

            // Tabs
            string[] tabs = new string[] { "Timed World Nodes", "Direct Item Search", "Gathering Route", "Fishing" };
            int activeIdx = _viewMode == "timed" ? 0 : (_viewMode == "search" ? 1 : (_viewMode == "route" ? 2 : 3));
            if (UIHelper.DrawPremiumTabSegment(tabs, ref activeIdx, ImGui.GetContentRegionAvail().X))
            {
                if (activeIdx == 0) _viewMode = "timed";
                else if (activeIdx == 1) _viewMode = "search";
                else if (activeIdx == 2) _viewMode = "route";
                else if (activeIdx == 3) _viewMode = "fishing";
            }
            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));

            if (_viewMode == "timed")
            {
                ImGui.Columns(2, "GatheringColumns", true);
                ImGui.SetColumnWidth(0, 250f * PluginUI.AppScale);

                DrawFilters();

                ImGui.NextColumn();
                DrawRadar(eTime, currentHour);
                ImGui.Columns(1);
            }
            else if (_viewMode == "search")
            {
                DrawSearchTab();
            }
            else if (_viewMode == "route")
            {
                DrawRouteTab();
            }
            else if (_viewMode == "fishing")
            {
                DrawFishingTab();
            }
        }

        private string _directSearchText = "";

        private void DrawSearchTab()
        {
            ImGui.Columns(2, "SearchColumns", true);
            ImGui.SetColumnWidth(0, 250f * PluginUI.AppScale);

            // Left Side: Search Input and Class Selection
            ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), "Item Search");
            ImGui.Dummy(new Vector2(0, 5));
            UIHelper.DrawPremiumInputText("txt_direct_search", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X - 10, 35f * PluginUI.AppScale), ref _directSearchText, 64);
            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
            
            string[] classTabs = new string[] { "Miner", "Botanist" };
            if (UIHelper.DrawPremiumTabSegment(classTabs, ref _searchSelectedTab, ImGui.GetContentRegionAvail().X - 10))
            {
                RebuildBrackets();
            }
            ImGui.Dummy(new Vector2(0, 10));

            ImGui.Spacing();
            bool hasSearch = !string.IsNullOrWhiteSpace(_directSearchText);
            
            if (!hasSearch)
            {
                UIHelper.BeginSmoothChild("BracketList", new Vector2(-1, -1), true);
                foreach (var exp in _expansionGroups)
                {
                    ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.3f, 0.3f, 0.3f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.15f, 0.15f, 0.15f, 1f));
                    bool isOpen = ImGui.CollapsingHeader(exp.Name, ImGuiTreeNodeFlags.DefaultOpen);
                    ImGui.PopStyleColor(3);

                    if (isOpen)
                    {
                        foreach (var b in exp.Brackets)
                        {
                            string lvlStr = b.StartLevel == 1 ? "Lv. 1-5" : $"Lv. {b.StartLevel}-{b.StartLevel + 4}";
                            bool isSelected = (_currentBrackets.IndexOf(b) == _searchSelectedBracket);
                            
                            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.0f, 0.65f, 1.0f, 0.6f));
                            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.0f, 0.75f, 1.0f, 0.7f));
                            ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.0f, 0.55f, 1.0f, 0.8f));
                            if (ImGui.Selectable(lvlStr, isSelected))
                            {
                                _searchSelectedBracket = _currentBrackets.IndexOf(b);
                            }
                            ImGui.PopStyleColor(3);
                        }
                    }
                }
                ImGui.EndChild();
            }
            else
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "Search results...");
            }

            ImGui.NextColumn();

            
            if (_configuration.EnableGatheringPrices)
            {
                if (UIHelper.DrawPremiumButton("btn_fetch_prices", ImGui.GetCursorScreenPos(), new Vector2(100, 24), "Fetch Prices", new Vector4(0.12f, 0.12f, 0.14f, 1f), new Vector4(0.0f, 0.65f, 1.0f, 1f), new Vector4(1,1,1,1), new Vector4(1,1,1,1)))
                {
                    if (_currentBrackets.Count > 0 && _searchSelectedBracket < _currentBrackets.Count)
                    {
                        var visibleNodes = new System.Collections.Generic.List<GatheringNode>();
                        var bracket = _currentBrackets[_searchSelectedBracket];
                        
                        var items = new System.Collections.Generic.List<GatheringLogItem>();
                        items.AddRange(bracket.Category1);
                        items.AddRange(bracket.Category2);
                        
                        foreach(var dbItem in items)
                        {
                            var liveNode = Nodes.FirstOrDefault(n => n.ItemId == dbItem.itemId);
                            if (liveNode != null) visibleNodes.Add(liveNode);
                            else {
                                visibleNodes.Add(new GatheringNode { ItemId = dbItem.itemId });
                            }
                        }
                        FetchPricesForVisibleNodes(visibleNodes);
                    }
                }
                if (_isFetchingPrices)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new System.Numerics.Vector4(0f, 1f, 1f, 1f), "Fetching...");
                }
                ImGui.Spacing();
            }

            // Right Side: Items in Bracket
            UIHelper.BeginSmoothChild("BracketItems", new System.Numerics.Vector2(-1, -1), true);
            
            if (hasSearch)
            {
                var lowerQuery = _directSearchText.ToLower();
                var matchingItems = new List<GatheringLogItem>();
                foreach (var b in _currentBrackets)
                {
                    matchingItems.AddRange(b.Category1.Where(i => i.Name != null && i.Name.ToLower().Contains(lowerQuery)));
                    matchingItems.AddRange(b.Category2.Where(i => i.Name != null && i.Name.ToLower().Contains(lowerQuery)));
                }
                
                ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), $"Search Results ({matchingItems.Count})");
                ImGui.Separator();
                DrawCategoryItems(matchingItems);
            }
            else if (_currentBrackets.Count > 0 && _searchSelectedBracket < _currentBrackets.Count)
            {
                var bracket = _currentBrackets[_searchSelectedBracket];
                
                string cat1Name = _searchSelectedTab == 0 ? "Mining" : "Logging";
                string cat2Name = _searchSelectedTab == 0 ? "Quarrying" : "Harvesting";
                
                ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), cat1Name);
                ImGui.Separator();
                DrawCategoryItems(bracket.Category1);
                
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), cat2Name);
                ImGui.Separator();
                DrawCategoryItems(bracket.Category2);
            }
            ImGui.EndChild();

            ImGui.Columns(1);
        }
        
        private void DrawCategoryItems(List<GatheringLogItem> items)
        {
            if (items.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No items found.");
                return;
            }
            
            float iconSize = 36f * PluginUI.AppScale;
            var eTime = EorzeaTimeHelper.GetEorzeaTime();
            int currentHour = eTime.Hour;

            foreach (var item in items)
            {
                bool hasLocation = TryGetLocationForItem(item.itemId, out _, out _, out _, out _, out _, out _, out _);
                float rowHeight = iconSize + 18f * PluginUI.AppScale;

                Vector2 p = ImGui.GetCursorScreenPos();
                float rowWidth = ImGui.GetContentRegionAvail().X;
                ImGui.GetWindowDrawList().AddRectFilled(p, new Vector2(p.X + rowWidth, p.Y + rowHeight), ImGui.GetColorU32(new Vector4(1,1,1,0.02f)), 6f);
                
                ImGui.SetCursorScreenPos(new Vector2(p.X + 8f, p.Y + 8f));
                if (item.Texture != null && item.Texture.GetWrapOrDefault() != null)
                {
                    ImGui.Image(item.Texture.GetWrapOrDefault().Handle, new Vector2(iconSize, iconSize));
                }
                else
                {
                    ImGui.Dummy(new Vector2(iconSize, iconSize));
                }
                
                // Name and Stars
                ImGui.SetCursorScreenPos(new Vector2(p.X + iconSize + 20f * PluginUI.AppScale, p.Y + 8f));
                ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), item.Name ?? $"Item #{item.itemId}");
                if (item.stars > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), new string('★', item.stars));
                }
                
                // Lvl
                ImGui.SetCursorScreenPos(new Vector2(p.X + iconSize + 20f * PluginUI.AppScale, p.Y + 28f * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Lv. {item.lvl}");
                
                if (_configuration.EnableGatheringPrices && _priceCache.TryGetValue((uint)item.itemId, out int price))
                {
                    ImGui.SameLine(0, 15f * PluginUI.AppScale);
                    ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), $"{price:N0} Gil");
                }
                
                // Tags
                float tagXOffset = ImGui.GetCursorPosX() + 10f * PluginUI.AppScale;
                
                Action<string, Vector4, Vector4> drawTag = (text, textColor, bgColor) =>
                {
                    ImGui.SameLine(0, 5f);
                    Vector2 tp = ImGui.GetCursorScreenPos();
                    Vector2 size = ImGui.CalcTextSize(text);
                    ImGui.GetWindowDrawList().AddRectFilled(tp, new Vector2(tp.X + size.X + 8f, tp.Y + size.Y + 4f), ImGui.GetColorU32(bgColor), 4f);
                    ImGui.GetWindowDrawList().AddRect(tp, new Vector2(tp.X + size.X + 8f, tp.Y + size.Y + 4f), ImGui.GetColorU32(new Vector4(textColor.X, textColor.Y, textColor.Z, 0.3f)), 4f);
                    ImGui.SetCursorScreenPos(new Vector2(tp.X + 4f, tp.Y + 2f));
                    ImGui.TextColored(textColor, text);
                    ImGui.SetCursorScreenPos(new Vector2(tp.X + size.X + 12f, tp.Y - 2f)); // Reset for next inline
                };

                var tags = new List<(string label, Vector4 color, Vector4 bg)>();
                if (item.hidden == 1) tags.Add(("HIDDEN", new Vector4(1f, 0.7f, 0.3f, 1f), new Vector4(1f, 0.7f, 0.3f, 0.15f)));

                bool isLegendary = false;
                bool isEphemeral = false;
                bool isUnspoiled = false;
                List<int> times = new();

                foreach (var nodeEntry in DataNodesMap.Values)
                {
                    var drops = new List<int>();
                    if (nodeEntry.items != null) drops.AddRange(nodeEntry.items);
                    if (nodeEntry.hiddenItems != null) drops.AddRange(nodeEntry.hiddenItems);
                    
                    if (drops.Contains((int)item.itemId))
                    {
                        if (nodeEntry.legendary) isLegendary = true;
                        if (nodeEntry.ephemeral) isEphemeral = true;
                        if (nodeEntry.time != null && nodeEntry.time.Count > 0)
                        {
                            times.AddRange(nodeEntry.time);
                        }
                        if (nodeEntry.spawns != null && nodeEntry.spawns.Count > 0)
                        {
                            times.AddRange(nodeEntry.spawns);
                            if (!nodeEntry.legendary && !nodeEntry.ephemeral) isUnspoiled = true;
                        }
                    }
                }

                var lNode = Nodes.FirstOrDefault(x => x.ItemId == item.itemId);
                if (lNode != null)
                {
                    if (lNode.nodeType == "Legendary") isLegendary = true;
                    if (lNode.nodeType == "Ephemeral") isEphemeral = true;
                    if (lNode.nodeType == "Unspoiled") isUnspoiled = true;
                    if (lNode.hours != null && lNode.hours.Count > 0)
                    {
                        foreach (var h in lNode.hours) if (!times.Contains(h)) times.Add(h);
                    }
                }

                if (isLegendary) tags.Add(("LEGENDARY / FOLKLORE", new Vector4(1f, 0.4f, 0.4f, 1f), new Vector4(1f, 0.4f, 0.4f, 0.15f)));
                if (isEphemeral) tags.Add(("EPHEMERAL", new Vector4(0.6f, 0.4f, 1f, 1f), new Vector4(0.6f, 0.4f, 1f, 0.15f)));
                if (isUnspoiled) tags.Add(("UNSPOILED", new Vector4(0.4f, 0.8f, 1f, 1f), new Vector4(0.4f, 0.8f, 1f, 0.15f)));

                foreach (var t in tags) drawTag(t.label, t.color, t.bg);
                
                // Timer (right aligned)
                bool isTimed = times.Count > 0;
                
                if (isTimed)
                {
                    GatheringNode timerNode = lNode ?? new GatheringNode { hours = times, nodeType = isEphemeral ? "Ephemeral" : "Unspoiled" };
                    bool isUpNow = EorzeaTimeHelper.IsNodeActive(timerNode, currentHour);
                    string timeText = "";
                    if (isUpNow)
                    {
                        int secLeft = (int)EorzeaTimeHelper.GetActiveSecondsLeft(timerNode, eTime);
                        int m = secLeft / 60;
                        int s = secLeft % 60;
                        timeText = $"{m}m {s}s left";
                    }
                    else
                    {
                        int realSecs = (int)EorzeaTimeHelper.GetRealSecondsLeft(timerNode, eTime);
                        int hr = realSecs / 3600;
                        int mn = (realSecs % 3600) / 60;
                        timeText = hr > 0 ? $"in {hr}h {mn}m" : $"in {mn}m";
                    }

                    string fullText = $"{(char)Dalamud.Interface.FontAwesomeIcon.Clock} {timeText}";
                    var timeSize = ImGui.CalcTextSize(fullText);
                    
                    float rightMargin = 140f * PluginUI.AppScale;
                    ImGui.SetCursorScreenPos(new Vector2(p.X + rowWidth - timeSize.X - rightMargin, p.Y + (rowHeight - timeSize.Y) / 2));
                    
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    ImGui.TextColored(isUpNow ? new Vector4(0.33f, 0.8f, 1f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f), $"{(char)Dalamud.Interface.FontAwesomeIcon.Clock}");
                    ImGui.PopFont();
                    ImGui.SameLine(0, 4f * PluginUI.AppScale);
                    ImGui.TextColored(isUpNow ? new Vector4(0.33f, 0.8f, 1f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f), timeText);
                }

                // Location Button
                if (hasLocation)
                {
                    TryGetLocationForItem(item.itemId, out int zoneid, out int mapId, out float mapX, out float mapY, out string zoneName, out string coords, out GatheringNode liveNode2);

                    uint aetheryteIdForTp = 0;
                    if (mapId > 0)
                    {
                        aetheryteIdForTp = GetAetheryteIdForMap(mapId);
                    }
                    bool canTeleportDirectly = aetheryteIdForTp != 0;
                    float actionButtonsWidth = canTeleportDirectly ? 110f * PluginUI.AppScale : 75f * PluginUI.AppScale;

                    ImGui.SetCursorScreenPos(new Vector2(p.X + rowWidth - actionButtonsWidth, p.Y + (rowHeight - 30f * PluginUI.AppScale) / 2));
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.MapMarkerAlt)}##loc_{item.itemId}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale)))
                    {
                        TryCreateMapLinkForItem(item.itemId);
                    }
                    ImGui.PopFont();
                    if (ImGui.IsItemHovered())
                    {
                        UIHelper.BeginTooltip();
                        ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Show Location on Map");
                        ImGui.Separator();
                        string locText = coords != null ? $"{zoneName} ({coords})" : $"{zoneName}";
                        ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), locText);
                        if (coords == null) {
                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(Exact coordinates unknown)");
                        }
                        UIHelper.EndTooltip();
                    }
                    
                    if (canTeleportDirectly)
                    {
                        ImGui.SameLine(0, 5f * PluginUI.AppScale);
                        ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                        bool clickedTp = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.PlaneDeparture)}##tp_search_{item.itemId}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale));
                        ImGui.PopFont();
                        if (clickedTp)
                        {
                            TeleportToAetheryte(aetheryteIdForTp);
                        }
                        DrawTeleportTooltip(aetheryteIdForTp);
                    }
                }

                // Add to Route Button
                ImGui.SetCursorScreenPos(new Vector2(p.X + rowWidth - 40f * PluginUI.AppScale, p.Y + (rowHeight - 30f * PluginUI.AppScale) / 2));
                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.Plus)}##route_{item.itemId}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale)))
                {
                    _itemToAddRoute = item.itemId;
                    _routeTargetQuantity = 100;
                    ImGui.OpenPopup($"AddRouteItemPopup_{item.itemId}");
                }
                ImGui.PopFont();
                UIHelper.DrawTooltip("Add to Route");

                if (ImGui.BeginPopup($"AddRouteItemPopup_{item.itemId}"))
                {
                    ImGui.Text($"Target Quantity:");
                    ImGui.InputInt("##qty", ref _routeTargetQuantity);
                    if (ImGui.Button("Add to Route"))
                    {
                        if (!_configuration.GatheringActiveRoute.Any(x => x.ItemId == _itemToAddRoute))
                        {
                            _configuration.GatheringActiveRoute.Add(new RouteItem
                            {
                                ItemId = _itemToAddRoute,
                                TargetQuantity = _routeTargetQuantity,
                                IsCompleted = false
                            });
                            _configuration.Save();
                        }
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.EndPopup();
                }

                ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + rowHeight + 6f * PluginUI.AppScale));
            }
        }

        private bool TryGetLocationForItem(uint itemId, out int zoneid, out int mapId, out float x, out float y, out string zoneName, out string coords, out GatheringNode liveNode)
        {
            zoneid = 0;
            mapId = 0;
            x = 0;
            y = 0;
            zoneName = null;
            coords = null;
            liveNode = null;

            bool foundInNodes = false;
            // First try timed nodes (because they have coords in string format for UI)
            liveNode = Nodes.FirstOrDefault(n => n.ItemId == itemId);
            if (liveNode != null)
            {
                zoneName = liveNode.zone;
                coords = liveNode.coords;
                foundInNodes = true;
            }

            if (!foundInNodes)
            {
                var fNode = AquaticNodes.FirstOrDefault(n => n.ItemId == itemId);
                if (fNode != null)
                {
                    zoneName = fNode.bestZone;
                    foundInNodes = true;
                }
            }

            // Then try DataNodesMap to get the actual mapId and exact coords
            foreach (var nodeEntry in DataNodesMap.Values)
            {
                var drops = new List<int>();
                if (nodeEntry.items != null) drops.AddRange(nodeEntry.items);
                if (nodeEntry.hiddenItems != null) drops.AddRange(nodeEntry.hiddenItems);
                
                if (drops.Contains((int)itemId))
                {
                    if (nodeEntry.zoneid == 0) continue; // Skip nodes with no location

                    zoneid = nodeEntry.zoneid;
                    mapId = nodeEntry.map;
                    x = nodeEntry.x;
                    y = nodeEntry.y;

                    if (x > 0 && y > 0)
                    {
                        coords = $"X: {x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}, Y: {y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
                    }

                    var place = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>()?.GetRow((uint)zoneid);
                    if (place.HasValue)
                    {
                        zoneName = place.Value.Name.ToString();
                    }
                    return true;
                }
            }

            if (mapId == 0 && !string.IsNullOrEmpty(zoneName))
            {
                var territories = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
                if (territories != null)
                {
                    var cleanZone = zoneName.Replace("'", "").Replace(" ", "");
                    foreach (var territory in territories)
                    {
                        if (territory.Map.RowId == 0) continue;
                        var placeName = territory.PlaceName.Value;
                        var cleanPlace = placeName.Name.ToString().Replace("'", "").Replace(" ", "");
                        
                        if (cleanPlace.Contains(cleanZone, StringComparison.OrdinalIgnoreCase))
                        {
                            mapId = (int)territory.Map.RowId;
                            zoneid = (int)placeName.RowId;
                            break;
                        }
                    }
                }
            }

            return foundInNodes;
        }

        private void TryCreateMapLinkForItem(uint itemId)
        {
            if (TryGetLocationForItem(itemId, out int zoneid, out int mapId, out float x, out float y, out string zoneName, out string coords, out GatheringNode liveNode))
            {
                if (mapId > 0 && x > 0 && y > 0)
                {
                    TryCreateMapLinkFromMapCoords(mapId, x, y);
                }
                else if (liveNode != null)
                {
                    TryCreateMapLink(liveNode);
                }
                else if (zoneid > 0)
                {
                    TryCreateMapLinkFromZone(zoneid);
                }
            }
        }

        private void TryCreateMapLinkFromMapCoords(int mapId, float x, float y)
        {
            try
            {
                var maps = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
                
                // mapId from JSON might be a sub-map (e.g. 4 for Lower La Noscea instead of main map 14).
                // We use it to find the TerritoryType, then use the TerritoryType's main map!
                var mapRow = maps?.GetRow((uint)mapId);
                
                if (mapRow.HasValue)
                {
                    var territoryId = mapRow.Value.TerritoryType.RowId;
                    if (territoryId > 0)
                    {
                        var territory = mapRow.Value.TerritoryType.Value;
                        // Always use the Territory's MAIN Map ID to ensure Dalamud accepts the flag
                        var mainMapId = territory.Map.RowId != 0 
                            ? territory.Map.RowId 
                            : mapRow.Value.RowId;
                            
                        var mapLink = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(
                            territoryId, mainMapId, x, y);
                        _gameGui.OpenMapWithMapLink(mapLink);
                        _log.Info($"Opened map via MapId {mapId} at {x}, {y} -> (Territory: {territoryId}, MainMap: {mainMapId})");
                    }
                    else
                    {
                        _log.Warning($"MapId {mapId} has no TerritoryType.");
                    }
                }
                else
                {
                    _log.Warning($"MapId {mapId} not found in Map sheet.");
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Failed to create MapLink for mapId {mapId} at {x}, {y}");
            }
        }

        private void TryCreateMapLinkFromZone(int placeNameId)
        {
            try
            {
                var territories = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
                var territory = territories?.FirstOrDefault(x => x.PlaceName.RowId == (uint)placeNameId && x.Map.RowId > 0);
                if (territory.HasValue && territory.Value.RowId > 0)
                {
                    var mapLink = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(
                        territory.Value.RowId, territory.Value.Map.RowId, 20f, 20f);
                    _gameGui.OpenMapWithMapLink(mapLink);
                }
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        private unsafe int GetItemCount(uint itemId)
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (manager == null) return 0;
            return manager->GetInventoryItemCount(itemId, false, false, false, 0);
        }

        private uint _lastTerritory = 0;
        private uint _activeRouteTargetItemId = 0;
        
        private bool _isRouteRunning = false;
        private bool _showCancelConfirm = false;
        private bool _isWaitingForNode = false;
        private uint _completedItemForPopup = 0;
        private RouteItem _popupNextTarget = null;
        private uint _popupNextAetheryteId = 0;
        
        // Universalis cache
        private Dictionary<uint, int> _priceCache = new();
        private Dictionary<uint, DateTime> _priceCacheTime = new();
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private bool _isFetchingPrices = false;
        
        private async void FetchPricesForVisibleNodes(IEnumerable<GatheringNode> nodes)
        {
            if (_isFetchingPrices) return;
            var itemIds = nodes.Where(n => n.ItemId > 0).Select(n => n.ItemId).Distinct().ToList();
            if (itemIds.Count == 0) return;
            
            string worldName = ((_objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)?.HomeWorld.Value.Name.ToString());
            if (string.IsNullOrEmpty(worldName)) worldName = "Ragnarok"; // fallback
            
            _isFetchingPrices = true;
            try
            {
                // Universalis allows max 100 items per request, but we usually show 20-30 max
                string idsStr = string.Join(",", itemIds);
                string url = $"https://universalis.app/api/v2/{worldName}/{idsStr}";
                _log.Information($"[Universalis] Fetching prices from: {url}");
                var response = await _httpClient.GetAsync(url);
                _log.Information($"[Universalis] Response Code: {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var jDoc = System.Text.Json.JsonDocument.Parse(json);
                    
                    if (jDoc.RootElement.TryGetProperty("items", out var itemsElement))
                    {
                        foreach (var prop in itemsElement.EnumerateObject())
                        {
                            if (uint.TryParse(prop.Name, out uint id))
                            {
                                if (prop.Value.TryGetProperty("minPrice", out var minPriceElement))
                                {
                                    if (minPriceElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    {
                                        _priceCache[id] = minPriceElement.GetInt32();
                                        _priceCacheTime[id] = DateTime.Now;
                                        _log.Information($"[Universalis] Cached price for {id} = {minPriceElement.GetInt32()}");
                                    }
                                }
                            }
                        }
                    }
                    else if (jDoc.RootElement.TryGetProperty("minPrice", out var singleMinPrice))
                    {
                        // Single item response
                        if (singleMinPrice.ValueKind == System.Text.Json.JsonValueKind.Number && itemIds.Count == 1)
                        {
                            _priceCache[itemIds[0]] = singleMinPrice.GetInt32();
                            _priceCacheTime[itemIds[0]] = DateTime.Now;
                            _log.Information($"[Universalis] Cached single price for {itemIds[0]} = {singleMinPrice.GetInt32()}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch prices from Universalis");
            }
            finally
            {
                _isFetchingPrices = false;
            }
        }
        
        private void OptimizeRoute()
        {
            if (_configuration.GatheringActiveRoute.Count == 0) return;
            
            var sortedList = _configuration.GatheringActiveRoute.OrderBy(r => 
            {
                if (TryGetLocationForItem(r.ItemId, out int zoneid, out int mapId, out float x, out float y, out string zoneName, out string coords, out GatheringNode liveNode))
                {
                    return mapId;
                }
                return 9999;
            }).ToList();
            
            _configuration.GatheringActiveRoute.Clear();
            _configuration.GatheringActiveRoute.AddRange(sortedList);
            _configuration.Save();
        }
        private RouteItem GetNextRouteTarget()
        {
            var activeRoute = _configuration.GatheringActiveRoute;
            DateTime eTime = EorzeaTimeHelper.GetEorzeaTime();
            int currentHour = eTime.Hour;
            
            // 1. Check for active Timed Nodes
            foreach (var item in activeRoute)
            {
                if (item.IsCompleted) continue;
                var liveNode = Nodes.FirstOrDefault(n => n.ItemId == item.ItemId);
                if (liveNode != null && liveNode.hours != null && liveNode.hours.Count > 0)
                {
                    if (EorzeaTimeHelper.IsNodeActive(liveNode, currentHour))
                    {
                        return item;
                    }
                }
            }
            
            // 2. Check for standard nodes
            foreach (var item in activeRoute)
            {
                if (item.IsCompleted) continue;
                var liveNode = Nodes.FirstOrDefault(n => n.ItemId == item.ItemId);
                if (liveNode == null || liveNode.hours == null || liveNode.hours.Count == 0)
                {
                    return item; // standard node
                }
            }
            
            return null; // only waiting timed nodes left
        }

        private unsafe bool IsFishCaught(uint itemId) {
            if (!_fishMapping.TryGetValue(itemId, out var mapping)) return false;
            var ps = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            if (ps == null) return false;
            
            var offset = mapping.rowId / 8;
            var bit = (byte)(mapping.rowId % 8);
            
            if (mapping.isSpear) {
                if (ps->CaughtSpearfishBitArray.Pointer == null) return false;
                return ((ps->CaughtSpearfishBitArray.Pointer[offset] >> bit) & 1) == 1;
            } else {
                if (ps->CaughtFishBitArray.Pointer == null) return false;
                return ((ps->CaughtFishBitArray.Pointer[offset] >> bit) & 1) == 1;
            }
        }

        private string _fishSearch = "";
        private Dictionary<uint, float> _fishAnimState = new();
        private Dictionary<uint, float> _fishIconScale = new();
        private bool _fishHideCaught = false;
        private int _fishSortMode = 0; // 0 = Alphabetical, 1 = Uptime, 2 = Patch
        
        private void DrawFishingTab()
        {
            UpdateFishUptimesCache();
            DateTime now = DateTime.UtcNow;

            ImGui.BeginGroup();
            ImGui.InputTextWithHint("##fish_search", "Search Fish...", ref _fishSearch, 300);
            ImGui.SameLine();
            ImGui.Checkbox("Hide Caught", ref _fishHideCaught);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180);
            ImGui.Combo("Sort By", ref _fishSortMode, new string[] { "Alphabetical (A-Z)", "Uptime (Active First)", "Patch (Newest First)" }, 3);
            ImGui.EndGroup();
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // Premium custom UI - using cards instead of standard table
            ImGuiWindowFlags childFlags = ImGuiWindowFlags.None;
            if (!_configuration.HideScrollbars)
                childFlags |= ImGuiWindowFlags.AlwaysVerticalScrollbar;

            ImGui.BeginChild("FishingDirectoryList", new Vector2(0, 0), false, childFlags);
            
            var weatherSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Weather>();
            
            List<AquaticNode> sortedNodes;
              if (_fishSortMode == 1)
              {
                  sortedNodes = AquaticNodes.OrderBy(f => {
                      var uptime1 = _fishUptimesCache.ContainsKey(f.ItemId) ? _fishUptimesCache[f.ItemId] : null;
                      if (uptime1 == null) return 2;
                      if (now >= uptime1.Value.start && now < uptime1.Value.end) return 0;
                      return 1;
                  }).ThenBy(f => {
                      var uptime2 = _fishUptimesCache.ContainsKey(f.ItemId) ? _fishUptimesCache[f.ItemId] : null;
                      if (uptime2 == null) return 0.0;
                      if (now >= uptime2.Value.start && now < uptime2.Value.end) return (uptime2.Value.end - now).TotalSeconds;
                      return (uptime2.Value.start - now).TotalSeconds;
                  }).ThenBy(f => f.name ?? "").ToList();
              }
              else if (_fishSortMode == 2)
              {
                  sortedNodes = AquaticNodes.OrderByDescending(f => f.patch.HasValue ? f.patch.Value : 0.0).ThenBy(f => f.name ?? "").ToList();
              }
              else
              {
                  sortedNodes = AquaticNodes.OrderBy(f => f.name ?? "").ToList();
              }            foreach (var f in sortedNodes)
            {
                bool caught = IsFishCaught(f.ItemId);
                if (_fishHideCaught && caught) continue;

                string nameLower = f.name?.ToLower() ?? "";
                string spotLower = f.bestSpot?.ToLower() ?? "";
                string zoneLower = f.bestZone?.ToLower() ?? "";
                string baitLower = f.BaitName?.ToLower() ?? "";
                string searchLower = _fishSearch.ToLower();

                if (!string.IsNullOrEmpty(_fishSearch) && !nameLower.Contains(searchLower) && !spotLower.Contains(searchLower) && !zoneLower.Contains(searchLower) && !baitLower.Contains(searchLower)) 
                    continue;

                Vector2 p = ImGui.GetCursorScreenPos();
                float width = ImGui.GetContentRegionAvail().X;
                
                string timeStatus = "Always Available";
                Vector4 statusColor = new Vector4(0.5f, 0.9f, 0.5f, 1f);
                bool isUp = false;
                
                if (_fishUptimesCache.TryGetValue(f.ItemId, out var uptime) && uptime != null)
                {
                    if (now >= uptime.Value.start && now < uptime.Value.end)
                    {
                        isUp = true;
                        var remaining = uptime.Value.end - now;
                        timeStatus = string.Format("Active {0}m {1}s", remaining.Minutes, remaining.Seconds);
                        statusColor = new Vector4(0.2f, 1f, 0.2f, 1f);
                    }
                    else
                    {
                        var wait = uptime.Value.start - now;
                        string waitStr = wait.TotalDays >= 1 ? string.Format("in {0}d {1}h", (int)wait.TotalDays, wait.Hours) : 
                                         wait.TotalHours >= 1 ? string.Format("in {0}h {1}m", wait.Hours, wait.Minutes) : 
                                         string.Format("in {0}m {1}s", wait.Minutes, wait.Seconds);
                        timeStatus = string.Format("Upcoming {0}", waitStr);
                        statusColor = new Vector4(1f, 0.5f, 0.1f, 1f);
                    }
                }
                
                ImGui.PushStyleColor(ImGuiCol.Header, isUp ? new Vector4(0.1f, 0.3f, 0.4f, 0.6f) : new Vector4(0.12f, 0.12f, 0.15f, 1f));
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(1f, 1f, 1f, 0.1f));
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(1f, 1f, 1f, 0.15f));
                
                bool isBig = f.bigFish.HasValue && f.bigFish.Value;
                Vector4 nameColor = isBig ? new Vector4(1f, 0.85f, 0.3f, 1f) : new Vector4(0.9f, 0.9f, 0.9f, 1f);
                ImGui.PushStyleColor(ImGuiCol.Text, nameColor);

                float iconSize = 20f * PluginUI.AppScale;
                string fishLabel = caught ? string.Format("       {0} {1}", (char)Dalamud.Interface.FontAwesomeIcon.CheckCircle, f.name) : string.Format("       {0}", f.name);
                
                Vector2 iconPos = ImGui.GetCursorScreenPos();
                bool expanded = ImGui.CollapsingHeader(string.Format("{0}###fish_{1}", fishLabel, f.ItemId));
                
                // Animation states
                float dt = ImGui.GetIO().DeltaTime;
                
                float animTarget = expanded ? 1f : 0f;
                float currentAnim = _fishAnimState.ContainsKey(f.ItemId) ? _fishAnimState[f.ItemId] : 0f;
                _fishAnimState[f.ItemId] = currentAnim + (animTarget - currentAnim) * (dt * 15f);
                float anim = _fishAnimState[f.ItemId];
                
                Vector2 iconMin = iconPos + new Vector2(24f * PluginUI.AppScale, 2f * PluginUI.AppScale);
                Vector2 iconMax = iconMin + new Vector2(iconSize, iconSize);
                bool iconHovered = ImGui.IsMouseHoveringRect(iconMin, iconMax);
                float scaleTarget = iconHovered ? 1.5f : 1f;
                float currentScale = _fishIconScale.ContainsKey(f.ItemId) ? _fishIconScale[f.ItemId] : 1f;
                _fishIconScale[f.ItemId] = currentScale + (scaleTarget - currentScale) * (dt * 15f);
                float iconScale = _fishIconScale[f.ItemId];
                
                if (f.Texture != null && f.Texture.GetWrapOrDefault() != null)
                {
                    float scaledSize = iconSize * iconScale;
                    Vector2 center = iconMin + new Vector2(iconSize / 2f, iconSize / 2f);
                    ImGui.GetWindowDrawList().AddImage(f.Texture.GetWrapOrDefault().Handle, center - new Vector2(scaledSize / 2f, scaledSize / 2f), center + new Vector2(scaledSize / 2f, scaledSize / 2f));
                }
                
                ImGui.PopStyleColor(4);
                
                ImGui.SameLine(width - 150f * PluginUI.AppScale);
                ImGui.TextColored(statusColor, timeStatus);
                
                if (anim > 0.01f)
                {
                    ImGui.Indent();
                    
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, anim);
                    ImGui.BeginChild(string.Format("fishChild_{0}", f.ItemId), new Vector2(0, 160f * PluginUI.AppScale * anim), false, ImGuiWindowFlags.NoScrollbar);
                    
                    if (ImGui.BeginTable(string.Format("fishDetails_{0}", f.ItemId), 2, ImGuiTableFlags.BordersInnerV))
                    {
                        ImGui.TableSetupColumn("Requirements", ImGuiTableColumnFlags.WidthStretch, 1.5f);
                        ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch, 1f);
                        
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        
                        if (f.time != null && f.time.Count == 2)
                        {
                            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), string.Format("Time: {0:D2}:00 - {1:D2}:00", f.time[0]/60, f.time[1]/60));
                        }
                        
                        if (f.weathers != null && f.weathers.Count > 0)
                        {
                            string weatherStr = string.Join(", ", f.weathers.Select(w => weatherSheet?.GetRowOrDefault((uint)w)?.Name.ToString() ?? w.ToString()));
                            if (f.previousWeathers != null && f.previousWeathers.Count > 0)
                            {
                                string prevStr = string.Join(", ", f.previousWeathers.Select(w => weatherSheet?.GetRowOrDefault((uint)w)?.Name.ToString() ?? w.ToString()));
                                ImGui.TextColored(new Vector4(0.8f, 0.4f, 1f, 1f), string.Format("Weather: [{0}] -> [{1}]", prevStr, weatherStr));
                            }
                            else
                            {
                                ImGui.TextColored(new Vector4(0.8f, 0.4f, 1f, 1f), string.Format("Weather: [{0}]", weatherStr));
                            }
                        }
                        
                        ImGui.Dummy(new Vector2(0, 5));
                        ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                        if (caught)
                        {
                            ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), ((char)Dalamud.Interface.FontAwesomeIcon.CheckCircle).ToString());
                            ImGui.PopFont();
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), "Status: Caught");
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), ((char)Dalamud.Interface.FontAwesomeIcon.TimesCircle).ToString());
                            ImGui.PopFont();
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(1f, 0.2f, 0.2f, 1f), "Status: Not Caught");
                        }
                        string baitStr = "";
                        if (f.BaitName != null)
                        {
                            baitStr = f.BaitName;
                        }
                        if (f.MoochNames != null && f.MoochNames.Count > 0)
                        {
                            if (!string.IsNullOrEmpty(baitStr)) baitStr += " -> ";
                            baitStr += string.Join(" -> ", f.MoochNames);
                        }
                        if (string.IsNullOrEmpty(baitStr)) baitStr = "Unknown";
                        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), string.Format("Bait: {0}", baitStr));
                        
                        if (!string.IsNullOrEmpty(f.hookset))
                        {
                            bool isPrec = f.hookset.Contains("Precision");
                            ImGui.TextColored(isPrec ? new Vector4(0.4f, 1f, 0.4f, 1f) : new Vector4(1f, 0.4f, 0.4f, 1f), f.hookset);
                            ImGui.SameLine();
                        }
                        
                        if (!string.IsNullOrEmpty(f.tug) || !string.IsNullOrEmpty(f.biteType))
                        {
                            ImGui.TextColored(new Vector4(1f, 1f, 0.2f, 1f), string.Format("Bite: {0}", f.tug ?? f.biteType));
                        }
                        else
                        {
                            ImGui.NewLine();
                        }
                        
                        if (f.snagging.HasValue && f.snagging.Value)
                        {
                            ImGui.TextColored(new Vector4(0.4f, 0.6f, 1f, 1f), "Requires Snagging");
                        }
                        if (f.fishEyes.HasValue && f.fishEyes.Value)
                        {
                            ImGui.TextColored(new Vector4(0.2f, 0.8f, 1f, 1f), "Requires Fish Eyes");
                        }
                        if (f.intuitionLength.HasValue)
                        {
                            ImGui.TextColored(new Vector4(1f, 0.4f, 0.6f, 1f), string.Format("Fisher's Intuition ({0}m)", f.intuitionLength.Value));
                        }
                        
                        ImGui.TableNextColumn();
                        
                        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), f.bestZone ?? "Unknown Zone");
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), f.bestSpot ?? "Unknown Spot");
                        
                        if (f.patch > 0)
                        {
                            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), string.Format("Patch {0:F1}", f.patch));
                        }
                        if (f.collectable)
                        {
                            ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), "Collectable");
                        }

                        if (ImGui.Button(string.Format("Show on Map##map_{0}", f.ItemId)))
                        {
                            TryCreateMapLinkForItem(f.ItemId);
                        }
                        
                        if (TryGetLocationForItem(f.ItemId, out _, out int mapId, out _, out _, out _, out _, out _))
                        {
                            uint aethId = mapId > 0 ? GetAetheryteIdForMap(mapId) : 0;
                            if (aethId > 0)
                            {
                                ImGui.SameLine();
                                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                                if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.PlaneDeparture)}##tp_{f.ItemId}"))
                                {
                                    TeleportToAetheryte(aethId);
                                }
                                ImGui.PopFont();
                                DrawTeleportTooltip(aethId);
                            }
                        }
                        
                        ImGui.EndTable();
                    }
                    
                    ImGui.EndChild();
                    ImGui.PopStyleVar();
                    
                    ImGui.Unindent();
                    ImGui.Dummy(new Vector2(0, 4));
                }
            }
  
              ImGui.EndChild();
        }

                        private void DrawRouteTab()
        {
            if (_shouldOpenMapLink && !_condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas])
            {
                _shouldOpenMapLink = false;
                TryCreateMapLinkForItem(_mapLinkTargetItemId);
            }

            var activeRoute = _configuration.GatheringActiveRoute;
            if (activeRoute.Count == 0)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Gathering Route");
                ImGui.TextWrapped("Your route is empty. Add items to your route from the Direct Item Search tab.");
                return;
            }

            // Route Controls
            ImGui.BeginGroup();
            if (!_isRouteRunning)
            {
                                if (UIHelper.DrawPremiumButton("btn_start_route", ImGui.GetCursorScreenPos(), new System.Numerics.Vector2(100f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Start Route", new System.Numerics.Vector4(0.1f, 0.6f, 0.2f, 1f), new System.Numerics.Vector4(0.2f, 0.8f, 0.3f, 1f), new System.Numerics.Vector4(1,1,1,1), new System.Numerics.Vector4(1,1,1,1)))
                {
                    _isRouteRunning = true;
                    if (!_configuration.IsManualRouteOverride)
                    {
                        OptimizeRoute();
                    }
                    var startTarget = GetNextRouteTarget();
                    if (startTarget != null)
                    {
                        if (TryGetLocationForItem(startTarget.ItemId, out int pName, out int nMap, out _, out _, out _, out _, out _))
                        {
                            var currentTerritory = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRow(_clientState.TerritoryType);
                            if (currentTerritory.HasValue && currentTerritory.Value.PlaceName.RowId != (uint)pName && currentTerritory.Value.Map.RowId != (uint)nMap)
                            {
                                _popupNextTarget = startTarget;
                                _popupNextAetheryteId = GetAetheryteIdForMap(nMap);
                                _showIntegratedZoneGuidance = true;
                                _isStartRouteGuidance = true;
                            }
                            else if (currentTerritory.HasValue && (currentTerritory.Value.PlaceName.RowId == (uint)pName || currentTerritory.Value.Map.RowId == (uint)nMap))
                            {
                                TryCreateMapLinkForItem(startTarget.ItemId);
                            }
                        }
                    }
                }
            }
            else
            {
                if (UIHelper.DrawPremiumButton("btn_pause_route", ImGui.GetCursorScreenPos(), new Vector2(100f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Pause Route", new Vector4(0.8f, 0.5f, 0.1f, 1f), new Vector4(0.9f, 0.6f, 0.2f, 1f), new Vector4(1,1,1,1), new Vector4(1,1,1,1)))
                {
                    _isRouteRunning = false;
                }
            }
            
            ImGui.SameLine();
            if (UIHelper.DrawPremiumButton("btn_cancel_route", ImGui.GetCursorScreenPos(), new Vector2(100f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Cancel Route", new Vector4(0.7f, 0.2f, 0.2f, 1f), new Vector4(0.9f, 0.3f, 0.3f, 1f), new Vector4(1,1,1,1), new Vector4(1,1,1,1)))
            {
                _showCancelConfirm = true;
            }
            ImGui.EndGroup();
            
            if (_showCancelConfirm)
            {
                ImGui.TextColored(new Vector4(0.93f, 0.79f, 0.32f, 1f), "Are you sure you want to cancel and reset the route?");
                if (ImGui.Button("Yes, Cancel", new Vector2(100, 24)))
                {
                    _isRouteRunning = false;
                    _showCancelConfirm = false;
                    foreach(var r in activeRoute) r.IsCompleted = false;
                    _configuration.Save();
                }
                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(60, 24)))
                {
                    _showCancelConfirm = false;
                }
            }

            ImGui.Dummy(new Vector2(0, 10));

            // Update State
            RouteItem nextTarget = null;
            bool routeChanged = false;
            
            if (_isRouteRunning)
            {
                bool allComplete = true;
                foreach (var item in activeRoute)
                {
                    if (!item.IsCompleted)
                    {
                        allComplete = false;
                        int count = GetItemCount(item.ItemId);
                        if (count >= item.TargetQuantity)
                        {
                            item.IsCompleted = true;
                            routeChanged = true;
                            
                            // Popup logic!
                            // Find the *new* next target to see if we need a teleport
                            _completedItemForPopup = item.ItemId;
                        }
                    }
                }
                
                if (routeChanged) _configuration.Save();

                if (!allComplete)
                {
                    nextTarget = GetNextRouteTarget();
                    
                    if (nextTarget == null)
                    {
                        // Meaning there are incomplete items, but none are active Timed Nodes
                        _isWaitingForNode = true;
                        
                        // Let's just point them to the chronologically next timed node
                        DateTime eTime = EorzeaTimeHelper.GetEorzeaTime();
                        nextTarget = activeRoute
                            .Where(r => !r.IsCompleted)
                            .Select(r => new { Item = r, Node = Nodes.FirstOrDefault(n => n.ItemId == r.ItemId) })
                            .Where(x => x.Node != null && x.Node.hours != null && x.Node.hours.Count > 0)
                            .Select(x => new { Item = x.Item, Secs = EorzeaTimeHelper.GetRealSecondsLeft(x.Node, eTime) })
                            .OrderBy(x => x.Secs)
                            .Select(x => x.Item)
                            .FirstOrDefault();
                    }
                    else
                    {
                        _isWaitingForNode = false;
                    }
                }
                else
                {
                    _isRouteRunning = false;
                }
            }
            
            if (_completedItemForPopup > 0 && nextTarget != null && nextTarget.ItemId != _completedItemForPopup)
            {
                // check if different map
                bool prevHasLoc = TryGetLocationForItem(_completedItemForPopup, out _, out int pMap, out _, out _, out _, out _, out _);
                bool nextHasLoc = TryGetLocationForItem(nextTarget.ItemId, out _, out int nMap, out _, out _, out _, out _, out _);
                
                if (prevHasLoc && nextHasLoc && pMap != nMap)
                {
                    _popupNextTarget = nextTarget;
                    _popupNextAetheryteId = GetAetheryteIdForMap(nMap);
                    ImGui.OpenPopup("ZoneGuidancePopup");
                }
                _completedItemForPopup = 0; // reset
            }

            if (nextTarget != null)
            {
                if (_activeRouteTargetItemId != nextTarget.ItemId)
                {
                    _activeRouteTargetItemId = nextTarget.ItemId;
                    TryCreateMapLinkForItem(nextTarget.ItemId);
                }

                                
                if (_clientState.TerritoryType != _lastTerritory)
                {
                    _lastTerritory = _clientState.TerritoryType;
                    if (TryGetLocationForItem(nextTarget.ItemId, out int placeNameId, out int targetMapId, out _, out _, out _, out _, out _))
                    {
                        var currentTerritory = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRow(_clientState.TerritoryType);
                        if (currentTerritory.HasValue && (currentTerritory.Value.PlaceName.RowId == (uint)placeNameId || currentTerritory.Value.Map.RowId == (uint)targetMapId))
                        {
                            _shouldOpenMapLink = true;
                            _mapLinkTargetItemId = nextTarget.ItemId;
                        }
                    }
                }

            }
            
            // Draw Popup
            DrawIntegratedZoneGuidance();

            // Render Route
            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Active Route");
            ImGui.Separator();
            
            if (_isWaitingForNode)
            {
                ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), "Waiting for next node to spawn...");
            }
            
            for (int i = 0; i < activeRoute.Count; i++)
            {
                var routeItem = activeRoute[i];
                var dbItem = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow(routeItem.ItemId);
                if (dbItem == null) continue;

                var p = ImGui.GetCursorScreenPos();
                float rowHeight = 44f * PluginUI.AppScale;
                ImGui.GetWindowDrawList().AddRectFilled(p, new Vector2(p.X + ImGui.GetContentRegionAvail().X, p.Y + rowHeight), ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.5f)), 5f);
                
                if (_isRouteRunning && nextTarget != null && nextTarget.ItemId == routeItem.ItemId)
                {
                    ImGui.GetWindowDrawList().AddRect(p, new Vector2(p.X + ImGui.GetContentRegionAvail().X, p.Y + rowHeight), ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0f, 0.8f)), 5f, ImDrawFlags.None, 2f);
                }

                // Icon
                if (dbItem.Value.Icon != 0)
                {
                    var tex = _textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(dbItem.Value.Icon));
                    if (tex != null && tex.GetWrapOrDefault() != null)
                    {
                        ImGui.SetCursorScreenPos(new Vector2(p.X + 6f, p.Y + 6f));
                        ImGui.Image(tex.GetWrapOrDefault().Handle, new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                    }
                }

                string itemName = dbItem.Value.Name.ToString();

                // Name
                ImGui.SetCursorScreenPos(new Vector2(p.X + 46f * PluginUI.AppScale, p.Y + 4f * PluginUI.AppScale));
                if (routeItem.IsCompleted)
                {
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    ImGui.TextColored(new Vector4(0.2f, 1f, 0.2f, 1f), $"{(char)Dalamud.Interface.FontAwesomeIcon.CheckCircle}");
                    ImGui.PopFont();
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), itemName);
                }
                else
                {
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), itemName);
                    
                    bool hasLocation = TryGetLocationForItem(routeItem.ItemId, out int zoneid, out int mapId, out float mapX, out float mapY, out string zoneName, out string coords, out GatheringNode liveNode3);
                    
                    uint aetheryteIdForTp = 0;
                    if (mapId > 0)
                    {
                        aetheryteIdForTp = GetAetheryteIdForMap(mapId);
                    }
                    
                    bool canTeleportDirectly = aetheryteIdForTp != 0;
                    float actionButtonsWidth = canTeleportDirectly ? 105f * PluginUI.AppScale : 70f * PluginUI.AppScale;
                    
                    if (hasLocation)
                    {
                        ImGui.SetCursorScreenPos(new Vector2(p.X + ImGui.GetContentRegionAvail().X - actionButtonsWidth, p.Y + (rowHeight - 30f * PluginUI.AppScale) / 2));
                        ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                        bool clickedMap = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.MapMarkerAlt)}##loc_{routeItem.ItemId}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale));
                        ImGui.PopFont();
                        if (clickedMap)
                        {
                            TryCreateMapLinkForItem(routeItem.ItemId);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            UIHelper.BeginTooltip();
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Show Location on Map");
                            ImGui.Separator();
                            string locText = coords != null ? $"{zoneName} ({coords})" : $"{zoneName}";
                            ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), locText);
                            if (coords == null) {
                                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(Exact coordinates unknown)");
                            }
                            UIHelper.EndTooltip();
                        }
                        
                        if (canTeleportDirectly)
                        {
                            ImGui.SameLine(0, 5f * PluginUI.AppScale);
                            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                            bool clickedTp = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.PlaneDeparture)}##tp_{routeItem.ItemId}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale));
                            ImGui.PopFont();
                            if (clickedTp)
                            {
                                TeleportToAetheryte(aetheryteIdForTp);
                            }
                            DrawTeleportTooltip(aetheryteIdForTp);
                        }
                    }

                    // Progress
                    int count = GetItemCount(routeItem.ItemId);
                    string prog = $"{count} / {routeItem.TargetQuantity}";
                    var progSize = ImGui.CalcTextSize(prog);
                    
                    float progressXOffset = hasLocation ? (actionButtonsWidth + 10f * PluginUI.AppScale) : (44f * PluginUI.AppScale);
                    ImGui.SetCursorScreenPos(new Vector2(p.X + ImGui.GetContentRegionAvail().X - progSize.X - progressXOffset, p.Y + (rowHeight - progSize.Y) / 2));
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), prog);
                }

                // Delete Button
                ImGui.SetCursorScreenPos(new Vector2(p.X + ImGui.GetContentRegionAvail().X - 30f * PluginUI.AppScale, p.Y + (rowHeight - 24f * PluginUI.AppScale) / 2));
                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.Trash)}##del_{routeItem.ItemId}", new Vector2(24f * PluginUI.AppScale, 24f * PluginUI.AppScale)))
                {
                    activeRoute.RemoveAt(i);
                    if (_activeRouteTargetItemId == routeItem.ItemId) _activeRouteTargetItemId = 0;
                    _configuration.Save();
                    i--;
                }
                ImGui.PopFont();
                UIHelper.DrawTooltip("Remove from Route");

                ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + rowHeight + 4f * PluginUI.AppScale));
            }
        }
        
        private void DrawIntegratedZoneGuidance()
        {
            if (!_showIntegratedZoneGuidance) return;
            
            var p = ImGui.GetCursorScreenPos();
            var availW = ImGui.GetContentRegionAvail().X;
            float boxH = 100f * PluginUI.AppScale;
            
            ImGui.GetWindowDrawList().AddRectFilled(p, new System.Numerics.Vector2(p.X + availW, p.Y + boxH), ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.1f, 0.25f, 0.1f, 0.9f)), 8f);
            ImGui.GetWindowDrawList().AddRect(p, new System.Numerics.Vector2(p.X + availW, p.Y + boxH), ImGui.ColorConvertFloat4ToU32(new System.Numerics.Vector4(0.0f, 0.65f, 1.0f, 1f)), 8f, 0, 2f);
            
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(p.X + 15f * PluginUI.AppScale, p.Y + 10f * PluginUI.AppScale));
            
            if (_isStartRouteGuidance)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 0f, 1f), "Starting Route - Next Location:");
            else
                ImGui.TextColored(new System.Numerics.Vector4(0f, 1f, 0f, 1f), "Item Gathered Successfully!");

            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(p.X + 15f * PluginUI.AppScale, p.Y + 35f * PluginUI.AppScale));
            if (_popupNextTarget != null)
            {
                var dbItem = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow(_popupNextTarget.ItemId);
                if (dbItem != null)
                {
                    TryGetLocationForItem(_popupNextTarget.ItemId, out _, out _, out _, out _, out string zName, out _, out _);
                    ImGui.Text($"Next stop: {dbItem.Value.Name.ToString()} in {zName}");
                    
                    ImGui.SetCursorScreenPos(new System.Numerics.Vector2(p.X + 15f * PluginUI.AppScale, p.Y + 60f * PluginUI.AppScale));
                    
                    if (_popupNextAetheryteId > 0)
                    {
                        if (UIHelper.DrawPremiumButton("btn_tp_now", ImGui.GetCursorScreenPos(), new System.Numerics.Vector2(120f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Teleport Now", new System.Numerics.Vector4(0.1f, 0.4f, 0.8f, 1f), new System.Numerics.Vector4(0.2f, 0.5f, 0.9f, 1f), new System.Numerics.Vector4(1,1,1,1), new System.Numerics.Vector4(1,1,1,1)))
                        {
                            TeleportToAetheryte(_popupNextAetheryteId);
                            _showIntegratedZoneGuidance = false;
                        }
                    }
                    else
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1f), "No Aetheryte available.");
                    }
                }
            }
            
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(p.X + availW - 135f * PluginUI.AppScale, p.Y + 60f * PluginUI.AppScale));
            if (UIHelper.DrawPremiumButton("btn_tp_close", ImGui.GetCursorScreenPos(), new System.Numerics.Vector2(120f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Dismiss", new System.Numerics.Vector4(0.3f, 0.3f, 0.3f, 1f), new System.Numerics.Vector4(0.4f, 0.4f, 0.4f, 1f), new System.Numerics.Vector4(1,1,1,1), new System.Numerics.Vector4(1,1,1,1)))
            {
                _showIntegratedZoneGuidance = false;
            }
            
            ImGui.SetCursorScreenPos(new System.Numerics.Vector2(p.X, p.Y + boxH + 10f * PluginUI.AppScale));
        }

        private void DrawFilters()
        {
            ImGui.Text("Search Node or Zone");
            ImGui.InputText("##search", ref _searchQuery, 100);
            
            ImGui.Spacing();
            if (ImGui.Checkbox("Favorites Only", ref _showFavoritesOnly)) { }

            ImGui.Spacing();
            ImGui.Text("Class");
            float btnW2 = (ImGui.GetContentRegionAvail().X - 8f * PluginUI.AppScale) / 2f;
            DrawToggleBtn("Miner", "MIN", _filterClass, btnW2);
            ImGui.SameLine();
            DrawToggleBtn("Botanist", "BTN", _filterClass, btnW2);

            ImGui.Spacing();
            ImGui.Text("Expansion");
            if (ImGui.BeginCombo("##expansion", _filterExpansion))
            {
                string[] expansions = { "All", "Dawntrail", "Endwalker", "Shadowbringers", "Stormblood", "Heavensward", "ARR" };
                foreach (var exp in expansions)
                {
                    bool isSelected = (_filterExpansion == exp);
                    if (ImGui.Selectable(exp, isSelected)) _filterExpansion = exp;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            ImGui.Spacing();
            ImGui.Text("Node Type");
            DrawToggleBtn("Legendary", "Legendary", _filterType);
            DrawToggleBtn("Unspoiled", "Unspoiled", _filterType);
            DrawToggleBtn("Ephemeral", "Ephemeral", _filterType);
        }
        private void DrawToggleBtn(string label, string value, HashSet<string> set, float width = -1f)
        {
            bool active = set.Contains(value);
            
            Vector4 bgCol = active ? new Vector4(0.0f, 0.65f, 1.0f, 1f) : new Vector4(0.12f, 0.12f, 0.14f, 1f);
            Vector4 hoverCol = active ? new Vector4(0.0f, 0.75f, 1.0f, 1f) : new Vector4(0.2f, 0.2f, 0.22f, 1f);
            Vector4 textCol = active ? new Vector4(1f, 1f, 1f, 1f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            
            float w = width > 0 ? width : ImGui.GetContentRegionAvail().X;
            if (UIHelper.DrawPremiumButton("btn_" + label, ImGui.GetCursorScreenPos(), new Vector2(w, 28f * PluginUI.AppScale), label, bgCol, hoverCol, textCol, new Vector4(1,1,1,1)))
            {
                if (active) set.Remove(value);
                else set.Add(value);
            }
        }

        private void DrawRadar(DateTime eTime, int currentHour)
        {
            var filteredNodes = Nodes.Where(n => 
            {
                if (_showFavoritesOnly) return _favorites.Contains(n.id);
                if (_favorites.Contains(n.id)) return true;
                
                if (!_filterClass.Contains(n.type)) return false;
                if (!_filterType.Contains(n.nodeType)) return false;
                if (_filterExpansion != "All" && n.expansion != _filterExpansion) return false;
                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    string sq = _searchQuery.ToLower();
                    if (!n.name.ToLower().Contains(sq) && !n.zone.ToLower().Contains(sq)) return false;
                }
                return true;
            }).ToList();

            var activeNodes = filteredNodes
                .Where(n => EorzeaTimeHelper.IsNodeActive(n, currentHour))
                .Select(n => new { Node = n, ActiveSecsLeft = EorzeaTimeHelper.GetActiveSecondsLeft(n, eTime) })
                .OrderBy(x => x.ActiveSecsLeft)
                .ToList();

            var upNextNodes = filteredNodes
                .Where(n => !EorzeaTimeHelper.IsNodeActive(n, currentHour))
                .Select(n => new { Node = n, RealSecsLeft = EorzeaTimeHelper.GetRealSecondsLeft(n, eTime) })
                .OrderBy(x => x.RealSecsLeft)
                .ToList();

            UIHelper.BeginSmoothChild("RadarScroll", new Vector2(-1, -1), true);

            // Active Now
            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), $"Active Now ({activeNodes.Count})");
            ImGui.Separator();
            if (activeNodes.Count == 0) ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "None");
            foreach (var item in activeNodes)
            {
                int m = (int)Math.Floor(item.ActiveSecsLeft / 60);
                int s = (int)Math.Floor(item.ActiveSecsLeft % 60);
                DrawNodeRow(item.Node, $"{m}m {s}s left", true);
            }
            ImGui.Spacing();

            // < 5m
            var lessThan5 = upNextNodes.Where(x => x.RealSecsLeft <= 300).ToList();
            ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), $"Spawning in < 5m ({lessThan5.Count})");
            ImGui.Separator();
            if (lessThan5.Count == 0) ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "None");
            foreach (var item in lessThan5)
            {
                int m = (int)Math.Floor(item.RealSecsLeft / 60);
                int s = (int)Math.Floor(item.RealSecsLeft % 60);
                DrawNodeRow(item.Node, $"in {m}m {s}s", false);
            }
            ImGui.Spacing();

            // 5 - 15m
            var between5and15 = upNextNodes.Where(x => x.RealSecsLeft > 300 && x.RealSecsLeft <= 900).ToList();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"Spawning in 5 - 15m ({between5and15.Count})");
            ImGui.Separator();
            if (between5and15.Count == 0) ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "None");
            foreach (var item in between5and15)
            {
                int m = (int)Math.Floor(item.RealSecsLeft / 60);
                int s = (int)Math.Floor(item.RealSecsLeft % 60);
                DrawNodeRow(item.Node, $"in {m}m {s}s", false);
            }
            ImGui.Spacing();

            // 15m+
            var moreThan15 = upNextNodes.Where(x => x.RealSecsLeft > 900).ToList();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"Spawning Later ({moreThan15.Count})");
            ImGui.Separator();
            if (moreThan15.Count == 0) ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "None");
            foreach (var item in moreThan15)
            {
                int m = (int)Math.Floor(item.RealSecsLeft / 60);
                int s = (int)Math.Floor(item.RealSecsLeft % 60);
                DrawNodeRow(item.Node, $"in {m}m {s}s", false);
            }

            ImGui.EndChild();
        }

        private void DrawNodeRow(GatheringNode node, string status, bool isActive)
        {
            float padding = 8f * PluginUI.AppScale;
            Vector2 p = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 55f * PluginUI.AppScale;

            // Background
            uint bgCol = isActive ? ImGui.GetColorU32(new Vector4(1f, 0.8f, 0f, 0.05f)) : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.02f));
            if (ImGui.IsMouseHoveringRect(p, new Vector2(p.X + w, p.Y + h)))
                bgCol = isActive ? ImGui.GetColorU32(new Vector4(1f, 0.8f, 0f, 0.1f)) : ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.05f));
                
            ImGui.GetWindowDrawList().AddRectFilled(p, new Vector2(p.X + w, p.Y + h), bgCol, 4f);

            // Icon Image
            ImGui.SetCursorScreenPos(new Vector2(p.X + padding, p.Y + padding));
            if (node.Texture != null && node.Texture.GetWrapOrDefault() != null)
            {
                ImGui.Image(node.Texture.GetWrapOrDefault().Handle, new Vector2(40f * PluginUI.AppScale, 40f * PluginUI.AppScale));
            }
            else
            {
                // Fallback to text if texture missing
                ImGui.SetCursorScreenPos(new Vector2(p.X + padding, p.Y + (h - 15f * PluginUI.AppScale) / 2));
                ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), node.type == "MIN" ? "Miner" : "Botanist");
            }

            uint aetheryteIdForTp = 0;
            if (DataNodesMap.TryGetValue(node.id, out var dn) && dn.map > 0)
            {
                aetheryteIdForTp = GetAetheryteIdForMap(dn.map);
            }
            else if (node.ItemId > 0 && TryGetLocationForItem(node.ItemId, out _, out int mapId, out _, out _, out _, out _, out _))
            {
                if (mapId > 0)
                {
                    aetheryteIdForTp = GetAetheryteIdForMap(mapId);
                }
            }
            bool canTeleportDirectly = aetheryteIdForTp != 0;

            float actionButtonsWidth = canTeleportDirectly ? 70f * PluginUI.AppScale : 35f * PluginUI.AppScale;
            float rightOffset = padding + actionButtonsWidth;

            // Status Text (Right)
            var textSize = ImGui.CalcTextSize(status);
            rightOffset += textSize.X + 10f * PluginUI.AppScale;

            // Favorite Button
            rightOffset += 30f * PluginUI.AppScale + 5f * PluginUI.AppScale;

            // Push ClipRect for Info section
            Vector2 clipMin = new Vector2(p.X, p.Y);
            Vector2 clipMax = new Vector2(p.X + w - rightOffset - 5f * PluginUI.AppScale, p.Y + h);
            ImGui.PushClipRect(clipMin, clipMax, true);

            // Info
            ImGui.SetCursorScreenPos(new Vector2(p.X + padding + 48f * PluginUI.AppScale, p.Y + padding));
            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), node.name);
            if (node.nodeType == "Legendary")
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "★");
            }
            
            ImGui.SetCursorScreenPos(new Vector2(p.X + padding + 48f * PluginUI.AppScale, p.Y + padding + 20f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"{node.type} Lv {node.level}   Slot {node.slot}");
            
            if (_configuration.EnableGatheringPrices && _priceCache.TryGetValue((uint)node.ItemId, out int nodePrice))
            {
                ImGui.SameLine(0, 15f * PluginUI.AppScale);
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), $"{nodePrice:N0} Gil");
            }
            if (!string.IsNullOrEmpty(node.scrips))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), $"• {node.scrips}");
            }
            ImGui.PopClipRect();
            
            // Location Click Area (Map Link)
            ImGui.SetCursorScreenPos(new Vector2(p.X + w - padding - actionButtonsWidth, p.Y + (h - 30f * PluginUI.AppScale) / 2));
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            bool clickedMap = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.MapMarkerAlt)}##map_{node.id}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale));
            ImGui.PopFont();
            if (clickedMap)
            {
                TryCreateMapLink(node);
            }
            if (ImGui.IsItemHovered())
            {
                UIHelper.BeginTooltip();
                ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Show Location on Map");
                ImGui.Separator();
                string locText = !string.IsNullOrEmpty(node.coords) ? $"{node.zone} ({node.coords})" : $"{node.zone}";
                ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), locText);
                if (string.IsNullOrEmpty(node.coords)) {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "(Exact coordinates unknown)");
                }
                UIHelper.EndTooltip();
            }
            
            if (canTeleportDirectly)
            {
                ImGui.SameLine(0, 5f * PluginUI.AppScale);
                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                bool clickedTp = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.PlaneDeparture)}##tp_{node.id}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale));
                ImGui.PopFont();
                if (clickedTp)
                {
                    TeleportToAetheryte(aetheryteIdForTp);
                }
                DrawTeleportTooltip(aetheryteIdForTp);
            }

            // Status Text (Right)
            ImGui.SetCursorScreenPos(new Vector2(p.X + w - padding - actionButtonsWidth - 10f * PluginUI.AppScale - textSize.X, p.Y + (h - textSize.Y) / 2));
            ImGui.TextColored(isActive ? new Vector4(1f, 0.8f, 0f, 1f) : new Vector4(0.6f, 0.6f, 0.6f, 1f), status);

            // Favorite Button
            ImGui.SetCursorScreenPos(new Vector2(p.X + w - rightOffset, p.Y + (h - 30f * PluginUI.AppScale) / 2));
            bool isFav = _favorites.Contains(node.id);
            ImGui.PushStyleColor(ImGuiCol.Text, isFav ? new Vector4(1f, 0.8f, 0f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 1f));
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.Star)}##fav_{node.id}", new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale)))
            {
                if (isFav) _favorites.Remove(node.id);
                else _favorites.Add(node.id);
                _configuration.GatheringFavorites = _favorites.ToList();
                _configuration.Save();
            }
            ImGui.PopFont();
            ImGui.PopStyleColor();

            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h + 4f * PluginUI.AppScale));
        }

        private void TryCreateMapLink(GatheringNode node)
        {
            try
            {
                if (string.IsNullOrEmpty(node.coords)) return;
                string c = node.coords.Replace("(", "").Replace(")", "").Replace("X:", "").Replace("Y:", "");
                var parts = c.Split(',');
                if (parts.Length >= 2)
                {
                    if (float.TryParse(parts[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float y))
                    {
                        var territories = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
                        var cleanZone = node.zone.Replace("'", "").Replace(" ", "");
                        
                        foreach (var territory in territories)
                        {
                            if (territory.Map.RowId == 0) continue;

                            var placeName = territory.PlaceName.Value;
                            var cleanPlace = placeName.Name.ToString().Replace("'", "").Replace(" ", "");
                            
                            if (cleanPlace.Contains(cleanZone, StringComparison.OrdinalIgnoreCase))
                            {
                                var map = territory.Map.Value;
                                var mapLink = new Dalamud.Game.Text.SeStringHandling.Payloads.MapLinkPayload(territory.RowId, map.RowId, x, y);
                                _gameGui.OpenMapWithMapLink(mapLink);
                                _log.Info($"Opened map for {node.zone} at {x}, {y}");
                                return;
                            }
                        }
                        
                        _log.Warning($"Could not find Map for zone string {node.zone}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Failed to create MapLink for node {node.zone}");
            }
        }

        public void Dispose()
        {
        }
        public void DrawWidgetOverlay(System.Numerics.Vector2 winPos, System.Numerics.Vector2 winSize)
        {
            if (!_isRouteRunning || _configuration.GatheringActiveRoute.Count == 0) return;
            
            if (_isWaitingForNode)
            {
                ImGui.SetCursorScreenPos(winPos + new System.Numerics.Vector2(10f, 75f));
                ImGui.TextColored(new System.Numerics.Vector4(0f, 1f, 1f, 1f), "Waiting...");
                return;
            }
            
            var nextTarget = GetNextRouteTarget();
            if (nextTarget != null)
            {
                var dbItem = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow(nextTarget.ItemId);
                if (dbItem != null)
                {
                    string itemName = dbItem.Value.Name.ToString();
                    if (itemName.Length > 12) itemName = itemName.Substring(0, 12) + "..";
                    ImGui.SetCursorScreenPos(winPos + new System.Numerics.Vector2(10f, 65f));
                    ImGui.TextColored(new System.Numerics.Vector4(1f, 1f, 1f, 1f), itemName);
                    
                    int count = GetItemCount(nextTarget.ItemId);
                    ImGui.SetCursorScreenPos(winPos + new System.Numerics.Vector2(10f, 80f));
                    ImGui.TextColored(new System.Numerics.Vector4(0.8f, 0.8f, 0.8f, 1f), $"{count} / {nextTarget.TargetQuantity}");
                    
                    bool hasLocation = TryGetLocationForItem(nextTarget.ItemId, out int zoneid, out int mapId, out float mapX, out float mapY, out string zoneName, out string coords, out GatheringNode liveNode3);
                    uint aetheryteIdForTp = mapId > 0 ? GetAetheryteIdForMap(mapId) : 0;
                    
                    if (aetheryteIdForTp != 0)
                    {
                        ImGui.SetCursorScreenPos(winPos + new System.Numerics.Vector2(165f, 72f));
                        ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0f, 0f, 0f, 0f));
                        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.8f, 0f, 1f));
                        ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                        bool clickedTp = ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.PlaneDeparture)}##mini_tp_{nextTarget.ItemId}", new System.Numerics.Vector2(24f, 24f));
                        ImGui.PopFont();
                        ImGui.PopStyleColor(2);
                        if (clickedTp)
                        {
                            TeleportToAetheryte(aetheryteIdForTp);
                        }
                    }
                }
            }
        }

        private void DrawTeleportTooltip(uint aetheryteIdForTp)
        {
            if (ImGui.IsItemHovered())
            {
                UIHelper.BeginTooltip();
                ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Teleport to Aetheryte");
                
                if (aetheryteIdForTp > 0)
                {
                    var aeth = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>()?.GetRow(aetheryteIdForTp);
                    if (aeth != null && aeth.HasValue)
                    {
                        ImGui.Separator();
                        string aName = aeth.Value.PlaceName.Value.Name.ToString();
                        string zName = aeth.Value.Territory.Value.PlaceName.Value.Name.ToString();
                        
                        if (!string.IsNullOrEmpty(aName))
                        {
                            ImGui.TextColored(new Vector4(0f, 1f, 1f, 1f), aName);
                            if (!string.IsNullOrEmpty(zName))
                            {
                                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), zName);
                            }
                        }
                    }
                }
                
                UIHelper.EndTooltip();
            }
        }

        public static unsafe void TeleportToAetheryte(uint aetheryteId)
        {
            try 
            {
                var telepo = FFXIVClientStructs.FFXIV.Client.Game.UI.Telepo.Instance();
                if (telepo != null)
                {
                    telepo->Teleport(aetheryteId, 0);
                }
            }
            catch (Exception)
            {
            }
        }

        private uint GetAetheryteIdForMap(int mapId)
        {
            try
            {
                var maps = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
                var mapRow = maps?.GetRow((uint)mapId);
                if (!mapRow.HasValue) return 0;

                var territoryId = mapRow.Value.TerritoryType.RowId;
                if (territoryId == 0) return 0;

                var aetherytes = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
                if (aetherytes == null) return 0;

                var territoryAetherytes = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(aetherytes, a => a.IsAetheryte && a.Territory.RowId == territoryId));
                
                foreach (var a in territoryAetherytes)
                {
                    var name = a.PlaceName.Value.Name.ToString();
                    if (string.IsNullOrEmpty(name)) continue;
                    
                    if (name.Contains("Closest") || name.Contains("Nächster") || name.Contains("plus proche") || name.Contains("最寄り") || name == "Ätheryt")
                        continue;
                        
                    return a.RowId;
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to get Aetheryte for map " + mapId);
            }
            return 0;
        }
        
        private void ExecuteLifestreamCommand(string args)
        {
            try 
            {
                var ipc = _pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
                ipc.InvokeAction(args);
                _log.Info($"Executed Lifestream.ExecuteCommand: {args}");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to execute Lifestream command: {ex.Message}");
            }
        }

    }




}

















