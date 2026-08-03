using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin;
using System.IO;

namespace XIVHubCompanion.Apps
{
    public class RouteStop
    {
        [JsonProperty("id")] public string id { get; set; } = string.Empty;
        [JsonProperty("itemId")] public int itemId { get; set; }
        [JsonProperty("itemName")] public string itemName { get; set; } = string.Empty;
        [JsonProperty("itemIcon")] public string itemIcon { get; set; } = string.Empty;
        [JsonProperty("world")] public string world { get; set; } = string.Empty;
        [JsonProperty("dc")] public string dc { get; set; } = string.Empty;
        [JsonProperty("retainer")] public string retainer { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int quantity { get; set; }
        [JsonProperty("pricePerUnit")] public int pricePerUnit { get; set; }
        [JsonProperty("total")] public int total { get; set; }
        [JsonProperty("hq")] public bool? hq { get; set; }
        [JsonProperty("checked")] public bool checkedState { get; set; }
        [JsonProperty("resaleValue")] public int? resaleValue { get; set; }
        [JsonProperty("excessQty")] public int? excessQty { get; set; }
    }

    public class DestinationGroup
    {
        [JsonProperty("dc")] public string dc { get; set; } = string.Empty;
        [JsonProperty("world")] public string world { get; set; } = string.Empty;
        [JsonProperty("stops")] public List<RouteStop> stops { get; set; } = new List<RouteStop>();
        [JsonProperty("totalCost")] public int totalCost { get; set; }
    }

    public class CartItem
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int quantity { get; set; }
        [JsonProperty("hq")] public bool? hq { get; set; }
        [JsonProperty("canBeHq")] public bool? canBeHq { get; set; }
        [JsonProperty("level")] public int? level { get; set; }
        [JsonProperty("ilvl")] public int? ilvl { get; set; }
    }

