using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVHubCompanion
{
    /// <summary>
    /// Everything the plugin says to Aetherhold: live sync, shared state sections (GET/PATCH /api/hub/state),
    /// the SSE channel and the tool APIs. One base address, one user agent, one token.
    /// </summary>
    public sealed class HubClient
    {
        /// <summary>Same app as xiv.naguya.tech (which stays alive for old plugin builds).</summary>
        public const string BaseUrl = "https://aetherhold.naguya.tech";
        public static readonly string Version = typeof(HubClient).Assembly.GetName().Version?.ToString() ?? "3.0.0.0";
        public static readonly string UserAgent = $"Aetherhold/{Version}";

        // The web server closes idle connections after 5 s; drop ours sooner so a sync never goes out on one it just
        // closed ("An error occurred while sending the request").
        private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(15) };
        private readonly HttpClient _long = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly IPluginLog _log;
        private CancellationTokenSource? _streamCts;
        private string? _token;

        public string CurrentUserRole { get; private set; } = "user";
        public int TotalSyncs { get; private set; }
        public int FailedSyncs { get; private set; }
        public DateTime LastSyncTime { get; private set; } = DateTime.MinValue;
        public string LastSyncStatus { get; private set; } = "Never synced";

        /// <summary>Set on a 401: the saved token is unknown. Everything pauses until a verify succeeds.</summary>
        public bool AuthRejected { get; private set; }
        public bool HasToken => !string.IsNullOrEmpty(_token);
        public event Action? OnAuthRejected;

        // ---- shared state sections -------------------------------------------------------------------------------
        private readonly ConcurrentDictionary<string, JToken> _state = new();
        private readonly ConcurrentDictionary<string, DateTime> _localSetAt = new();
        public bool StateLoaded { get; private set; }
        /// <summary>A section changed (initial load, website, another device). Raised off the framework thread.</summary>
        public event Action<string, JToken?>? StateChanged;

        public HubClient(IPluginLog log, Configuration config)
        {
            _log = log;
            foreach (var c in new[] { _http, _long }) c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            SetToken(config.XivHubId);
        }

        public void SetToken(string? token)
        {
            _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
            var auth = _token == null ? null : new AuthenticationHeaderValue("Bearer", _token);
            _http.DefaultRequestHeaders.Authorization = auth;
            _long.DefaultRequestHeaders.Authorization = auth;
        }

        public void AttachAuthHeader(Configuration config) => SetToken(config.XivHubId);

        public void MarkAuthRejected()
        {
            if (AuthRejected) return;
            AuthRejected = true;
            LastSyncStatus = "Token rejected";
            _log.Warning("Aetherhold rejected the saved plugin token (401). Sync paused until it is entered again.");
            OnAuthRejected?.Invoke();
        }

        // ---- HTTP -------------------------------------------------------------------------------------------------
        private static string Url(string path) => path.StartsWith("http") ? path : BaseUrl + path;
        public static string Q(string s) => Uri.EscapeDataString(s ?? "");

        /// <summary>Sends a request; returns the body on success (and, with okOnly false, on any status), else null.</summary>
        public async Task<string?> Send(HttpMethod method, string path, object? body = null, bool okOnly = true, TimeSpan? timeout = null)
        {
            try
            {
                using var req = new HttpRequestMessage(method, Url(path));
                if (body != null) req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
                var client = timeout.HasValue ? _long : _http;
                using var res = await client.SendAsync(req, cts?.Token ?? CancellationToken.None);
                if (res.StatusCode == HttpStatusCode.Unauthorized && HasToken && (path.StartsWith("/api/local") || path.StartsWith("/api/hub"))) MarkAuthRejected();
                var text = await res.Content.ReadAsStringAsync();
                return res.IsSuccessStatusCode || !okOnly ? text : null;
            }
            catch (Exception ex)
            {
                _log.Debug($"[Aetherhold] {method} {path} failed: {ex.Message}");
                return null;
            }
        }

        public Task<string?> Get(string path, bool okOnly = true) => Send(HttpMethod.Get, path, null, okOnly);
        public Task<string?> Post(string path, object body, bool okOnly = true) => Send(HttpMethod.Post, path, body, okOnly);
        public async Task<bool> Delete(string path) => await Send(HttpMethod.Delete, path) != null;

        // ---- sign-in ----------------------------------------------------------------------------------------------
        public async Task<string> VerifyUserAsync(string token, string expectedName)
        {
            SetToken(token);
            try
            {
                using var res = await _http.GetAsync(Url("/api/local/verify"));
                if (res.StatusCode == HttpStatusCode.Unauthorized) return "Invalid token";
                if (!res.IsSuccessStatusCode) return $"Error: {res.StatusCode}";
                var data = JObject.Parse(await res.Content.ReadAsStringAsync());
                if (data["verified"]?.Value<bool>() != true) return "Invalid token";
                var charName = WebUtility.HtmlDecode(data["name"]?.ToString() ?? "");
                if (charName != expectedName) return $"Character mismatch. Token belongs to {charName}, but you are playing as {expectedName}.";
                CurrentUserRole = data["role"]?.ToString() ?? "user";
                AuthRejected = false;
                _ = LoadStateAsync();
                return "Success";
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to verify the plugin token.");
                return $"Exception: {ex.Message}";
            }
        }

        // ---- live sync (unchanged wire format: POST /api/local/sync) -----------------------------------------------
        public void SendDataAsync(object data) => Task.Run(async () =>
        {
            try
            {
                var json = JsonConvert.SerializeObject(data);
                HttpResponseMessage sent;
                try { sent = await _http.PostAsync(Url("/api/local/sync"), new StringContent(json, Encoding.UTF8, "application/json")); }
                catch (HttpRequestException) // connection dropped before an answer: try once more on a fresh one
                { sent = await _http.PostAsync(Url("/api/local/sync"), new StringContent(json, Encoding.UTF8, "application/json")); }
                using var res = sent;
                LastSyncTime = DateTime.Now;
                TotalSyncs++;
                if (res.StatusCode == HttpStatusCode.Unauthorized) { FailedSyncs++; MarkAuthRejected(); }
                else if (!res.IsSuccessStatusCode) { FailedSyncs++; LastSyncStatus = $"Error: {res.StatusCode}"; }
                else LastSyncStatus = "Success";
            }
            catch (Exception ex)
            {
                LastSyncTime = DateTime.Now; TotalSyncs++; FailedSyncs++;
                LastSyncStatus = $"Exception: {ex.Message}";
            }
        });

        public void SendActionAsync(object data) => _ = Post("/api/local/action", data);

        // ---- state sections ---------------------------------------------------------------------------------------
        public JToken? GetState(string section) => _state.TryGetValue(section, out var v) ? v : null;

        public T? GetState<T>(string section)
        {
            var v = GetState(section);
            if (v == null || v.Type == JTokenType.Null) return default;
            try { return v.ToObject<T>(); } catch { return default; }
        }

        /// <summary>GET /api/hub/state: every section once (after sign-in and after the stream reconnects).</summary>
        public async Task LoadStateAsync()
        {
            if (!HasToken || AuthRejected) return;
            var json = await Get("/api/hub/state");
            if (json == null) return;
            try
            {
                if (JObject.Parse(json)["state"] is JObject sections) Apply(sections, fromLoad: true);
                StateLoaded = true;
            }
            catch (Exception ex) { _log.Debug($"[Aetherhold] state parse failed: {ex.Message}"); }
        }

        /// <summary>PATCH /api/hub/state { values }. The server pushes it to the website and other devices.</summary>
        public void SetStates(IDictionary<string, object?> values)
        {
            if (!HasToken || AuthRejected || values.Count == 0) return;
            foreach (var (k, v) in values)
            {
                _state[k] = v == null ? JValue.CreateNull() : JToken.FromObject(v);
                _localSetAt[k] = DateTime.UtcNow;
            }
            _ = Send(HttpMethod.Patch, "/api/hub/state", new { values });
        }

        public void SetState(string section, object? value) => SetStates(new Dictionary<string, object?> { [section] = value });

        private void Apply(JObject sections, bool fromLoad)
        {
            foreach (var p in sections.Properties())
            {
                // Our own PATCH echoes back through SSE; skip it so a quick second change isn't undone by the first echo.
                if (!fromLoad && _localSetAt.TryGetValue(p.Name, out var at) && (DateTime.UtcNow - at).TotalSeconds < 2) continue;
                var v = p.Value;
                // Old clients stored some sections as JSON strings
                if (v.Type == JTokenType.String && v.ToString() is var s && (s.StartsWith("{") || s.StartsWith("[")))
                    try { v = JToken.Parse(s); } catch { }
                _state[p.Name] = v;
                try { StateChanged?.Invoke(p.Name, v); } catch (Exception ex) { _log.Error(ex, $"State handler failed for {p.Name}"); }
            }
        }

        // ---- SSE (state / live) -----------------------------------------------------------------------------------
        public void StartStreaming()
        {
            if (_streamCts != null) return;
            _streamCts = new CancellationTokenSource();
            var token = _streamCts.Token;
            Task.Run(() => StreamLoop(token));
        }

        public void StopStreaming()
        {
            _streamCts?.Cancel();
            _streamCts = null;
        }

        private async Task StreamLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (AuthRejected || !HasToken)
                {
                    try { await Task.Delay(5000, ct); } catch { break; }
                    continue;
                }
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, Url("/api/local/stream"));
                    req.Headers.Accept.ParseAdd("text/event-stream");
                    using var res = await _long.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (res.StatusCode == HttpStatusCode.Unauthorized) { MarkAuthRejected(); continue; }
                    res.EnsureSuccessStatusCode();
                    // We may have missed changes while disconnected
                    await LoadStateAsync();

                    using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct));
                    var type = "message";
                    while (!ct.IsCancellationRequested)
                    {
                        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        readCts.CancelAfter(TimeSpan.FromSeconds(45)); // server pings every 15 s
                        var line = await reader.ReadLineAsync(readCts.Token);
                        if (line == null) break;
                        if (line.StartsWith("event:")) type = line[6..].Trim();
                        else if (line.StartsWith("data:") && type != "ping") Dispatch(type, line[5..].Trim());
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { _log.Debug($"[Aetherhold] stream: {ex.Message}. Reconnecting in 5 s."); }
                try { await Task.Delay(5000, ct); } catch { break; }
            }
        }

        /// <summary>3.0 listens to `state` only; `live`, `clientStateUpdate` and `marketUpdate` are for the website and 2.1.0.1.</summary>
        private void Dispatch(string type, string data)
        {
            if (type != "state") return;
            try { if (JObject.Parse(data)["sections"] is JObject s) Apply(s, fromLoad: false); }
            catch (Exception ex) { _log.Debug($"[Aetherhold] bad state event: {ex.Message}"); }
        }

        // ---- tool APIs (thin wrappers, same routes as 2.1.0.1) -----------------------------------------------------
        public Task<string?> CalculateRouteAsync(object payload) => Post("/api/market/calculate", payload);
        public Task<string?> SearchItemsAsync(string query) => Get($"/api/market/search?q={Q(query)}");
        public Task<string?> FetchMarketListingsAsync(string scope, uint itemId) => Get($"/api/market/listings?world={Q(scope)}&itemId={itemId}", okOnly: false);
        public Task<string?> FetchCategoriesAsync() => Get("/api/market/category", okOnly: false);

        public Task<string?> FetchCategoryItemsAsync(int categoryId, int page, string minLevel, string maxLevel, string job)
        {
            var q = $"id={categoryId}&page={page}&limit=50";
            if (!string.IsNullOrEmpty(minLevel)) q += $"&minLevel={Q(minLevel)}";
            if (!string.IsNullOrEmpty(maxLevel)) q += $"&maxLevel={Q(maxLevel)}";
            if (!string.IsNullOrEmpty(job)) q += $"&job={Q(job)}";
            return Get($"/api/market/category?{q}", okOnly: false);
        }

        public Task<string?> FetchEventsAsync(string name, string world) => Get($"/api/events/custom?name={Q(name)}&world={Q(world)}");
        public Task<string?> FetchLodestoneEventsAsync() => Get("/api/events/lodestone");
        public Task<string?> CreateCustomEventAsync(object payload) => Post("/api/events/custom", payload);
        public Task<bool> DeleteCustomEventAsync(string id) => Delete($"/api/events/custom?id={Q(id)}");
        public Task WipeCalendarAsync(string name, string world) => Post("/api/events/wipe", new { name, world });
        public Task<string?> GetVenuesAsync(string name, string world) => Get($"/api/events/venues?name={Q(name)}&world={Q(world)}");
        public Task<string?> SubscribeVenueAsync(string url, string name, string world) => Post("/api/events/venues", new { url, playerName = name, playerWorld = world }, okOnly: false);
        public Task<bool> DeleteVenueAsync(string id, string name, string world) => Delete($"/api/events/venues?id={Q(id)}&name={Q(name)}&world={Q(world)}");
        public Task<string?> SyncVenuesAsync(string name, string world) => Post("/api/events/venues/sync", new { playerName = name, playerWorld = world }, okOnly: false);

        public Task<string?> ImportPartakeAsync(string url, string name, string world)
        {
            var isVenue = url.Contains("partake.gg/venues/") || url.Contains("partake.gg/teams/");
            return Post(isVenue ? "/api/events/venues" : "/api/events/partake", new { url, playerName = name, playerWorld = world }, okOnly: false);
        }

        public Task<string?> FetchGatheringNodesAsync() => Get("/api/gathering/nodes");
        public Task<string?> FetchAquaticNodesAsync() => Get($"/data/xhub_aquatic_nodes_v3.json?t={DateTime.UtcNow.Ticks}");
        public Task<string?> FetchGatheringLogPagesAsync() => Get("/data/gathering-log-pages.json");
        public Task<string?> FetchDataNodesAsync() => Get("/data/nodes.json");
    }
}
