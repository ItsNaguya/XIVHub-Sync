using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // ---- shapes shared with the website (market.* sections and /api/market/*) ----------------------------------------
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
        [JsonProperty("hq", NullValueHandling = NullValueHandling.Ignore)] public bool? hq { get; set; }
        [JsonProperty("checked")] public bool checkedState { get; set; }
        [JsonProperty("resaleValue", NullValueHandling = NullValueHandling.Ignore)] public int? resaleValue { get; set; }
        [JsonProperty("excessQty", NullValueHandling = NullValueHandling.Ignore)] public int? excessQty { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    public class DestinationGroup
    {
        [JsonProperty("dc")] public string dc { get; set; } = string.Empty;
        [JsonProperty("world")] public string world { get; set; } = string.Empty;
        [JsonProperty("stops")] public List<RouteStop> stops { get; set; } = new List<RouteStop>();
        [JsonProperty("totalCost")] public int totalCost { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    public class CartItem
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int quantity { get; set; }
        [JsonProperty("hq", NullValueHandling = NullValueHandling.Ignore)] public bool? hq { get; set; }
        [JsonProperty("canBeHq", NullValueHandling = NullValueHandling.Ignore)] public bool? canBeHq { get; set; }
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public int? level { get; set; }
        [JsonProperty("ilvl", NullValueHandling = NullValueHandling.Ignore)] public int? ilvl { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    public class MarketFavorite
    {
        [JsonProperty("id")] public int id { get; set; }
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string icon { get; set; } = string.Empty;
        [JsonProperty("level", NullValueHandling = NullValueHandling.Ignore)] public int? level { get; set; }
        [JsonProperty("ilvl", NullValueHandling = NullValueHandling.Ignore)] public int? ilvl { get; set; }
        [JsonProperty("canBeHq", NullValueHandling = NullValueHandling.Ignore)] public bool? canBeHq { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? extra { get; set; }
    }

    /// <summary>market.lists: saved carts ({ id, name, items }).</summary>
    public class ShoppingList
    {
        [JsonProperty("id")] public string id { get; set; } = string.Empty;
        [JsonProperty("name")] public string name { get; set; } = string.Empty;
        [JsonProperty("items")] public List<CartItem> items { get; set; } = new();
    }

    /// <summary>market.routeConfig (website defaults: balanced, strict, keep, both travels on).</summary>
    public class RouteConfig
    {
        [JsonProperty("priority")] public string priority { get; set; } = "balanced";
        [JsonProperty("strategy")] public string strategy { get; set; } = "strict";
        [JsonProperty("quality")] public string quality { get; set; } = "keep";
        [JsonProperty("serverTravel")] public bool serverTravel { get; set; } = true;
        [JsonProperty("dcTravel")] public bool dcTravel { get; set; } = true;
    }

    /// <summary>market.soldOut: listings the route planner must skip.</summary>
    public class SoldOut
    {
        [JsonProperty("itemId")] public int itemId { get; set; }
        [JsonProperty("retainer")] public string retainer { get; set; } = string.Empty;
        [JsonProperty("world")] public string world { get; set; } = string.Empty;
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
        [JsonProperty("error")] public string? error { get; set; }
        [JsonProperty("worldName")] public string? worldName { get; set; }
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

    /// <summary>
    /// Market on the tablet: search and category browser, item prices (Universalis), favourites, cart, saved lists and
    /// the travel route. Everything lives in the website's market.* sections; the plugin keeps no copy of its own.
    /// </summary>
    public partial class MarketApp : IApp
    {
        public string Name => "Market";
        public string Icon => ((char)FontAwesomeIcon.Store).ToString();
        public bool HasSettings => true;
        public bool LargeText => true;

        public void DrawSettings()
        {
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(Theme.Crystal, "Integration Options");
            ImGui.Dummy(new Vector2(0, 5));
            bool enableHover = _configuration.EnableHoverItemFetching;
            if (UIHelper.DrawPremiumSwitchWithText("chk_hover", "Enable Hover Item Fetching", ref enableHover))
            {
                _configuration.EnableHoverItemFetching = enableHover;
                _configuration.Save();
            }
        }

        private const string SecCart = "market.cart", SecFavs = "market.favorites", SecRoute = "market.route", SecLists = "market.lists",
            SecConfig = "market.routeConfig", SecSoldOut = "market.soldOut", SecTab = "market.tab", SecScope = "market.scope", SecAddHq = "market.addHq";
        private static readonly string[] Tabs = { "search", "favorites", "cart" };

        private readonly IGameGui _gameGui;
        private readonly IAddonLifecycle _addonLifecycle;
        private readonly ITextureProvider _textureProvider;
        private readonly HubClient _sender;
        private readonly IPluginLog _log;
        private readonly IObjectTable _objectTable;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly Configuration _configuration;

        public static Action<CartItem>? OnAddToCart;

        public MarketApp(IGameGui gameGui, IAddonLifecycle addonLifecycle, ITextureProvider textureProvider, HubClient sender, IPluginLog log, IObjectTable objectTable, IDalamudPluginInterface pluginInterface, Configuration configuration)
        {
            _gameGui = gameGui;
            _addonLifecycle = addonLifecycle;
            _textureProvider = textureProvider;
            _sender = sender;
            _log = log;
            _objectTable = objectTable;
            _pluginInterface = pluginInterface;
            _configuration = configuration;

            // Crafting's "buy" sends materials here
            OnAddToCart = item => { SyncIn(); AddToCart(item.id, item.name, item.icon, item.canBeHq, item.level, item.ilvl, item.quantity, item.hq); };

            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostDraw, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearch", OnItemSearchResultUpdate);
            _addonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemSearchResultCategory", OnItemSearchResultUpdate);
        }

        public void Dispose()
        {
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostDraw, "ItemSearchResult", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearch", OnItemSearchResultUpdate);
            _addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemSearchResultCategory", OnItemSearchResultUpdate);
            if (OnAddToCart != null && OnAddToCart.Target == this) OnAddToCart = null;
        }

        // ---- numbers for the Home widget ----------------------------------------------------------------------------
        public int CartLines => _cart.Count;
        public int CartQuantity => _cart.Sum(c => c.quantity);
        public int RouteStops => _route.Sum(d => d.stops.Count);
        public int RouteStopsDone => _route.Sum(d => d.stops.Count(s => s.checkedState));
        public long RouteCost => _route.Sum(d => (long)d.totalCost);

        public int GetSelectedItemId() => _selected?.id ?? 0;

        /// <summary>Opens the item's prices (hover in game, Crafting, rows here).</summary>
        public void SelectMarketItem(MarketSearchItem item)
        {
            _selected = item;
            _data = null;
            _dataError = null;
            _loadedScope = null;
            _detailQty = 1;
        }

        public void Update()
        {
            SyncIn();
            if (_routeResult is { } r)
            {
                _routeResult = null;
                _route = r.dests;
                _unfulfilled = r.unfulfilled;
                WriteRoute();
                _cartRouteView = true;
            }
        }

        public void Draw()
        {
            UpdatePlayer();
            DrawHeader();
            if (_selected != null) { DrawDetail(); return; }
            switch (CurrentTab)
            {
                case 1: DrawFavorites(); break;
                case 2: DrawCart(); break;
                default: DrawSearch(); break;
            }
        }

        // =============================================================================================================
        // Sync with the website. Each section is re-read when HubClient swaps its token (own writes keep the new one).
        // =============================================================================================================
        private JToken? _cartTok, _favTok, _routeTok, _listsTok, _cfgTok, _soldTok;
        private List<CartItem> _cart = new();
        private Dictionary<string, MarketFavorite> _favs = new();
        private List<DestinationGroup> _route = new();
        private List<ShoppingList> _lists = new();
        private RouteConfig _cfg = new();
        private List<SoldOut> _soldOut = new();
        private readonly HashSet<string> _activeRetainers = new(StringComparer.OrdinalIgnoreCase);

        private static T? Read<T>(JToken? t) where T : class
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            try { return t.ToObject<T>(); } catch { return null; }
        }

        private void SyncIn()
        {
            var t = _sender.GetState(SecCart);
            if (!ReferenceEquals(t, _cartTok)) { _cartTok = t; _cart = Read<List<CartItem>>(t)?.Where(c => c != null && c.id > 0).ToList() ?? new(); }
            t = _sender.GetState(SecFavs);
            if (!ReferenceEquals(t, _favTok)) { _favTok = t; _favs = Read<Dictionary<string, MarketFavorite>>(t)?.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value) ?? new(); }
            t = _sender.GetState(SecRoute);
            if (!ReferenceEquals(t, _routeTok))
            {
                _routeTok = t;
                _route = Read<List<DestinationGroup>>(t)?.Where(d => d != null && !string.IsNullOrWhiteSpace(d.world)).ToList() ?? new();
                foreach (var d in _route) d.stops ??= new();
                UpdateActiveRetainers();
            }
            t = _sender.GetState(SecLists);
            if (!ReferenceEquals(t, _listsTok)) { _listsTok = t; _lists = Read<List<ShoppingList>>(t)?.Where(l => l != null).ToList() ?? new(); }
            t = _sender.GetState(SecConfig);
            if (!ReferenceEquals(t, _cfgTok)) { _cfgTok = t; _cfg = Read<RouteConfig>(t) ?? new(); }
            t = _sender.GetState(SecSoldOut);
            if (!ReferenceEquals(t, _soldTok)) { _soldTok = t; _soldOut = Read<List<SoldOut>>(t) ?? new(); }
        }

        private JToken? Write(string section, object value, JToken? old)
        {
            _sender.SetState(section, value);
            return _sender.GetState(section) ?? old;
        }

        private void WriteCart() => _cartTok = Write(SecCart, _cart, _cartTok);
        private void WriteFavs() => _favTok = Write(SecFavs, _favs, _favTok);
        private void WriteRoute() { _routeTok = Write(SecRoute, _route, _routeTok); UpdateActiveRetainers(); }
        private void WriteLists() => _listsTok = Write(SecLists, _lists, _listsTok);
        private void WriteConfig() => _cfgTok = Write(SecConfig, _cfg, _cfgTok);
        private void WriteSoldOut() => _soldTok = Write(SecSoldOut, _soldOut, _soldTok);

        private int CurrentTab
        {
            get
            {
                int i = Array.IndexOf(Tabs, (_sender.GetState(SecTab) as JValue)?.Value as string);
                return i < 0 ? _localTab : i;
            }
        }
        private int _localTab;

        private void SetTab(int i)
        {
            _localTab = i;
            _selected = null;
            _sender.SetState(SecTab, Tabs[i]);
        }

        private bool AddHq => (_sender.GetState(SecAddHq) as JValue)?.Value is bool b ? b : _localAddHq;
        private bool _localAddHq;
        private void SetAddHq(bool v) { _localAddHq = v; _sender.SetState(SecAddHq, v); }

        // same rule as the website's addToMarketCart: one line per item, quantities add up
        private void AddToCart(int id, string name, string icon, bool? canBeHq, int? level, int? ilvl, int qty, bool? hq)
        {
            if (id <= 0 || qty <= 0) return;
            var existing = _cart.FirstOrDefault(c => c.id == id);
            if (existing != null)
            {
                existing.quantity = Math.Min(9999, existing.quantity + qty);
                if (hq.HasValue) existing.hq = hq;
                existing.canBeHq = canBeHq ?? existing.canBeHq;
            }
            else _cart.Add(new CartItem { id = id, name = name, icon = icon, quantity = Math.Min(9999, qty), hq = hq ?? false, canBeHq = canBeHq, level = level, ilvl = ilvl });
            WriteCart();
        }

        private bool IsFav(int id) => _favs.ContainsKey(id.ToString());

        private void ToggleFav(int id, string name, string icon, bool? canBeHq, int? level, int? ilvl)
        {
            var key = id.ToString();
            if (!_favs.Remove(key)) _favs[key] = new MarketFavorite { id = id, name = name, icon = icon, canBeHq = canBeHq, level = level, ilvl = ilvl };
            WriteFavs();
        }

        private void UpdateActiveRetainers()
        {
            _activeRetainers.Clear();
            foreach (var s in _route.SelectMany(d => d.stops))
                if (!string.IsNullOrEmpty(s.retainer) && !s.checkedState) _activeRetainers.Add(s.retainer);
        }

        // =============================================================================================================
        // Character and market scope (same options as the website's scope picker)
        // =============================================================================================================
        private string _homeWorld = "", _homeDc = "";
        private List<ScopeOpt>? _scopeOpts;
        private string _scopeKey = "";

        private record ScopeOpt(string Value, string Label, string Short, int Group);

        private static readonly (string Region, string[] Dcs)[] RegionDcs =
        {
            ("Europe", new[] { "Chaos", "Light" }),
            ("North-America", new[] { "Aether", "Crystal", "Dynamis", "Primal" }),
            ("Japan", new[] { "Elemental", "Gaia", "Mana", "Meteor" }),
            ("Oceania", new[] { "Materia" }),
        };
        private static string RegionLabel(string r) => r == "North-America" ? "North America" : r;
        private static string? RegionOfDc(string dc) => RegionDcs.FirstOrDefault(r => r.Dcs.Contains(dc)).Region;
        private static string? Reachable(string dc) => RegionOfDc(dc) is { } r ? (r == "Oceania" ? "Oceania" : $"{r},Oceania") : null;

        private void UpdatePlayer()
        {
            try
            {
                if (_objectTable[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc)
                {
                    var hw = pc.HomeWorld.Value;
                    _homeWorld = hw.Name.ToString();
                    _homeDc = hw.DataCenter.Value.Name.ToString();
                }
            }
            catch { }
        }

        private string Scope
        {
            get
            {
                if ((_sender.GetState(SecScope) as JValue)?.Value is string s && s.Length > 0) return s;
                if (_localScope.Length > 0) return _localScope;
                return Reachable(_homeDc) ?? (_homeWorld.Length > 0 ? _homeWorld : "Europe");
            }
        }
        private string _localScope = "";
        private void SetScope(string s) { _localScope = s; _sender.SetState(SecScope, s); }

        private List<ScopeOpt> ScopeOptions()
        {
            var key = _homeWorld + "|" + _homeDc;
            if (_scopeOpts != null && key == _scopeKey) return _scopeOpts;
            var o = new List<ScopeOpt>();
            var reach = Reachable(_homeDc);
            var region = RegionOfDc(_homeDc);
            if (reach != null) o.Add(new(reach, "Reachable markets", "Reachable", 0));
            if (_homeWorld.Length > 0) o.Add(new(_homeWorld, $"Home world ({_homeWorld})", _homeWorld, 0));
            if (_homeDc.Length > 0) o.Add(new(_homeDc, $"Data center ({_homeDc})", _homeDc, 0));
            foreach (var (r, dcs) in RegionDcs)
                if (region == null || r == region)
                    foreach (var d in dcs) if (d != _homeDc) o.Add(new(d, $"{d} (data center)", d, 1));
            if (region != null && region != "Oceania" && _homeDc != "Materia") o.Add(new("Materia", "Materia (data center)", "Materia", 1));
            foreach (var (r, _) in RegionDcs) o.Add(new(r, $"{RegionLabel(r)} (region)", RegionLabel(r), 2));
            var seen = new HashSet<string>();
            _scopeOpts = o.Where(x => seen.Add(x.Value)).ToList();
            _scopeKey = key;
            return _scopeOpts;
        }

        private string ScopeShort(string scope)
            => ScopeOptions().FirstOrDefault(o => o.Value == scope)?.Short ?? string.Join(" + ", scope.Split(',').Select(RegionLabel));

        // =============================================================================================================
        // Header: tabs + scope picker
        // =============================================================================================================
        private void DrawHeader()
        {
            var p = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X, h = S(34), gap = S(8);
            bool oneRow = w >= S(640);
            float scopeW = oneRow ? Math.Clamp(w * 0.3f, S(200), S(300)) : w;
            float tabsW = oneRow ? w - scopeW - gap : w;
            float bw = (tabsW - gap * 2) / 3;
            int tab = _selected != null ? -1 : CurrentTab;
            string[] labels = { "Search", _favs.Count > 0 ? $"Favourites ({_favs.Count})" : "Favourites", _cart.Count > 0 ? $"Cart ({_cart.Count})" : "Cart" };
            FontAwesomeIcon[] icons = { FontAwesomeIcon.Search, FontAwesomeIcon.Star, FontAwesomeIcon.ShoppingCart };
            for (int i = 0; i < 3; i++)
                if (Ui.Button("mk_tab" + i, labels[i], p + new Vector2(i * (bw + gap), 0), new Vector2(bw, h), i == tab ? Kind.Primary : Kind.Ghost, icons[i]) && i != tab)
                    SetTab(i);

            var sp = oneRow ? p + new Vector2(tabsW + gap, (h - FrameH) / 2) : p + new Vector2(0, h + S(8));
            DrawScopeCombo(sp, scopeW);
            Advance(p, (oneRow ? h : h + S(8) + FrameH) + S(10));
        }

        private void DrawScopeCombo(Vector2 pos, float width)
        {
            var scope = Scope;
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo("##mk_scope", "Prices: " + ScopeShort(scope)))
            {
                string[] groups = { "Yours", "Data centers", "Regions" };
                var opts = ScopeOptions();
                for (int g = 0; g < 3; g++)
                {
                    var list = opts.Where(o => o.Group == g).ToList();
                    if (list.Count == 0) continue;
                    ImGui.TextDisabled(groups[g]);
                    foreach (var o in list)
                        if (ImGui.Selectable("  " + o.Label + "##sc" + o.Value, o.Value == scope)) SetScope(o.Value);
                }
                ImGui.EndCombo();
            }
            PopFrame();
            Ui.Tooltip("Which market board prices come from");
        }

        // =============================================================================================================
        // Network
        // =============================================================================================================
        private List<MarketSearchItem> _searchResults = new();
        private bool _isSearching, _searched;
        private string _searchError = "";

        private void RunSearch(string query)
        {
            query = query.Trim();
            if (query.Length == 0 || _isSearching) return;
            _isSearching = true;
            _searchError = "";
            _showResults = 1;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.SearchItemsAsync(query);
                    if (json == null) { _searchError = "The search did not answer. Try again in a moment."; _searchResults = new(); return; }
                    _searchResults = (JToken.Parse(json)["results"] as JArray)?.ToObject<List<MarketSearchItem>>() ?? new();
                }
                catch (Exception ex) { _log.Error(ex, "Market search failed"); _searchError = "The search failed."; }
                finally { _isSearching = false; _searched = true; }
            });
        }

        private List<Category> _categories = new();
        private bool _loadingCategories, _categoriesTried;

        private void EnsureCategories()
        {
            if (_categoriesTried) return;
            _categoriesTried = true;
            _loadingCategories = true;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.FetchCategoriesAsync();
                    if (!string.IsNullOrEmpty(json)) _categories = JsonConvert.DeserializeObject<CategoryResponse>(json)?.categories ?? new();
                }
                catch (Exception ex) { _log.Error(ex, "Failed to load market categories"); }
                finally { _loadingCategories = false; }
            });
        }

        private Category? _selectedCategory;
        private List<MarketSearchItem> _categoryItems = new();
        private int _categoryPage = 1, _categoryTotal, _categoryReq;
        private bool _categoryHasMore, _loadingCategory;
        private string _filterMinLevel = "", _filterMaxLevel = "", _filterJob = "";

        private void LoadCategory(Category cat, int page)
        {
            _selectedCategory = cat;
            _showResults = 2;
            int req = ++_categoryReq;
            _loadingCategory = true;
            string min = _filterMinLevel, max = _filterMaxLevel, job = _filterJob;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.FetchCategoryItemsAsync(cat.id, page, min, max, job);
                    if (req != _categoryReq) return;
                    var r = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<CategoryItemsResponse>(json);
                    if (r?.results == null) { if (page == 1) _categoryItems = new(); return; }
                    _categoryItems = page == 1 ? r.results : _categoryItems.Concat(r.results).ToList();
                    _categoryPage = page;
                    _categoryTotal = r.total;
                    _categoryHasMore = page < r.totalPages;
                }
                catch (Exception ex) { _log.Error(ex, "Failed to load category items"); }
                finally { if (req == _categoryReq) _loadingCategory = false; }
            });
        }

        // item detail (Universalis through /api/market/listings)
        private MarketSearchItem? _selected;
        private MarketData? _data;
        private string? _dataError, _loadedScope;
        private bool _loadingData;
        private int _dataReq;

        private void EnsureDetailLoaded()
        {
            if (_selected == null) return;
            var scope = Scope;
            if (scope == _loadedScope) return;
            _loadedScope = scope;
            LoadData(_selected.id, scope);
        }

        private void LoadData(int itemId, string scope)
        {
            int req = ++_dataReq;
            _loadingData = true;
            _dataError = null;
            Task.Run(async () =>
            {
                MarketData? data = null;
                string? error = null;
                try
                {
                    var json = await _sender.FetchMarketListingsAsync(scope, (uint)itemId);
                    if (string.IsNullOrEmpty(json)) error = "Universalis did not answer. Try again in a moment.";
                    else
                    {
                        var tok = JToken.Parse(json);
                        data = (tok["market"] is JObject m ? m : tok).ToObject<MarketData>();
                        if (data == null) error = "No market data for this item.";
                        else if (!string.IsNullOrEmpty(data.error) && data.listings.Count == 0 && data.recentHistory.Count == 0) { error = data.error; data = null; }
                        else FillGaps(data);
                    }
                }
                catch (Exception ex) { _log.Error(ex, "Failed to load market data"); error = "The prices could not be read."; }
                if (req != _dataReq) return;
                _data = data;
                _dataError = error;
                _loadingData = false;
            });
        }

        private static void FillGaps(MarketData d)
        {
            d.listings ??= new();
            d.recentHistory ??= new();
            d.worldUploadTimes ??= new();
            if (d.minPrice == 0 && d.listings.Count > 0) d.minPrice = d.listings.Min(l => l.pricePerUnit);
            if (d.minPriceNQ == 0 && d.listings.Any(l => !l.hq)) d.minPriceNQ = d.listings.Where(l => !l.hq).Min(l => l.pricePerUnit);
            if (d.minPriceHQ == 0 && d.listings.Any(l => l.hq)) d.minPriceHQ = d.listings.Where(l => l.hq).Min(l => l.pricePerUnit);
            float Velocity(IEnumerable<MarketSale> s)
            {
                var l = s.ToList();
                if (l.Count < 2) return 0;
                float days = (l.Max(x => x.timestamp) - l.Min(x => x.timestamp)) / 86400f;
                return days > 0 ? l.Count / days : 0;
            }
            if (d.nqSaleVelocity == 0) d.nqSaleVelocity = Velocity(d.recentHistory.Where(s => !s.hq));
            if (d.hqSaleVelocity == 0) d.hqSaleVelocity = Velocity(d.recentHistory.Where(s => s.hq));
            if (d.regularSaleVelocity == 0) d.regularSaleVelocity = d.nqSaleVelocity + d.hqSaleVelocity;
        }

        // route planning (/api/market/calculate), result applied on the UI thread in Update()
        private bool _calculating;
        private string? _routeError;
        private List<(CartItem item, string reason)> _unfulfilled = new();
        private volatile RouteResult? _routeResult;
        private sealed record RouteResult(List<DestinationGroup> dests, List<(CartItem item, string reason)> unfulfilled);

        private void PlanRoute(List<SoldOut>? blacklist = null)
        {
            if (_cart.Count == 0 || _calculating) return;
            if (_homeWorld.Length == 0) { _routeError = "Your character's home world is not known yet, so no route can be planned."; return; }
            var preserved = _cartRouteView ? _route.SelectMany(d => d.stops).Where(s => s.checkedState).ToList() : new List<RouteStop>();
            var payload = new
            {
                cart = _cart.ToList(),
                homeWorld = _homeWorld,
                searchScope = Scope,
                routePriority = _cfg.priority,
                routeStrategy = _cfg.strategy,
                routeQuality = _cfg.quality,
                allowServerTravel = _cfg.serverTravel,
                allowDcTravel = _cfg.serverTravel && _cfg.dcTravel,
                preservedStops = preserved,
                blacklistedListings = blacklist ?? _soldOut,
            };
            _calculating = true;
            _routeError = null;
            _unfulfilled = new();
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Post("/api/market/calculate", payload, okOnly: false);
                    var obj = string.IsNullOrEmpty(json) ? null : JToken.Parse(json) as JObject;
                    if (obj?["finalDestinations"] is JArray fd)
                    {
                        var dests = fd.ToObject<List<DestinationGroup>>()?.Where(d => !string.IsNullOrWhiteSpace(d.dc) && !string.IsNullOrWhiteSpace(d.world)).ToList() ?? new();
                        var unf = (obj["unfulfilledItems"] as JArray)?.OfType<JObject>()
                            .Select(u => (u["item"]?.ToObject<CartItem>() ?? new CartItem(), u["reason"]?.ToString() ?? "")).ToList() ?? new();
                        _routeResult = new RouteResult(dests, unf);
                    }
                    else _routeError = obj?["error"]?.ToString() is { Length: > 0 } e ? e : "The route could not be planned.";
                }
                catch (Exception ex) { _log.Error(ex, "Route calculation failed"); _routeError = "The route could not be planned."; }
                finally { _calculating = false; }
            });
        }

        // =============================================================================================================
        // Retainer highlight on the in-game market board (retainers of open route stops turn pink)
        // =============================================================================================================
        private readonly HashSet<string> _loggedMatches = new();

        private unsafe void OnItemSearchResultUpdate(AddonEvent type, AddonArgs args)
        {
            try
            {
                if (_activeRetainers.Count == 0) return;
                var addon = (AtkUnitBase*)args.Addon.Address;
                if (addon == null || addon->UldManager.NodeList == null) return;
                TraverseAndHighlight(addon->UldManager);
            }
            catch (Exception ex) { _log.Error(ex, "Crash in OnItemSearchResultUpdate"); }
        }

        private unsafe void TraverseAndHighlight(AtkUldManager manager)
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
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var match = _activeRetainers.FirstOrDefault(r => text.Contains(r, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        if (_loggedMatches.Add(match)) _log.Debug($"[Market] highlighted retainer {match}");
                        textNode->TextColor.R = 255; textNode->TextColor.G = 105; textNode->TextColor.B = 180; textNode->TextColor.A = 255;
                        textNode->EdgeColor.R = 255; textNode->EdgeColor.G = 105; textNode->EdgeColor.B = 180; textNode->EdgeColor.A = 255;
                        textNode->AtkResNode.Color.R = 255; textNode->AtkResNode.Color.G = 105; textNode->AtkResNode.Color.B = 180;
                    }
                    else if (textNode->AtkResNode.Color.R == 255 && textNode->AtkResNode.Color.G == 105 && textNode->AtkResNode.Color.B == 180)
                    {
                        textNode->TextColor.R = 255; textNode->TextColor.G = 255; textNode->TextColor.B = 255; textNode->TextColor.A = 255;
                        textNode->EdgeColor.R = 0; textNode->EdgeColor.G = 0; textNode->EdgeColor.B = 0; textNode->EdgeColor.A = 255;
                        textNode->AtkResNode.Color.R = 255; textNode->AtkResNode.Color.G = 255; textNode->AtkResNode.Color.B = 255;
                    }
                }
                else if (current->Type == NodeType.Component || (int)current->Type >= 1000)
                {
                    var compNode = (AtkComponentNode*)current;
                    if (compNode->Component != null && compNode->Component->UldManager.NodeList != null)
                        TraverseAndHighlight(compNode->Component->UldManager);
                }
            }
        }

        // =============================================================================================================
        // Small tablet widgets (same look as Crafting's)
        // =============================================================================================================
        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float FrameH => ImGui.GetFontSize() + S(7) * 2;

        private static void Advance(Vector2 p, float h)
        {
            ImGui.SetCursorScreenPos(p);
            ImGui.Dummy(new Vector2(S(1), Math.Max(1, h)));
        }

        private static void PushFrame()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(9, 7));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(6));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Night1.With(0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.CrystalLine);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.Text3);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Night1.With(0.98f));
            ImGui.PushStyleColor(ImGuiCol.Header, Ui.Accent.With(0.22f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Ui.Accent.With(0.3f));
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Night3);
        }

        private static void PopFrame()
        {
            ImGui.PopStyleColor(11);
            ImGui.PopStyleVar(4);
        }

        private static bool Input(string id, ref string value, string hint, float width, Vector2 pos, int max = 100, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
        {
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            bool r = ImGui.InputTextWithHint(id, hint, ref value, max, flags);
            PopFrame();
            return r;
        }

        private static bool Check(string id, string label, ref bool value, Vector2 pos, bool enabled = true)
        {
            float box = S(18);
            var size = new Vector2(box + S(8) + Ui.TextSize(label).X, Math.Max(box, LH));
            bool clicked = Ui.Hit(id, pos, size, out float h) && enabled;
            if (!enabled) h = 0;
            var dl = Ui.Dl;
            var b = pos + new Vector2(0, (size.Y - box) / 2);
            float a = enabled ? 1f : 0.4f;
            dl.AddRectFilled(b, b + new Vector2(box, box), (value ? Ui.Accent.With(0.25f + 0.15f * h) : Theme.Night1.With(0.9f)).U32(), S(4));
            dl.AddRect(b, b + new Vector2(box, box), (value ? Ui.Accent.With(a) : Theme.Text3.With((0.5f + 0.4f * h) * a)).U32(), S(4), 0, S(1));
            if (value) Ui.IconCentered(dl, b + new Vector2(box, box) / 2, FontAwesomeIcon.Check.ToIconString(), Ui.Accent.With(a), 0.7f);
            Ui.Text(dl, pos + new Vector2(box + S(8), (size.Y - LH) / 2), label, UIHelper.LerpColor(Theme.Text2, Theme.Text, h).With(a));
            if (clicked) value = !value;
            return clicked;
        }

        private static float CheckW(string label) => S(26) + Ui.TextSize(label).X;

        private static bool ChipToggle(string id, string text, bool active, ref Vector2 at, float left, float right, float scale = 0.85f)
        {
            var size = Ui.TextSize(text, scale) + S(16, 8);
            if (at.X + size.X > right && at.X > left) at = new Vector2(left, at.Y + size.Y + S(6));
            bool clicked = Ui.Hit(id, at, size, out float h);
            var dl = Ui.Dl;
            var col = active ? Ui.Accent : Theme.Text2;
            dl.AddRectFilled(at, at + size, (active ? col.With(0.2f + 0.1f * h) : Theme.Night1.With(0.7f + 0.2f * h)).U32(), size.Y / 2);
            dl.AddRect(at, at + size, col.With(active ? 0.7f : 0.2f + 0.3f * h).U32(), size.Y / 2, 0, S(1));
            Ui.Text(dl, at + S(8, 4), text, active ? Theme.Text : UIHelper.LerpColor(Theme.Text2, Theme.Text, h), scale);
            at.X += size.X + S(6);
            return clicked;
        }

        private static float ChipH(float scale = 0.85f) => Ui.TextSize("Ag", scale).Y + S(8);

        /// <summary>[-] n [+]; returns true when the value changed. Width is StepperW.</summary>
        private static bool Stepper(string id, ref int value, Vector2 pos, int min = 1, int max = 9999)
        {
            float b = S(26);
            bool changed = false;
            if (Ui.IconButton(id + "_m", FontAwesomeIcon.Minus, pos, b) && value > min) { value--; changed = true; }
            var txt = value.ToString();
            var ts = Ui.TextSize(txt);
            Ui.Dl.AddRectFilled(pos + new Vector2(b + S(2), 0), pos + new Vector2(b + S(52), b), Theme.Night1.With(0.8f).U32(), S(4));
            Ui.Text(Ui.Dl, pos + new Vector2(b + (S(54) - ts.X) / 2, (b - ts.Y) / 2), txt, Theme.Text);
            if (Ui.IconButton(id + "_p", FontAwesomeIcon.Plus, pos + new Vector2(b + S(54), 0), b) && value < max) { value++; changed = true; }
            // right-click on the number types an exact amount
            ImGui.SetCursorScreenPos(pos + new Vector2(b + S(2), 0));
            ImGui.InvisibleButton(id + "_n", new Vector2(S(50), b));
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered()) UIHelper.DrawTooltip("Right-click to type an amount");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _typeBuf = value.ToString(); ImGui.OpenPopup(id + "_pop"); }
            if (ImGui.BeginPopup(id + "_pop"))
            {
                ImGui.SetNextItemWidth(S(90));
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                if (ImGui.InputText("##amt", ref _typeBuf, 5, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    if (int.TryParse(_typeBuf, out var nv)) { nv = Math.Clamp(nv, min, max); if (nv != value) { value = nv; changed = true; } }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            return changed;
        }

        private static float StepperW => S(106);
        private static string _typeBuf = "";

        /// <summary>A small button sized to its label; flows onto the next line inside [left, right].</summary>
        private static bool FlowButton(string id, string label, FontAwesomeIcon? icon, Kind kind, ref Vector2 at, float left, float right, float h)
        {
            float w = Ui.TextSize(label).X + (icon.HasValue ? Ui.IconSize(icon.Value, 0.9f).X + S(6) : 0) + S(24);
            if (at.X + w > right && at.X > left) at = new Vector2(left, at.Y + h + S(6));
            bool r = Ui.Button(id, label, at, new Vector2(w, h), kind, icon);
            at.X += w + S(6);
            return r;
        }

        private static readonly Regex IconRx = new(@"(\d+)(?:\.png)?\s*$", RegexOptions.Compiled);
        private static uint IconId(string? s) => s != null && IconRx.Match(s) is { Success: true } m && uint.TryParse(m.Groups[1].Value, out var id) ? id : 0;

        private void GameIcon(string? icon, Vector2 pos, float size, float alpha = 1f)
        {
            uint id = IconId(icon);
            if (id == 0) { Ui.Dl.AddRectFilled(pos, pos + new Vector2(size, size), Theme.Night1.U32(), S(4)); return; }
            var wrap = _textureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrDefault();
            if (wrap != null) Ui.Dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size), Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, alpha).U32());
        }

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || Ui.TextSize(text, scale).X <= width) return text ?? "";
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }

        private static string Gil(long v) => v.ToString("N0");
        private static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";

        private static string LevelText(int? level, int? ilvl)
        {
            var parts = new List<string>();
            if (level is > 0) parts.Add($"Lv. {level}");
            if (ilvl is > 1) parts.Add($"iLv. {ilvl}");
            return string.Join("  ·  ", parts);
        }

        /// <summary>Universalis times: upload times in ms, sale timestamps in s.</summary>
        private static string AgoMs(long ms)
        {
            if (ms <= 0) return "unknown";
            var mins = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms) / 60000;
            if (mins < 1) return "just now";
            if (mins < 60) return $"{mins}m ago";
            if (mins < 60 * 24) return $"{mins / 60}h ago";
            return $"{mins / 1440}d ago";
        }

        private static void Empty(Vector2 p, float w, string text, FontAwesomeIcon icon = FontAwesomeIcon.InfoCircle)
        {
            var dl = Ui.Dl;
            float h = LH + S(24);
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), (h - LH) / 2), icon, Theme.Text3);
            Ui.Text(dl, p + new Vector2(S(40), (h - LH) / 2), Fit(text, w - S(54)), Theme.Text2);
        }
        private static float EmptyH => LH + S(24);

        private static void Message(Vector2 p, float w, string text, Vector4 color)
        {
            var dl = Ui.Dl;
            float h = LH + S(20);
            Ui.Panel(dl, p, p + new Vector2(w, h), color.With(0.1f), color.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), (h - LH) / 2), FontAwesomeIcon.ExclamationTriangle, color);
            Ui.Text(dl, p + new Vector2(S(40), (h - LH) / 2), Fit(text, w - S(54)), Theme.Text);
        }
    }
}