    public class MarketFavorite
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("level")] public int? level { get; set; }
        [JsonProperty("ilvl")] public int? ilvl { get; set; }
        [JsonProperty("canBeHq")] public bool? canBeHq { get; set; }
    }

    public class Category
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("category")] public int category { get; set; }
    }

    public class CategoryResponse
    {
        [JsonProperty("categories")] public List<Category> categories { get; set; } = new();
    }

    public class CategoryItemsResponse
    {
        [JsonProperty("results")] public List<MarketSearchItem> results { get; set; } = new();
        [JsonProperty("total")] public int total { get; set; }
        [JsonProperty("page")] public int page { get; set; }
        [JsonProperty("limit")] public int limit { get; set; }
        [JsonProperty("totalPages")] public int totalPages { get; set; }
    }

    public class MarketSearchItem
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("level")] public int? level { get; set; }
        [JsonProperty("ilvl")] public int? ilvl { get; set; }
        [JsonProperty("canBeHq")] public bool? canBeHq { get; set; }
    }

    public class MarketListing
    {
        [JsonProperty("pricePerUnit")] public int pricePerUnit { get; set; }
        [JsonProperty("quantity")] public int quantity { get; set; }
        [JsonProperty("hq")] public bool hq { get; set; }
        [JsonProperty("retainerName")] public string retainerName { get; set; } = string.Empty;
        [JsonProperty("total")] public int total { get; set; }
        [JsonProperty("worldName")] public string worldName { get; set; } = string.Empty;
    }

    public class MarketSale
    {
        [JsonProperty("pricePerUnit")] public int pricePerUnit { get; set; }
        [JsonProperty("quantity")] public int quantity { get; set; }
        [JsonProperty("buyerName")] public string buyerName { get; set; } = string.Empty;
        [JsonProperty("hq")] public bool hq { get; set; }
        [JsonProperty("timestamp")] public long timestamp { get; set; }
        [JsonProperty("worldName")] public string worldName { get; set; } = string.Empty;
    }

    public class MarketData
    {
        [JsonProperty("error")] public string error { get; set; }
        [JsonProperty("minPrice")] public int minPrice { get; set; }
        [JsonProperty("minPriceNQ")] public int minPriceNQ { get; set; }
        [JsonProperty("minPriceHQ")] public int minPriceHQ { get; set; }
        [JsonProperty("averagePrice")] public float averagePrice { get; set; }
        [JsonProperty("averagePriceNQ")] public float averagePriceNQ { get; set; }
        [JsonProperty("averagePriceHQ")] public float averagePriceHQ { get; set; }
        [JsonProperty("currentAveragePrice")] public float currentAveragePrice { get; set; }
        [JsonProperty("currentAveragePriceNQ")] public float currentAveragePriceNQ { get; set; }
        [JsonProperty("currentAveragePriceHQ")] public float currentAveragePriceHQ { get; set; }
        [JsonProperty("regularSaleVelocity")] public float regularSaleVelocity { get; set; }
        [JsonProperty("nqSaleVelocity")] public float nqSaleVelocity { get; set; }
        [JsonProperty("hqSaleVelocity")] public float hqSaleVelocity { get; set; }
        [JsonProperty("listings")] public List<MarketListing> listings { get; set; } = new();
        [JsonProperty("recentHistory")] public List<MarketSale> recentHistory { get; set; } = new();
        [JsonProperty("lastUploadTime")] public long lastUploadTime { get; set; }
        [JsonProperty("worldUploadTimes")] public Dictionary<int, long> worldUploadTimes { get; set; } = new();
    }

    public class MarketApp : IApp
    {
        public string Name => "Market";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.Store).ToString(); 
        public bool HasSettings => true;
        public void Update() { } 
        public void DrawSettings() 
        {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(new Vector4(0.13f, 0.77f, 0.36f, 1.0f), "Integration Options");
            ImGui.Dummy(new Vector2(0, 5));
            
            bool enableHover = _configuration.EnableHoverItemFetching;
            if (UIHelper.DrawGarlondSwitchWithText("chk_hover", "Enable Hover Item Fetching", ref enableHover))
            {
                _configuration.EnableHoverItemFetching = enableHover;
                _configuration.Save();
            }
        }

        private readonly IGameGui _gameGui;
        private readonly IAddonLifecycle _addonLifecycle;
        private readonly ITextureProvider _textureProvider;
        private readonly DataSender _sender;
        private readonly IPluginLog _log;
        private readonly Dalamud.Plugin.Services.IObjectTable _objectTable;

        private List<DestinationGroup> _destinations = new List<DestinationGroup>();
        private List<CartItem> _cart = new List<CartItem>();
        private Dictionary<string, MarketFavorite> _marketFavorites = new Dictionary<string, MarketFavorite>();
        
        private Dictionary<uint, ISharedImmediateTexture> _iconCache = new Dictionary<uint, ISharedImmediateTexture>();
        private bool _loggedReflection = false;
        private HashSet<string> _loggedMatches = new HashSet<string>();
        private HashSet<string> _seenTexts = new HashSet<string>();
        private HashSet<string> _activeRetainers = new HashSet<string>();
        
        private bool _hasPulledState = false;
        
        // Routing Settings
        private int _routePriority = 1;
        private int _routeStrategy = 0;
        private int _routeQuality = 0;
        private bool _allowServerTravel = true;
        private bool _allowDcTravel = true;
        private bool _isCalculating = false;
        
        // Search State
        private string _searchQuery = "";
        private List<MarketSearchItem> _searchResults = new List<MarketSearchItem>();
        private bool _isSearching = false;
        
        // Category Explorer State
        private List<Category> _categories = new();
        private Category _selectedCategory = null;
        private List<MarketSearchItem> _categoryItems = new();
        private bool _isLoadingCategories = false;
        private int _categoryPage = 1;
        private bool _categoryHasMore = false;
        
        // Category Filters
        private string _filterMinLevel = "";
        private string _filterMaxLevel = "";
        private string _filterJob = "";
        private int? _prevCategoryGroup = null;

        private bool _isRoutingEngineOpen = false;
        private string _activeCharacterName = string.Empty;

        // Detail View State
        private MarketSearchItem _selectedItem = null;
        private MarketData _marketData = null;
        private bool _isLoadingMarketData = false;
        private bool _detailShowHq = false;
        
        private Dictionary<int, int> _searchQuickAddQty = new();
        private Dictionary<int, float> _itemHoverAlphas = new();

        private IDalamudPluginInterface _pluginInterface;
        private Configuration _configuration;

        public static Action<CartItem> OnAddToCart;

        public MarketApp(IGameGui gameGui, IAddonLifecycle addonLifecycle, ITextureProvider textureProvider, DataSender sender, IPluginLog log, Dalamud.Plugin.Services.IObjectTable objectTable, IDalamudPluginInterface pluginInterface, Configuration configuration)
        {
            _gameGui = gameGui;
            _addonLifecycle = addonLifecycle;
            _textureProvider = textureProvider;
            _sender = sender;
            _log = log;
            _objectTable = objectTable;
            _pluginInterface = pluginInterface;
            _configuration = configuration;

            OnAddToCart = (item) => {
                var existing = _cart.FirstOrDefault(x => x.id == item.id && x.hq == item.hq);
                if (existing != null)
                {
                    existing.quantity += item.quantity;
                }
                else
                {
                    _cart.Add(item);
                }
                PushState();
            };

            _sender.OnServerEventReceived += OnServerEvent;
            
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostDraw, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearch", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearchResultCategory", OnItemSearchResultUpdate);
        }

        private void OnServerEvent(string eventType, string data)
        {
            if (eventType == "marketUpdate")
            {
                try {
                    var token = JToken.Parse(data);
                    if (token is JObject payload)
                    {
                        if (payload["data"] != null && payload["data"] is JObject innerData)
                        {
                            payload = innerData;
                        }

                        if (payload["cart"] != null) _cart = payload["cart"].ToObject<List<CartItem>>() ?? new List<CartItem>();
                        if (payload["marketFavorites"] != null) _marketFavorites = payload["marketFavorites"].ToObject<Dictionary<string, MarketFavorite>>() ?? new Dictionary<string, MarketFavorite>();
                        if (payload["route"] != null) _destinations = payload["route"].ToObject<List<DestinationGroup>>() ?? new List<DestinationGroup>();
                    }
                    UpdateActiveRetainers();
                } catch (Exception ex) {
                    _log.Error(ex, $"Failed to parse marketUpdate. Payload: {data}");
                }
            }
        }
        
        private void PushState()
        {
            if (string.IsNullOrEmpty(_activeCharacterName)) return;

            var payload = new
            {
                name = _activeCharacterName,
                cart = _cart,
                marketFavorites = _marketFavorites,
                route = _destinations
            };
            System.Threading.Tasks.Task.Run(() => _sender.PushMarketStateAsync(payload));
        }

        private async System.Threading.Tasks.Task PullInitialStateAsync()
        {
            try
            {
                var responseJson = await _sender.PullMarketStateAsync(_activeCharacterName);
                if (!string.IsNullOrEmpty(responseJson))
                {
                    var token = JToken.Parse(responseJson);
                    if (token is JObject obj && obj["data"] != null)
                    {
                        var data = obj["data"] as JObject;
                        if (data != null)
                        {
                            if (data["cart"] != null) _cart = data["cart"].ToObject<List<CartItem>>() ?? new List<CartItem>();
                            if (data["marketFavorites"] != null) _marketFavorites = data["marketFavorites"].ToObject<Dictionary<string, MarketFavorite>>() ?? new Dictionary<string, MarketFavorite>();
                            if (data["route"] != null) _destinations = data["route"].ToObject<List<DestinationGroup>>() ?? new List<DestinationGroup>();
                        }
                        UpdateActiveRetainers();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to pull initial state");
            }
        }
        
        private async System.Threading.Tasks.Task PerformSearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            try
            {
                _isSearching = true;
                _searchResults.Clear();
                var responseJson = await _sender.SearchItemsAsync(query);
                if (!string.IsNullOrEmpty(responseJson))
                {
                    var token = JToken.Parse(responseJson);
                    if (token is JObject obj && obj["results"] != null)
                    {
                        _searchResults = obj["results"].ToObject<List<MarketSearchItem>>() ?? new List<MarketSearchItem>();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Search failed");
            }
            finally
            {
                _isSearching = false;
            }
        }

        private async System.Threading.Tasks.Task LoadCategoriesAsync()
        {
            if (_categories.Count > 0) return;
            try
            {
                _isLoadingCategories = true;
                var responseJson = await _sender.FetchCategoriesAsync();
                if (!string.IsNullOrEmpty(responseJson))
                {
                    var response = JsonConvert.DeserializeObject<CategoryResponse>(responseJson);
                    if (response != null && response.categories != null)
                    {
                        _categories = response.categories;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load categories");
            }
            finally
            {
                _isLoadingCategories = false;
            }
        }

        private async System.Threading.Tasks.Task LoadCategoryItemsAsync(Category category, int page)
        {
            try
            {
                _isSearching = true;
                if (page == 1)
                {
                    _categoryItems.Clear();
                    _categoryPage = 1;
                }

                var responseJson = await _sender.FetchCategoryItemsAsync(category.id, page, _filterMinLevel, _filterMaxLevel, _filterJob);
                if (!string.IsNullOrEmpty(responseJson))
                {
                    var response = JsonConvert.DeserializeObject<CategoryItemsResponse>(responseJson);
                    if (response != null && response.results != null)
                    {
                        if (page == 1)
                        {
                            _categoryItems = response.results;
                        }
                        else
                        {
                            _categoryItems.AddRange(response.results);
                        }
                        _categoryPage = page;
                        _categoryHasMore = page < response.totalPages;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load category items");
            }
            finally
            {
                _isSearching = false;
            }
        }

        private static readonly Dictionary<string, string> WORLD_TO_REGION = new() {
            {"Ravana","Oceania"},{"Bismarck","Oceania"},{"Sephirot","Oceania"},{"Sophia","Oceania"},{"Zurvan","Oceania"},{"Materia","Oceania"},
            {"Aegis","Japan"},{"Atomos","Japan"},{"Carbuncle","Japan"},{"Garuda","Japan"},{"Gungnir","Japan"},{"Kujata","Japan"},{"Tonberry","Japan"},{"Typhon","Japan"},{"Elemental","Japan"},
            {"Alexander","Japan"},{"Bahamut","Japan"},{"Durandal","Japan"},{"Fenrir","Japan"},{"Ifrit","Japan"},{"Ridill","Japan"},{"Tiamat","Japan"},{"Ultima","Japan"},{"Gaia","Japan"},
            {"Anima","Japan"},{"Asura","Japan"},{"Chocobo","Japan"},{"Hades","Japan"},{"Ixion","Japan"},{"Masamune","Japan"},{"Pandaemonium","Japan"},{"Titan","Japan"},{"Mana","Japan"},
            {"Belias","Japan"},{"Mandragora","Japan"},{"Ramuh","Japan"},{"Shinryu","Japan"},{"Unicorn","Japan"},{"Valefor","Japan"},{"Yojimbo","Japan"},{"Zeromus","Japan"},{"Meteor","Japan"},
            {"Adamantoise","North-America"},{"Cactuar","North-America"},{"Faerie","North-America"},{"Gilgamesh","North-America"},{"Jenova","North-America"},{"Midgardsormr","North-America"},{"Sargatanas","North-America"},{"Siren","North-America"},{"Aether","North-America"},
            {"Behemoth","North-America"},{"Excalibur","North-America"},{"Exodus","North-America"},{"Famfrit","North-America"},{"Hyperion","North-America"},{"Lamia","North-America"},{"Leviathan","North-America"},{"Ultros","North-America"},{"Primal","North-America"},
            {"Balmung","North-America"},{"Brynhildr","North-America"},{"Coeurl","North-America"},{"Diabolos","North-America"},{"Goblin","North-America"},{"Malboro","North-America"},{"Mateus","North-America"},{"Zalera","North-America"},{"Crystal","North-America"},
            {"Halicarnassus","North-America"},{"Maduin","North-America"},{"Marilith","North-America"},{"Seraph","North-America"},{"Cuchulainn","North-America"},{"Kraken","North-America"},{"Rafflesia","North-America"},{"Golem","North-America"},{"Dynamis","North-America"},
            {"Cerberus","Europe"},{"Louisoix","Europe"},{"Moogle","Europe"},{"Omega","Europe"},{"Phantom","Europe"},{"Ragnarok","Europe"},{"Sagittarius","Europe"},{"Spriggan","Europe"},{"Chaos","Europe"},
            {"Alpha","Europe"},{"Lich","Europe"},{"Odin","Europe"},{"Phoenix","Europe"},{"Raiden","Europe"},{"Shiva","Europe"},{"Twintania","Europe"},{"Zodiark","Europe"},{"Light","Europe"}
        };

        private string _activeScope = null;
        private string _reachableScope = "Europe";
        private int _selectedScopeIndex = 0;
        private readonly string[] _scopeDisplayNames = new[] {
            "Accessible Markets",
            "Europe", "Chaos", "Light",
            "North-America", "Aether", "Primal", "Crystal", "Dynamis",
            "Japan", "Elemental", "Gaia", "Mana", "Meteor",
            "Oceania", "Materia"
        };

        public int GetSelectedItemId()
        {
            return _selectedItem?.id ?? 0;
        }

        public void SelectMarketItem(MarketSearchItem item)
        {
            _selectedItem = item;
            _marketData = null;
            _detailCartQty = 1;
            _detailCartHq = item.canBeHq ?? false;
            
            if (_activeScope == null) 
            {
                try 
                {
                    var homeWorld = (_objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)?.HomeWorld.Value.Name.ToString();
                    if (!string.IsNullOrEmpty(homeWorld))
                    {
                        if (WORLD_TO_REGION.TryGetValue(homeWorld, out var region))
                        {
                            if (region == "Oceania") 
                            {
                                _reachableScope = "Oceania";
                            } 
                            else 
                            {
                                _reachableScope = $"{region},Oceania";
                            }
                        }
                    }
                } 
                catch { }

                _activeScope = _reachableScope;
                _selectedScopeIndex = 0;
            }
            System.Threading.Tasks.Task.Run(() => LoadMarketDataAsync(item.id, _activeScope));
        }

        private async System.Threading.Tasks.Task LoadMarketDataAsync(int itemId, string scope)
        {
            try
            {
                _isLoadingMarketData = true;
                var responseJson = await _sender.FetchMarketListingsAsync(scope, (uint)itemId);
                if (!string.IsNullOrEmpty(responseJson))
                {
                    var token = JToken.Parse(responseJson);
                    if (token is JObject obj && obj["market"] != null)
                    {
                        _marketData = obj["market"].ToObject<MarketData>();
                    }
                    else
                    {
                        _marketData = JsonConvert.DeserializeObject<MarketData>(responseJson);
                    }
                    
                    if (_marketData != null)
                    {
                        if (_marketData.minPrice == 0 && _marketData.listings != null && _marketData.listings.Count > 0)
                            _marketData.minPrice = _marketData.listings.Min(l => l.pricePerUnit);
                            
                        if (_marketData.minPriceNQ == 0 && _marketData.listings != null && _marketData.listings.Any(l => !l.hq))
                            _marketData.minPriceNQ = _marketData.listings.Where(l => !l.hq).Min(l => l.pricePerUnit);
                            
                        if (_marketData.minPriceHQ == 0 && _marketData.listings != null && _marketData.listings.Any(l => l.hq))
                            _marketData.minPriceHQ = _marketData.listings.Where(l => l.hq).Min(l => l.pricePerUnit);
                            
                        if (_marketData.nqSaleVelocity == 0 && _marketData.recentHistory != null)
                        {
                            var nqSales = _marketData.recentHistory.Where(s => !s.hq).ToList();
                            if (nqSales.Count >= 2)
                            {
                                long minTime = nqSales.Min(s => s.timestamp);
                                long maxTime = nqSales.Max(s => s.timestamp);
                                float days = (maxTime - minTime) / 86400f;
                                if (days > 0) _marketData.nqSaleVelocity = nqSales.Count / days;
                            }
                        }
                        
                        if (_marketData.hqSaleVelocity == 0 && _marketData.recentHistory != null)
                        {
                            var hqSales = _marketData.recentHistory.Where(s => s.hq).ToList();
                            if (hqSales.Count >= 2)
                            {
                                long minTime = hqSales.Min(s => s.timestamp);
                                long maxTime = hqSales.Max(s => s.timestamp);
                                float days = (maxTime - minTime) / 86400f;
                                if (days > 0) _marketData.hqSaleVelocity = hqSales.Count / days;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load market data");
            }
            finally
            {
                _isLoadingMarketData = false;
            }
        }

        private void UpdateActiveRetainers()
        {
            _activeRetainers.Clear();
            if (_destinations == null) return;
            foreach (var dest in _destinations)
            {
                if (dest.stops == null) continue;
                foreach (var stop in dest.stops)
                {
                    if (!string.IsNullOrEmpty(stop.retainer))
                    {
                        _activeRetainers.Add(stop.retainer);
                    }
                }
            }
        }

        private unsafe void OnItemSearchResultUpdate(AddonEvent type, AddonArgs args)
        {
            try 
            {
                if (_activeRetainers.Count == 0) return;
                
                var addon = (AtkUnitBase*)args.Addon.Address;
                if (addon == null || addon->UldManager.NodeList == null) return;
                
                TraverseAndHighlight(addon->UldManager, _activeRetainers);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Crash in OnItemSearchResultUpdate");
            }
        }
        
        private unsafe void TraverseAndHighlight(AtkUldManager manager, HashSet<string> retainers)
        {
            if (manager.NodeList == null) return;

            for (int i = 0; i < manager.NodeListCount; i++)
            {
                var current = manager.NodeList[i];
                if (current == null) continue;
                if (current->Type == NodeType.Text)
                {
                    var textNode = (AtkTextNode*)current;
                    var text = textNode->NodeText.ToString();
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (_seenTexts.Add(text))
                        {
                            _log.Info($"[UI-TEXT] {text}");
                        }

                        bool match = false;
                        foreach (var ret in retainers)
                        {
                            if (text.Contains(ret, StringComparison.OrdinalIgnoreCase))
                            {
                                match = true;
                                if (_loggedMatches.Add(ret))
                                {
                                    _log.Info($"SUCCESSFULLY MATCHED RETAINER: {ret} in text: '{text}'");
                                }
                                break;
                            }
                        }

                        if (match)
                        {
                            textNode->TextColor.R = 255; textNode->TextColor.G = 105; textNode->TextColor.B = 180; textNode->TextColor.A = 255;
                            textNode->EdgeColor.R = 255; textNode->EdgeColor.G = 105; textNode->EdgeColor.B = 180; textNode->EdgeColor.A = 255;
                            textNode->AtkResNode.Color.R = 255; textNode->AtkResNode.Color.G = 105; textNode->AtkResNode.Color.B = 180;
                        }
                        else
                        {
                            if (textNode->AtkResNode.Color.R == 255 && textNode->AtkResNode.Color.G == 105 && textNode->AtkResNode.Color.B == 180)
                            {
                                textNode->TextColor.R = 255; textNode->TextColor.G = 255; textNode->TextColor.B = 255; textNode->TextColor.A = 255;
                                textNode->EdgeColor.R = 0; textNode->EdgeColor.G = 0; textNode->EdgeColor.B = 0; textNode->EdgeColor.A = 255;
                                textNode->AtkResNode.Color.R = 255; textNode->AtkResNode.Color.G = 255; textNode->AtkResNode.Color.B = 255;
                            }
                        }
                    }
                }
                else if (current->Type == NodeType.Component || (int)current->Type >= 1000)
                {
                    var compNode = (AtkComponentNode*)current;
                    if (compNode->Component != null)
                    {
                        if (compNode->Component->UldManager.NodeList != null)
                        {
                            TraverseAndHighlight(compNode->Component->UldManager, retainers);
                        }
                    }
                }
            }
        }

        private ISharedImmediateTexture? GetIcon(uint iconId)
        {
            if (_iconCache.TryGetValue(iconId, out var tex)) return tex;
            
            try
            {
                var newTex = _textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                _iconCache[iconId] = newTex;
                return newTex;
            }
            catch (Exception ex) 
            { 
                _log.Error(ex, $"Failed to get icon {iconId}");
            }
            return null;
        }

        public void Draw()
        {
            try
            {
                var name = _objectTable[0]?.Name.ToString();
                if (!string.IsNullOrEmpty(name)) _activeCharacterName = name;
            }
            catch { }

            if (!_hasPulledState && !string.IsNullOrEmpty(_activeCharacterName))
            {
                _hasPulledState = true;
                System.Threading.Tasks.Task.Run(PullInitialStateAsync);
            }

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 12) * PluginUI.AppScale);
            ImGuiWindowFlags childFlags = ImGuiWindowFlags.None;
            if (PluginUI.HideScrollbars) childFlags |= ImGuiWindowFlags.NoScrollbar;
            UIHelper.BeginSmoothChild("MarketPaddedChild", ImGui.GetContentRegionAvail(), false, childFlags);
            
            if (_selectedItem != null)
            {
                DrawItemDetail();
            }
            else
            {
                if (ImGui.BeginTabBar("MarketAppTabs"))
                {
                    if (ImGui.BeginTabItem("Search"))
                    {
                        DrawSearchTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Favorites"))
                    {
                        DrawFavoritesTab();
                        ImGui.EndTabItem();
                    }
                    if (ImGui.BeginTabItem("Cart & Route"))
                    {
                        DrawCartAndRouteTab();
                        ImGui.EndTabItem();
                    }
                    ImGui.EndTabBar();
                }
            }
            
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        
        private void DrawCartAndRouteTab()
        {
            ImGui.Dummy(new Vector2(0, 10) * PluginUI.AppScale);
            
            if (_cart.Count > 0)
            {
                if (UIHelper.DrawGarlondCollapsingHeader("hdr_route_cfg", "Routing Configuration", ref _isRoutingEngineOpen))
                {
                    DrawRoutingEngineUI();
                }
                ImGui.Dummy(new Vector2(0, 10) * PluginUI.AppScale);
                
                string btnText = _destinations.Count > 0 ? "Recalculate Route (Server)" : "Calculate Route (Server)";
                if (_isCalculating) btnText = "Calculating Route...";
                
                if (UIHelper.DrawGarlondCalculateButton("btn_calc", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 60f * PluginUI.AppScale), btnText, _isCalculating))
                {
                    if (!_isCalculating)
                    {
                        string homeWorld = ((_objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)?.HomeWorld.Value.Name.ToString()) ?? "Cerberus";
                        System.Threading.Tasks.Task.Run(() => TriggerCalculateRoute(homeWorld));
                    }
                }
                ImGui.Dummy(new Vector2(0, 10) * PluginUI.AppScale);
            }

            if (_destinations.Count == 0 && _cart.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 1, 0.5f), "No active routes or shopping cart items.");
                return;
            }

            if (_destinations.Count > 0)
            {
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
                Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
                Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                
                ImGui.TextColored(new Vector4(1, 1, 1, 0.7f), "ACTIVE SHOPPING ROUTE");
                if (UIHelper.DrawGarlondButton("btn_clear_route", ImGui.GetCursorScreenPos(), new Vector2(100, 25) * PluginUI.AppScale, "Clear Route", btnBg, btnHover, btnText, btnHoverText))
                {
                    _destinations.Clear();
                    UpdateActiveRetainers();
                    PushState();
                }
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                foreach (var dest in _destinations)
                {
                    DrawDestinationGroup(dest);
                }
            }
            
            if (_cart.Count > 0)
            {
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
                Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
                Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                
                ImGui.Dummy(new Vector2(0, 10) * PluginUI.AppScale);
                ImGui.TextColored(new Vector4(1, 1, 1, 0.7f), "SHOPPING CART");
                if (UIHelper.DrawGarlondButton("btn_clear_cart", ImGui.GetCursorScreenPos(), new Vector2(100, 25) * PluginUI.AppScale, "Clear Cart", btnBg, btnHover, btnText, btnHoverText))
                {
                    _cart.Clear();
                    PushState();
                }
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                
                foreach (var item in _cart.ToList())
                {
                    DrawCartItem(item);
                }
            }
        }
        
        private void DrawFavoritesTab()
        {
            ImGui.Dummy(new Vector2(0, 10) * PluginUI.AppScale);
            if (_marketFavorites.Count == 0)
            {
                ImGui.TextColored(new Vector4(1, 1, 1, 0.5f), "You have no favorites.");
                return;
            }
            
            foreach (var kvp in _marketFavorites.ToList())
            {
                DrawSearchOrFavoriteItem(kvp.Value.id, kvp.Value.name, kvp.Value.icon, kvp.Value.level, kvp.Value.ilvl, true, kvp.Value.canBeHq);
            }
        }
        
        private void DrawSearchTab()
        {
            if (_categories.Count == 0 && !_isLoadingCategories)
            {
                System.Threading.Tasks.Task.Run(LoadCategoriesAsync);
            }

            Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

            var avail = ImGui.GetContentRegionAvail();
            float leftPaneWidth = 350f * PluginUI.AppScale;
            float rightPaneWidth = avail.X - leftPaneWidth - (10f * PluginUI.AppScale);

            // LEFT PANE
            if (UIHelper.BeginSmoothChild("search_left_pane", new Vector2(leftPaneWidth, avail.Y), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                UIHelper.DrawGarlondInputText("##search_input", ImGui.GetCursorScreenPos(), new Vector2(leftPaneWidth - (105 * PluginUI.AppScale), 25 * PluginUI.AppScale), ref _searchQuery, 200);
                ImGui.SameLine(0, 5 * PluginUI.AppScale);

                string searchIcon = ((char)Dalamud.Interface.FontAwesomeIcon.Search).ToString();
                if (_isSearching)
                {
                    ImGui.BeginDisabled();
                    UIHelper.DrawGarlondButton("btn_searching", ImGui.GetCursorScreenPos(), new Vector2(100 * PluginUI.AppScale, 25 * PluginUI.AppScale), $"{searchIcon} Searching...", btnBg, btnBg, btnText, btnText);
                    ImGui.EndDisabled();
                }
                else
                {
                    if (UIHelper.DrawGarlondButton("btn_search", ImGui.GetCursorScreenPos(), new Vector2(100 * PluginUI.AppScale, 25 * PluginUI.AppScale), $"{searchIcon} Search", btnBg, btnHover, btnText, btnHoverText))
                    {
                        System.Threading.Tasks.Task.Run(() => PerformSearchAsync(_searchQuery));
                    }
                }

                ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));

                if (UIHelper.BeginSmoothChild("category_explorer", new Vector2(leftPaneWidth, ImGui.GetContentRegionAvail().Y), false))
                {
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "CATEGORY SEARCH");

                    if (_isLoadingCategories)
                    {
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Loading categories...");
                    }
                    else
                    {
                        var groups = new[]
                        {
                            new { Id = 1, Label = "Main Arm/Off Arm" },
                            new { Id = 2, Label = "Armor" },
                            new { Id = 3, Label = "Items" },
                            new { Id = 4, Label = "Housing" }
                        };

                        foreach (var group in groups)
                        {
                            var cats = _categories.Where(c => c.category == group.Id).ToList();
                            if (cats.Count == 0) continue;

                            float groupWidth = leftPaneWidth - (30f * PluginUI.AppScale);
                            float buttonTotalWidth = (18f + 8f + 4f) * PluginUI.AppScale;
                            int iconsPerRow = (int)(groupWidth / buttonTotalWidth);
                            if (iconsPerRow < 1) iconsPerRow = 1;
                            
                            int rows = (cats.Count + (iconsPerRow - 1)) / iconsPerRow;
                            float buttonTotalHeight = (18f + 8f + 4f) * PluginUI.AppScale;
                            float childHeight = (22f * PluginUI.AppScale) + (rows * buttonTotalHeight);
                            if (group.Id == 1) childHeight += 40f * PluginUI.AppScale;
                            if (group.Id == 2) childHeight += 40f * PluginUI.AppScale;

                            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.06f, 0.06f, 0.08f, 0.7f));
                            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.3f, 0.3f, 0.4f, 0.3f));
                            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
                            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4) * PluginUI.AppScale);
                            if (UIHelper.BeginSmoothChild($"group_{group.Id}", new Vector2(0, childHeight), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                            {
                                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), group.Label);

                                float startX = ImGui.GetCursorScreenPos().X;
                                float rightEdge = startX + ImGui.GetContentRegionAvail().X;
                                
                                for (int i = 0; i < cats.Count; i++)
                                {
                                    var cat = cats[i];
                                    bool isSelected = _selectedCategory?.id == cat.id && _searchResults.Count == 0;
                                    
                                    ImGui.PushID($"cat_{cat.id}");
                                    
                                    uint iconId = 0;
                                    var parts = cat.icon.Split('/');
                                    if (parts.Length > 0)
                                    {
                                        uint.TryParse(parts[parts.Length - 1].Replace(".png", ""), out iconId);
                                    }

                                    Vector2 iconSize = new Vector2(18, 18) * PluginUI.AppScale;
                                    if (iconId != 0)
                                    {
                                        dynamic texWrap = GetIcon(iconId)?.GetWrapOrDefault();
                                        if (texWrap != null)
                                        {
                                            if (isSelected) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.83f, 0.69f, 0.22f, 0.3f));
                                            else ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));

                                            if (ImGui.ImageButton(texWrap.Handle, iconSize))
                                            {
                                                _searchResults.Clear();
                                                _selectedCategory = cat;
                                                System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(cat, 1));
                                            }
                                            
                                            ImGui.PopStyleColor();
                                        }
                                        else
                                        {
                                            if (ImGui.InvisibleButton($"##missing_{cat.id}", iconSize))
                                            {
                                                _searchResults.Clear();
                                                _selectedCategory = cat;
                                                System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(cat, 1));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        if (ImGui.InvisibleButton($"##missing_{cat.id}", iconSize))
                                        {
                                            _searchResults.Clear();
                                            _selectedCategory = cat;
                                            System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(cat, 1));
                                        }
                                    }
                                    
                                    if (ImGui.IsItemHovered())
                                    {
                                        UIHelper.BeginTooltip(); ImGui.TextUnformatted(cat.name); UIHelper.EndTooltip();
                                    }

                                    ImGui.PopID();

                                    float nextX = ImGui.GetItemRectMax().X + (4 * PluginUI.AppScale);
                                    if (i != cats.Count - 1 && nextX + iconSize.X < rightEdge)
                                    {
                                        ImGui.SameLine(0, 4 * PluginUI.AppScale);
                                    }
                                }

                                if (group.Id == 1 || group.Id == 2)
                                {
                                    ImGui.Dummy(new Vector2(0, 3 * PluginUI.AppScale));
                                    ImGui.Separator();
                                    ImGui.Dummy(new Vector2(0, 3 * PluginUI.AppScale));


                                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Lv.");
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(50 * PluginUI.AppScale);
                                    ImGui.InputText("##minLv", ref _filterMinLevel, 4);
                                    if (ImGui.IsItemDeactivatedAfterEdit() && _selectedCategory != null)
                                    {
                                        System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(_selectedCategory, 1));
                                    }

                                    ImGui.SameLine();
                                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "-");
                                    ImGui.SameLine();
                                    ImGui.SetNextItemWidth(50 * PluginUI.AppScale);
                                    ImGui.InputText("##maxLv", ref _filterMaxLevel, 4);
                                    if (ImGui.IsItemDeactivatedAfterEdit() && _selectedCategory != null)
                                    {
                                        System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(_selectedCategory, 1));
                                    }

                                    if (group.Id == 2)
                                    {
                                        ImGui.SameLine(0, 15 * PluginUI.AppScale);
                                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Job");
                                        ImGui.SameLine();
                                        ImGui.SetNextItemWidth(140 * PluginUI.AppScale);
                                        
                                        string displayJob = JOB_NAMES.ContainsKey(_filterJob) ? JOB_NAMES[_filterJob] : "Any Job";
                                        if (ImGui.BeginCombo("##job", displayJob))
                                        {
                                            bool is_any = (_filterJob == "");
                                            if (ImGui.Selectable("Any Job", is_any))
                                            {
                                                _filterJob = "";
                                                if (_selectedCategory != null)
                                                {
                                                    System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(_selectedCategory, 1));
                                                }
                                            }
                                            
                                            foreach (var cat in JOB_CATEGORIES)
                                            {
                                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), cat.Key);
                                                foreach (var job in cat.Value)
                                                {
                                                    bool is_selected = (_filterJob == job);
                                                    if (ImGui.Selectable($"   {job} ({JOB_NAMES[job]})", is_selected))
                                                    {
                                                        _filterJob = job;
                                                        if (_selectedCategory != null)
                                                        {
                                                            System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(_selectedCategory, 1));
                                                        }
                                                    }
                                                    if (is_selected) ImGui.SetItemDefaultFocus();
                                                }
                                            }
                                            ImGui.EndCombo();
                                        }
                                    }
                                }

                                ImGui.EndChild();
                            }
                            ImGui.PopStyleVar(2);
                            ImGui.PopStyleColor(2);
                            ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));
                        }
                    }
                    ImGui.EndChild();
                }
                ImGui.EndChild();
            }

            ImGui.SameLine(0, 5 * PluginUI.AppScale);
            var dividerPos = ImGui.GetCursorScreenPos();
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(dividerPos.X, dividerPos.Y),
                new Vector2(dividerPos.X, dividerPos.Y + ImGui.GetContentRegionAvail().Y),
                ImGui.GetColorU32(ImGuiCol.Separator)
            );
            ImGui.Dummy(new Vector2(1, ImGui.GetContentRegionAvail().Y));
            ImGui.SameLine(0, 5 * PluginUI.AppScale);

            // RIGHT PANE
            if (UIHelper.BeginSmoothChild("search_right_pane", new Vector2(rightPaneWidth, avail.Y), false))
            {
                if (_isSearching)
                {
                    ImGui.TextColored(new Vector4(0.83f, 0.69f, 0.22f, 1.0f), "Loading...");
                }
                else if (_searchResults.Count > 0)
                {
                    foreach (var item in _searchResults)
                    {
                        DrawSearchOrFavoriteItem(item.id, item.name, item.icon, item.level, item.ilvl, false, item.canBeHq);
                    }
                }
                else if (_categoryItems.Count > 0)
                {
                    foreach (var item in _categoryItems)
                    {
                        DrawSearchOrFavoriteItem(item.id, item.name, item.icon, item.level, item.ilvl, false, item.canBeHq);
                    }

                    if (_categoryHasMore)
                    {
                        ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                        if (UIHelper.DrawGarlondButton("btn_load_more", ImGui.GetCursorScreenPos(), new Vector2(rightPaneWidth, 30 * PluginUI.AppScale), "Load More", btnBg, btnHover, btnText, btnHoverText))
                        {
                            if (_selectedCategory != null)
                            {
                                System.Threading.Tasks.Task.Run(() => LoadCategoryItemsAsync(_selectedCategory, _categoryPage + 1));
                            }
                        }
                        ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                    }
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), "Search or select a category to view items");
                }
                ImGui.EndChild();
            }
        }

        private static readonly Dictionary<string, string> JOB_NAMES = new() {
            { "", "Any Job" },
            { "PLD", "Paladin" }, { "WAR", "Warrior" }, { "DRK", "Dark Knight" }, { "GNB", "Gunbreaker" },
            { "WHM", "White Mage" }, { "SCH", "Scholar" }, { "AST", "Astrologian" }, { "SGE", "Sage" },
            { "MNK", "Monk" }, { "DRG", "Dragoon" }, { "NIN", "Ninja" }, { "SAM", "Samurai" }, { "RPR", "Reaper" }, { "VPR", "Viper" },
            { "BRD", "Bard" }, { "MCH", "Machinist" }, { "DNC", "Dancer" },
            { "BLM", "Black Mage" }, { "SMN", "Summoner" }, { "RDM", "Red Mage" }, { "PCT", "Pictomancer" }, { "BLU", "Blue Mage" },
            { "CRP", "Carpenter" }, { "BSM", "Blacksmith" }, { "ARM", "Armorer" }, { "GSM", "Goldsmith" },
            { "LTW", "Leatherworker" }, { "WVR", "Weaver" }, { "ALC", "Alchemist" }, { "CUL", "Culinarian" },
            { "MIN", "Miner" }, { "BTN", "Botanist" }, { "FSH", "Fisher" }
        };

        private static readonly Dictionary<string, string[]> JOB_CATEGORIES = new() {
            { "Tanks", new[] { "PLD", "WAR", "DRK", "GNB" } },
            { "Healers", new[] { "WHM", "SCH", "AST", "SGE" } },
            { "Melee DPS", new[] { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR" } },
            { "Physical Ranged", new[] { "BRD", "MCH", "DNC" } },
            { "Magical Ranged", new[] { "BLM", "SMN", "RDM", "PCT", "BLU" } },
            { "Crafters", new[] { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" } },
            { "Gatherers", new[] { "MIN", "BTN", "FSH" } }
        };

        private static readonly Dictionary<int, string> WORLD_ID_TO_NAME = new() {
            { 21, "Ravana" }, { 22, "Bismarck" }, { 23, "Asura" }, { 24, "Belias" },
            { 28, "Pandaemonium" }, { 29, "Shinryu" }, { 30, "Unicorn" }, { 31, "Yojimbo" },
            { 32, "Zeromus" }, { 33, "Twintania" }, { 34, "Brynhildr" }, { 35, "Famfrit" },
            { 36, "Lich" }, { 37, "Mateus" }, { 39, "Omega" }, { 40, "Jenova" },
            { 41, "Zalera" }, { 42, "Zodiark" }, { 43, "Alexander" }, { 44, "Anima" },
            { 45, "Carbuncle" }, { 46, "Fenrir" }, { 47, "Hades" }, { 48, "Ixion" },
            { 49, "Kujata" }, { 50, "Typhon" }, { 51, "Ultima" }, { 52, "Valefor" },
            { 53, "Exodus" }, { 54, "Faerie" }, { 55, "Lamia" }, { 56, "Phoenix" },
            { 57, "Siren" }, { 58, "Garuda" }, { 59, "Ifrit" }, { 60, "Ramuh" },
            { 61, "Titan" }, { 62, "Diabolos" }, { 63, "Gilgamesh" }, { 64, "Leviathan" },
            { 65, "Midgardsormr" }, { 66, "Odin" }, { 67, "Shiva" }, { 68, "Atomos" },
            { 69, "Bahamut" }, { 70, "Chocobo" }, { 71, "Moogle" }, { 72, "Tonberry" },
            { 73, "Adamantoise" }, { 74, "Coeurl" }, { 75, "Malboro" }, { 76, "Tiamat" },
            { 77, "Ultros" }, { 78, "Behemoth" }, { 79, "Cactuar" }, { 80, "Cerberus" },
            { 81, "Goblin" }, { 82, "Mandragora" }, { 83, "Louisoix" }, { 85, "Spriggan" },
            { 86, "Sephirot" }, { 87, "Sophia" }, { 88, "Zurvan" }, { 90, "Aegis" },
            { 91, "Balmung" }, { 92, "Durandal" }, { 93, "Excalibur" }, { 94, "Gungnir" },
            { 95, "Hyperion" }, { 96, "Masamune" }, { 97, "Ragnarok" }, { 98, "Ridill" },
            { 99, "Sargatanas" }, { 400, "Sagittarius" }, { 401, "Phantom" }, { 402, "Alpha" },
            { 403, "Raiden" }, { 404, "Marilith" }, { 405, "Seraph" }, { 406, "Halicarnassus" },
            { 407, "Maduin" }, { 408, "Cuchulainn" }, { 409, "Kraken" }, { 410, "Rafflesia" },
            { 411, "Golem" }
        };

        private string FormatTimeAgo(long timestampMs)
        {
            if (timestampMs == 0) return "Unknown";
            var diffMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampMs;
            var mins = diffMs / 60000;
            if (mins < 1) return "Just now";
            if (mins < 60) return $"{mins} mins ago";
            var hrs = mins / 60;
            if (hrs < 24) return $"{hrs} hours ago";
            return $"{hrs / 24} days ago";
        }

        private void DrawServerBreakdown()
        {
            if (_marketData.worldUploadTimes == null || _marketData.worldUploadTimes.Count == 0) return;
            
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var servers = _marketData.worldUploadTimes
                .Select(kv => new { Id = kv.Key, Name = WORLD_ID_TO_NAME.ContainsKey(kv.Key) ? WORLD_ID_TO_NAME[kv.Key] : $"World {kv.Key}", Time = kv.Value })
                .OrderBy(s => s.Name)
                .ToList();

            int cols = (int)(w / 140f);
            if (cols < 1) cols = 1;
            
            if (ImGui.BeginTable("market_server_breakdown", cols, ImGuiTableFlags.SizingStretchProp))
            {
                for (int i = 0; i < servers.Count; i++)
                {
                    if (i % cols == 0) ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var s = servers[i];
                    
                    var cellP = ImGui.GetCursorScreenPos();
                    var cellW = ImGui.GetColumnWidth();
                    var cellH = 42f * PluginUI.AppScale;
                    
                    UIHelper.DrawCard(cellP, new Vector2(cellW - 5, cellH), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 4f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

                    var nameSize = ImGui.CalcTextSize(s.Name);
                    var timeText = FormatTimeAgo(s.Time);
                    var timeSize = ImGui.CalcTextSize(timeText);
                    
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4 * PluginUI.AppScale);
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (cellW - 5 - nameSize.X) / 2);
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1), s.Name);
                    
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (cellW - 5 - timeSize.X) / 2);
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), timeText);
                    
                    ImGui.Dummy(new Vector2(0, 4 * PluginUI.AppScale));
                }
                ImGui.EndTable();
            }
        }

        private void DrawItemDetail()
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;

            Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

            // Back Button
            if (UIHelper.DrawGarlondButton("btn_back_detail", p, new Vector2(40, 32) * PluginUI.AppScale, "<", btnBg, btnHover, btnText, btnHoverText))
            {
                _selectedItem = null;
                _marketData = null;
                return;
            }

            ImGui.SameLine(0, 10 * PluginUI.AppScale);

            // Icon
            uint iconId = 0;
            if (!string.IsNullOrEmpty(_selectedItem.icon))
            {
                var parts = _selectedItem.icon.Split('/');
                var filename = parts[parts.Length - 1].Replace(".png", "");
                uint.TryParse(filename, out iconId);
            }
            if (iconId != 0)
            {
                dynamic texWrap = GetIcon(iconId)?.GetWrapOrDefault();
                if (texWrap != null)
                {
                    try { ImGui.Image(texWrap.Handle, new Vector2(32, 32) * PluginUI.AppScale); } 
                    catch (Exception) { }
                    ImGui.SameLine(0, 10 * PluginUI.AppScale);
                }
            }

            // Name and Level
            if (_selectedItem.level.HasValue || _selectedItem.ilvl.HasValue)
            {
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(1, 1, 1, 1), _selectedItem.name);
                
                string lvlText = "";
                if (_selectedItem.level.HasValue && _selectedItem.level.Value > 0) lvlText += $"Lv. {_selectedItem.level.Value}";
                if (_selectedItem.ilvl.HasValue && _selectedItem.ilvl.Value > 0)
                {
                    if (lvlText.Length > 0) lvlText += "  ";
                    lvlText += $"iLv. {_selectedItem.ilvl.Value}";
                }
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), lvlText);
                ImGui.EndGroup();
            }
            else
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(new Vector4(1, 1, 1, 1), _selectedItem.name);
            }

            // Favorite Button on the far right
            bool isFav = _marketFavorites.ContainsKey(_selectedItem.id.ToString());
            float rightEdge = p.X + w;
            ImGui.SameLine(0, 0);
            if (UIHelper.DrawGarlondButton("btn_fav_detail", new Vector2(Math.Max(p.X + 200, rightEdge - 100 * PluginUI.AppScale), p.Y + 4), new Vector2(90, 25) * PluginUI.AppScale, isFav ? "Unfavorite" : "Favorite", btnBg, btnHover, btnText, btnHoverText))
            {
                if (isFav) _marketFavorites.Remove(_selectedItem.id.ToString());
                else _marketFavorites[_selectedItem.id.ToString()] = new MarketFavorite { id = _selectedItem.id, name = _selectedItem.name, icon = _selectedItem.icon, level = _selectedItem.level, ilvl = _selectedItem.ilvl, canBeHq = _selectedItem.canBeHq };
                PushState();
            }

            ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
            
            // Scope Selector Row & External Links
            var scopeP = ImGui.GetCursorScreenPos();
            UIHelper.DrawCard(scopeP, new Vector2(w, 40 * PluginUI.AppScale), new Vector4(0.12f, 0.12f, 0.14f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
            
            ImGui.SetCursorScreenPos(new Vector2(scopeP.X + 10 * PluginUI.AppScale, scopeP.Y + 8 * PluginUI.AppScale));
            ImGui.SetNextItemWidth(180 * PluginUI.AppScale);
            if (ImGui.Combo("##scope_selector", ref _selectedScopeIndex, _scopeDisplayNames, _scopeDisplayNames.Length))
            {
                var selection = _scopeDisplayNames[_selectedScopeIndex];
                if (selection == "Accessible Markets")
                {
                    _activeScope = _reachableScope;
                }
                else
                {
                    _activeScope = selection;
                }
                _isLoadingMarketData = true;
                _marketData = null;
                System.Threading.Tasks.Task.Run(() => LoadMarketDataAsync(_selectedItem.id, _activeScope));
            }

            // External Links on the right
            if (UIHelper.DrawGarlondButton("btn_universalis", new Vector2(scopeP.X + w - 190 * PluginUI.AppScale, scopeP.Y + 8 * PluginUI.AppScale), new Vector2(85, 24) * PluginUI.AppScale, "Universalis", btnBg, btnHover, new Vector4(0.4f, 0.8f, 0.9f, 1.0f), btnHoverText))
            {
                Dalamud.Utility.Util.OpenLink("https://universalis.app/market/" + _selectedItem.id);
            }
            if (UIHelper.DrawGarlondButton("btn_garland", new Vector2(scopeP.X + w - 95 * PluginUI.AppScale, scopeP.Y + 8 * PluginUI.AppScale), new Vector2(85, 24) * PluginUI.AppScale, "GarlandTools", btnBg, btnHover, new Vector4(0.4f, 0.8f, 0.9f, 1.0f), btnHoverText))
            {
                Dalamud.Utility.Util.OpenLink("https://garlandtools.org/db/#item/" + _selectedItem.id);
            }
            
            ImGui.SetCursorScreenPos(new Vector2(scopeP.X, scopeP.Y + 45 * PluginUI.AppScale));
            
            ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));

            if (_isLoadingMarketData)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "Loading market data...");
                return;
            }

            if (_marketData == null)
            {
                ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "Failed to load market data or item has no listings.");
                return;
            }
            
            if (!string.IsNullOrEmpty(_marketData.error))
            {
                ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), _marketData.error);
                return;
            }

            DrawServerBreakdown();
            ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));

            // Add to Cart Ribbon
            var ribbonP = ImGui.GetCursorScreenPos();
            UIHelper.DrawCard(ribbonP, new Vector2(w, 40 * PluginUI.AppScale), new Vector4(0.12f, 0.12f, 0.14f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
            ImGui.SetCursorScreenPos(new Vector2(ribbonP.X + 10, ribbonP.Y + 10));
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Add to Shopping Cart:");
            
            float rightAlign = ribbonP.X + w - 200 * PluginUI.AppScale;
            if (_selectedItem.canBeHq != false) rightAlign -= 60 * PluginUI.AppScale;

            ImGui.SetCursorScreenPos(new Vector2(rightAlign, ribbonP.Y + 8));
            
            ImGui.PushItemWidth(40 * PluginUI.AppScale);
            ImGui.InputInt("##cart_qty", ref _detailCartQty, 0, 0);
            ImGui.PopItemWidth();
            if (_detailCartQty < 1) _detailCartQty = 1;

            if (_selectedItem.canBeHq != false)
            {
                ImGui.SameLine(0, 10 * PluginUI.AppScale);
                ImGui.Checkbox("HQ", ref _detailCartHq);
            }
            
            ImGui.SameLine(0, 10 * PluginUI.AppScale);
            if (UIHelper.DrawGarlondButton("btn_add_cart_detail", ImGui.GetCursorScreenPos(), new Vector2(60, 25) * PluginUI.AppScale, "Add", btnBg, btnHover, btnText, btnHoverText))
            {
                var existing = _cart.FirstOrDefault(c => c.id == _selectedItem.id);
                if (existing != null)
                {
                    existing.quantity += _detailCartQty;
                    existing.hq = _detailCartHq;
                }
                else
                {
                    _cart.Add(new CartItem { id = _selectedItem.id, name = _selectedItem.name, icon = _selectedItem.icon, quantity = _detailCartQty, hq = _detailCartHq, canBeHq = _selectedItem.canBeHq });
                }
                PushState();
            }
            ImGui.SetCursorScreenPos(new Vector2(ribbonP.X, ribbonP.Y + 45 * PluginUI.AppScale));
            
            ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));

            if (_selectedItem.canBeHq != false)
            {
                ImGui.Columns(2, "market_columns", false);
                DrawPricesSummary(true);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawTrendSection(true);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawListingsTable(true);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawSalesTable(true);

                ImGui.NextColumn();

                DrawPricesSummary(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawTrendSection(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawListingsTable(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawSalesTable(false);

                ImGui.Columns(1);
            }
            else
            {
                DrawPricesSummary(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawTrendSection(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawListingsTable(false);
                ImGui.Dummy(new Vector2(0, 10 * PluginUI.AppScale));
                DrawSalesTable(false);
            }
        }

        private int _detailCartQty = 1;
        private bool _detailCartHq = false;

        private void DrawPricesSummary(bool isHq)
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 80f * PluginUI.AppScale;

            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
            
            ImGui.SetCursorScreenPos(new Vector2(p.X + 10, p.Y + 8));
            ImGui.TextColored(new Vector4(0.3f, 0.69f, 0.31f, 1.0f), isHq ? "CHEAPEST HQ" : "CHEAPEST NQ");
            
            ImGui.SetCursorScreenPos(new Vector2(p.X + 10, p.Y + 28));
            var listings = _marketData.listings.Where(l => l.hq == isHq).ToList();
            if (listings.Count > 0)
            {
                var cheapest = listings.OrderBy(l => l.pricePerUnit).First();
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), $"{cheapest.quantity} x {cheapest.pricePerUnit:N0}");
                ImGui.SetCursorScreenPos(new Vector2(p.X + 10, p.Y + 52));
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"Server: ");
                ImGui.SameLine(0, 0);
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), cheapest.worldName ?? "-");
                ImGui.SameLine(0, 5 * PluginUI.AppScale);
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"- Total: {cheapest.quantity * cheapest.pricePerUnit:N0}");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), isHq ? "No HQ listings available." : "No NQ listings available.");
            }

            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
        }

        private void DrawTrendSection(bool isHq)
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 80f * PluginUI.AppScale;

            ImGui.TextColored(new Vector4(1f, 0.41f, 0.71f, 1f), isHq ? "HQ Price history" : "NQ Price history");
            ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));

            var graphP = ImGui.GetCursorScreenPos();
            UIHelper.DrawCard(graphP, new Vector2(w, h), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            var sales = _marketData.recentHistory.Where(s => s.hq == isHq).ToList();
            if (sales.Count >= 2)
            {
                var values = sales.Select(s => (float)s.pricePerUnit).ToArray();
                Array.Reverse(values); // Chronological
                Sparkline.Draw(graphP, graphP + new Vector2(w, h), values, new Vector4(1f, 0.41f, 0.71f, 1f), new Vector4(1f, 0.41f, 0.71f, 0.18f));
            }
            else
            {
                var text = "Not enough data";
                var size = ImGui.CalcTextSize(text);
                ImGui.SetCursorScreenPos(new Vector2(graphP.X + (w - size.X) / 2, graphP.Y + (h - size.Y) / 2));
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), text);
            }

            ImGui.SetCursorScreenPos(new Vector2(p.X, graphP.Y + h));
        }

        private void DrawListingsTable(bool isHq)
        {
            ImGui.TextColored(new Vector4(1f, 0.41f, 0.71f, 1f), isHq ? "HQ Listings" : "NQ Listings");
            ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));
            
            var listings = _marketData.listings.Where(l => l.hq == isHq).Take(50).ToList();
            if (listings.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No listings.");
                return;
            }

            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 30f + listings.Count * 22f; // approximate height
            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.10f, 0.10f, 0.12f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            ImGui.SetCursorScreenPos(new Vector2(p.X + 5, p.Y + 5));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.15f, 0.17f, 1f));
            if (ImGui.BeginTable($"market_listings_table_{(isHq ? "hq" : "nq")}", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp, new Vector2(w - 10, h - 10)))
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableHeadersRow();

                foreach (var l in listings)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.94f, 0.78f, 0.35f, 1f), $"{l.pricePerUnit:N0}");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"x{l.quantity}");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), l.worldName);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), l.retainerName);
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleColor();
            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h + 10));
        }

        private void DrawSalesTable(bool isHq)
        {
            ImGui.TextColored(new Vector4(1f, 0.41f, 0.71f, 1f), isHq ? "HQ Recent sales" : "NQ Recent sales");
            ImGui.Dummy(new Vector2(0, 5 * PluginUI.AppScale));
            
            var sales = _marketData.recentHistory.Where(s => s.hq == isHq).Take(50).ToList();
            if (sales.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No recent sales.");
                return;
            }

            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 30f + sales.Count * 22f; // approximate height
            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.10f, 0.10f, 0.12f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            ImGui.SetCursorScreenPos(new Vector2(p.X + 5, p.Y + 5));
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.15f, 0.15f, 0.17f, 1f));
            if (ImGui.BeginTable($"market_sales_table_{(isHq ? "hq" : "nq")}", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp, new Vector2(w - 10, h - 10)))
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch, 0.25f);
                ImGui.TableSetupColumn("Buyer", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                ImGui.TableHeadersRow();

                foreach (var s in sales)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.94f, 0.78f, 0.35f, 1f), $"{s.pricePerUnit:N0}");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"x{s.quantity}");
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), s.worldName);
                    ImGui.TableNextColumn();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), s.buyerName);
                }
                ImGui.EndTable();
            }
            ImGui.PopStyleColor();
            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h + 10));
        }
        
        private void DrawSearchOrFavoriteItem(int id, string name, string icon, int? level, int? ilvl, bool isFavoriteTab, bool? canBeHq = false)
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 50f;
            
            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            float clickWidth = w / 2;
            ImGui.SetCursorScreenPos(p);
            ImGui.PushID($"item_card_{id}");
            bool clicked = ImGui.InvisibleButton("card", new Vector2(clickWidth, h));
            bool isHovered = ImGui.IsItemHovered();
            
            if (!_itemHoverAlphas.ContainsKey(id)) _itemHoverAlphas[id] = 0f;
            if (isHovered)
            {
                _itemHoverAlphas[id] = Math.Min(1f, _itemHoverAlphas[id] + ImGui.GetIO().DeltaTime * 10f);
            }
            else
            {
                _itemHoverAlphas[id] = Math.Max(0f, _itemHoverAlphas[id] - ImGui.GetIO().DeltaTime * 10f);
            }
            
            if (_itemHoverAlphas[id] > 0f)
            {
                float alpha = _itemHoverAlphas[id] * 0.1f;
                uint colLeft = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, alpha));
                uint colRight = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.0f));
                ImGui.GetWindowDrawList().AddRectFilledMultiColor(p, new Vector2(p.X + clickWidth, p.Y + h), colLeft, colRight, colRight, colLeft);
            }
            if (clicked)
            {
                var item = new MarketSearchItem { id = id, name = name, icon = icon, level = level, ilvl = ilvl, canBeHq = canBeHq };
                SelectMarketItem(item);
            }
            ImGui.PopID();

            uint iconId = 0;
            if (!string.IsNullOrEmpty(icon))
            {
                var parts = icon.Split('/');
                var filename = parts[parts.Length - 1].Replace(".png", "");
                uint.TryParse(filename, out iconId);
            }
            if (iconId != 0)
            {
                dynamic texWrap = GetIcon(iconId)?.GetWrapOrDefault();
                if (texWrap != null)
                {
                    ImGui.SetCursorScreenPos(new Vector2(p.X + 12, p.Y + 9));
                    try { ImGui.Image(texWrap.Handle, new Vector2(32, 32) * PluginUI.AppScale); } 
                    catch (Exception) { }
                }
            }

            if (level.HasValue || ilvl.HasValue)
            {
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 8 * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), name);
                
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 28 * PluginUI.AppScale));
                string lvlText = "";
                if (level.HasValue && level.Value > 0) lvlText += $"Lv. {level.Value}";
                if (ilvl.HasValue && ilvl.Value > 0)
                {
                    if (lvlText.Length > 0) lvlText += "  ";
                    lvlText += $"iLv. {ilvl.Value}";
                }
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), lvlText);
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 16 * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), name);
            }
            
            // Buttons on the right
            bool isFav = _marketFavorites.ContainsKey(id.ToString());
            
            Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            
            float rightEdge = p.X + w;
            
            ImGui.PushID(id);
            if (UIHelper.DrawGarlondButton("fav", new Vector2(Math.Max(p.X + 175, rightEdge - 225), p.Y + 12), new Vector2(75, 25) * PluginUI.AppScale, isFav ? "Unfavorite" : "Favorite", btnBg, btnHover, btnText, btnHoverText))
            {
                if (isFav) _marketFavorites.Remove(id.ToString());
                else _marketFavorites[id.ToString()] = new MarketFavorite { id = id, name = name, icon = icon, level = level, ilvl = ilvl, canBeHq = canBeHq };
                PushState();
            }
            
            if (!_searchQuickAddQty.ContainsKey(id)) _searchQuickAddQty[id] = 1;
            int qty = _searchQuickAddQty[id];
            
            ImGui.SetCursorScreenPos(new Vector2(Math.Max(p.X + 255, rightEdge - 145), p.Y + 14));
            ImGui.PushItemWidth(55 * PluginUI.AppScale);
            if (ImGui.InputInt($"##qty_{id}", ref qty, 0, 0))
            {
                if (qty < 1) qty = 1;
                _searchQuickAddQty[id] = qty;
            }
            ImGui.PopItemWidth();
            
            if (UIHelper.DrawGarlondButton("cart", new Vector2(Math.Max(p.X + 315, rightEdge - 85), p.Y + 12), new Vector2(70, 25) * PluginUI.AppScale, "Add", btnBg, btnHover, btnText, btnHoverText))
            {
                var existing = _cart.FirstOrDefault(c => c.id == id);
                if (existing != null)
                {
                    existing.quantity += qty;
                }
                else
                {
                    _cart.Add(new CartItem { id = id, name = name, icon = icon, quantity = qty, canBeHq = canBeHq, level = level, ilvl = ilvl });
                }
                PushState();
            }
            ImGui.PopID();

            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
            ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
        }

        private void DrawRoutingEngineUI()
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.05f, 0.05f, 0.07f, 0.8f));
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
            UIHelper.BeginSmoothChild("RoutingEngineUI", new Vector2(-1, 150) * PluginUI.AppScale, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

            if (ImGui.BeginTable("routingEngineGrid", 4, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
            {
                ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Strategy", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Travel", ImGuiTableColumnFlags.WidthStretch);
                
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Priority");
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                UIHelper.DrawGarlondRadioButtonWithText("route_pri_0", "Fastest", ref _routePriority, 0);
                UIHelper.DrawGarlondRadioButtonWithText("route_pri_1", "Balanced", ref _routePriority, 1);
                UIHelper.DrawGarlondRadioButtonWithText("route_pri_2", "Cheapest", ref _routePriority, 2);
                ImGui.EndGroup();

                ImGui.TableNextColumn();
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Strategy");
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                UIHelper.DrawGarlondRadioButtonWithText("route_strat_0", "Strict Qty", ref _routeStrategy, 0);
                UIHelper.DrawGarlondRadioButtonWithText("route_strat_1", "Smart Bulk", ref _routeStrategy, 1);
                ImGui.EndGroup();

                ImGui.TableNextColumn();
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Quality");
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                UIHelper.DrawGarlondRadioButtonWithText("route_qual_0", "Keep HQ", ref _routeQuality, 0);
                UIHelper.DrawGarlondRadioButtonWithText("route_qual_1", "Force HQ", ref _routeQuality, 1);
                UIHelper.DrawGarlondRadioButtonWithText("route_qual_2", "Ignore HQ", ref _routeQuality, 2);
                ImGui.EndGroup();

                ImGui.TableNextColumn();
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Travel");
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
                UIHelper.DrawGarlondSwitchWithText("chk_server_travel", "Allow Server Travel", ref _allowServerTravel);
                if (!_allowServerTravel) _allowDcTravel = false;
                
                ImGui.Dummy(new Vector2(0, 2) * PluginUI.AppScale);
                UIHelper.DrawGarlondSwitchWithText("chk_dc_travel", "Allow DC Travel", ref _allowDcTravel);
                ImGui.EndGroup();

                ImGui.EndTable();
            }
            
            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        private async System.Threading.Tasks.Task TriggerCalculateRoute(string homeWorld)
        {
            if (_cart.Count == 0) return;

            try
            {
                _isCalculating = true;
                string priority = _routePriority == 0 ? "fastest" : _routePriority == 1 ? "balanced" : "cheapest";
                string strategy = _routeStrategy == 0 ? "strict" : "bulk";
                string quality = _routeQuality == 0 ? "keep" : _routeQuality == 1 ? "force_hq" : "ignore_hq";
                
                var responseJson = await _sender.CalculateRouteAsync(new {
                    cart = _cart,
                    homeWorld = homeWorld,
                    searchScope = _activeScope,
                    routePriority = priority,
                    routeStrategy = strategy,
                    routeQuality = quality,
                    allowServerTravel = _allowServerTravel,
                    allowDcTravel = _allowDcTravel
                });

                if (!string.IsNullOrEmpty(responseJson))
                {
                    var token = JToken.Parse(responseJson);
                    if (token is JObject obj && obj["finalDestinations"] != null)
                    {
                        var dests = obj["finalDestinations"].ToObject<List<DestinationGroup>>();
                        _destinations = dests?.Where(d => !string.IsNullOrWhiteSpace(d.dc) && !string.IsNullOrWhiteSpace(d.world)).ToList() ?? new List<DestinationGroup>();
                        UpdateActiveRetainers();
                        PushState();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to trigger route calculation: {ex}");
            }
            finally
            {
                _isCalculating = false;
            }
        }

        private void DrawDestinationGroup(DestinationGroup dest)
        {
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.13f, 0.77f, 0.36f, 1.0f), "\uf0ac");
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1.0f), $"{dest.world} [{dest.dc}]");

            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            float rightEdge = p.X + w;
            
            string destTotalText = $"{dest.totalCost:N0} Gil";
            var destTotalSize = ImGui.CalcTextSize(destTotalText);
            
            ImGui.SetCursorScreenPos(new Vector2(rightEdge - destTotalSize.X - 10f, p.Y - 20));
            ImGui.TextColored(new Vector4(0.94f, 0.78f, 0.35f, 1f), destTotalText);
            
            ImGui.SetCursorScreenPos(p);
            ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);

            foreach (var stop in dest.stops)
            {
                DrawRouteStop(stop);
                ImGui.Dummy(new Vector2(0, 5) * PluginUI.AppScale);
            }
        }

        private void DrawRouteStop(RouteStop stop)
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 54f;
            
            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            ImGui.SetCursorScreenPos(new Vector2(p.X + 12, p.Y + 17));
            bool isChecked = stop.checkedState;
            ImGui.PushID(stop.id);
            if (UIHelper.DrawGarlondCheckbox("chk", ImGui.GetCursorScreenPos(), ref isChecked))
            {
                stop.checkedState = isChecked;
                _sender.SendActionAsync(new { action = "TOGGLE_STOP", itemId = stop.itemId, @checked = isChecked });
                PushState();
            }
            ImGui.PopID();
            
            uint iconId = 0;
            if (!string.IsNullOrEmpty(stop.itemIcon))
            {
                var match = System.Text.RegularExpressions.Regex.Match(stop.itemIcon, @"(\d+)\.png");
                if (match.Success)
                {
                    uint.TryParse(match.Groups[1].Value, out iconId);
                }
                else
                {
                    var parts = stop.itemIcon.Split('/');
                    var filename = parts[parts.Length - 1].Replace(".png", "");
                    uint.TryParse(filename, out iconId);
                }
            }
            if (iconId != 0)
            {
                dynamic texWrap = GetIcon(iconId)?.GetWrapOrDefault();
                if (texWrap != null)
                {
                    ImGui.SetCursorScreenPos(new Vector2(p.X + 45, p.Y + 11));
                    try { 
                        ImGui.Image(texWrap.Handle, new Vector2(32, 32) * PluginUI.AppScale); 
                    } 
                    catch (Exception ex) { 
                        if (!_loggedReflection) {
                            _loggedReflection = true;
                            _log.Error(ex, "Failed to draw Image");
                        }
                    }
                }
            }

            ImGui.SetCursorScreenPos(new Vector2(p.X + 85, p.Y + 10));
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), stop.itemName);
            
            if (stop.hq.HasValue && stop.hq.Value)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.13f, 0.83f, 0.93f, 1f), "\uE03C");
            }
            
            ImGui.SetCursorScreenPos(new Vector2(p.X + 85, p.Y + 29));
            ImGui.SetWindowFontScale(0.85f);
            ImGui.TextColored(new Vector4(0.6f, 0.65f, 0.7f, 1f), $"Retainer: {stop.retainer}");
            ImGui.SetWindowFontScale(1.0f);

            string buyText = $"Buy {stop.quantity} @ {stop.pricePerUnit:N0}";
            string totalText = $"{stop.total:N0} Gil";
            
            float rightEdge = p.X + w;
            
            float btnWidth = PluginUI.Scaled(70f);
            float btnX = rightEdge - btnWidth - 10f;
            
            var totalSize = ImGui.CalcTextSize(totalText);
            float totalX = btnX - totalSize.X - 15f;
            
            var buySize = ImGui.CalcTextSize(buyText);
            float buyX = totalX - buySize.X - 25f;
            
            ImGui.SetCursorScreenPos(new Vector2(buyX, p.Y + 18));
            ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), buyText);

            ImGui.SetCursorScreenPos(new Vector2(totalX, p.Y + 18));
            ImGui.TextColored(new Vector4(0.94f, 0.78f, 0.35f, 1f), totalText);

            if (UIHelper.DrawGarlondWarningButton($"btn_soldout_{stop.id}", new Vector2(btnX, p.Y + 16), new Vector2(btnWidth, 22 * PluginUI.AppScale), "Sold Out"))
            {
                // Trigger automatic recalculation
                string homeWorld = ((_objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter)?.HomeWorld.Value.Name.ToString()) ?? "Cerberus";
                System.Threading.Tasks.Task.Run(() => TriggerCalculateRoute(homeWorld));
            }

            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
        }

        private void DrawCartItem(CartItem item)
        {
            var p = ImGui.GetCursorScreenPos();
            var w = ImGui.GetContentRegionAvail().X;
            var h = 50f;
            
            UIHelper.DrawCard(p, new Vector2(w, h), new Vector4(0.12f, 0.12f, 0.13f, 1.0f), 6f, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));

            uint iconId = 0;
            if (!string.IsNullOrEmpty(item.icon))
            {
                var parts = item.icon.Split('/');
                var filename = parts[parts.Length - 1].Replace(".png", "");
                uint.TryParse(filename, out iconId);
            }
            if (iconId != 0)
            {
                dynamic texWrap = GetIcon(iconId)?.GetWrapOrDefault();
                if (texWrap != null)
                {
                    ImGui.SetCursorScreenPos(new Vector2(p.X + 12, p.Y + 9));
                    try { ImGui.Image(texWrap.Handle, new Vector2(32, 32) * PluginUI.AppScale); } 
                    catch (Exception ex) { 
                        if (!_loggedReflection) {
                            _loggedReflection = true;
                            _log.Error(ex, "Failed to draw Image in Cart");
                        }
                    }
                }
            }

            if (item.level.HasValue || item.ilvl.HasValue)
            {
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 8 * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), item.name);
                
                if (item.hq.HasValue && item.hq.Value)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.13f, 0.83f, 0.93f, 1f), "\uE03C");
                }
                
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 28 * PluginUI.AppScale));
                string lvlText = "";
                if (item.level.HasValue && item.level.Value > 0) lvlText += $"Lv. {item.level.Value}";
                if (item.ilvl.HasValue && item.ilvl.Value > 0)
                {
                    if (lvlText.Length > 0) lvlText += "  ";
                    lvlText += $"iLv. {item.ilvl.Value}";
                }
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), lvlText);
            }
            else
            {
                ImGui.SetCursorScreenPos(new Vector2(p.X + 55 * PluginUI.AppScale, p.Y + 16 * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), item.name);
                
                if (item.hq.HasValue && item.hq.Value)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.13f, 0.83f, 0.93f, 1f), "\uE03C");
                }
            }
            
            float rightEdge = p.X + w;
            Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

            ImGui.PushID(item.id);
            
            if (item.canBeHq != false)
            {
                ImGui.SetCursorScreenPos(new Vector2(Math.Max(p.X + 130, rightEdge - 210), p.Y + 16));
                bool isHq = item.hq ?? false;
                if (ImGui.Checkbox("HQ", ref isHq))
                {
                    item.hq = isHq;
                    PushState();
                }
            }
            
            if (UIHelper.DrawGarlondButton("rem", new Vector2(Math.Max(p.X + 200, rightEdge - 145), p.Y + 12), new Vector2(25, 25) * PluginUI.AppScale, "-", btnBg, btnHover, btnText, btnHoverText))
            {
                item.quantity--;
                if (item.quantity <= 0) _cart.Remove(item);
                PushState();
            }
            
            ImGui.SetCursorScreenPos(new Vector2(Math.Max(p.X + 230, rightEdge - 115), p.Y + 14));
            ImGui.PushItemWidth(55 * PluginUI.AppScale);
            int cartQty = item.quantity;
            if (ImGui.InputInt($"##qty_edit_{item.id}", ref cartQty, 0, 0))
            {
                if (cartQty < 1) cartQty = 1;
                if (cartQty != item.quantity)
                {
                    item.quantity = cartQty;
                    PushState();
                }
            }
            ImGui.PopItemWidth();
            
            if (UIHelper.DrawGarlondButton("add", new Vector2(Math.Max(p.X + 290, rightEdge - 55), p.Y + 12), new Vector2(25, 25) * PluginUI.AppScale, "+", btnBg, btnHover, btnText, btnHoverText))
            {
                item.quantity++;
                PushState();
            }
            ImGui.PopID();

            ImGui.SetCursorScreenPos(new Vector2(p.X, p.Y + h));
        }

        public void Dispose()
        {
            _sender.OnServerEventReceived -= OnServerEvent;
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearch", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearchResultCategory", OnItemSearchResultUpdate);
            
            _iconCache.Clear();
        }
    }
}

