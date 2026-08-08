using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin.Services;

namespace XIVHubCompanion
{
    public class DataSender
    {
        private readonly HttpClient _client;
        private readonly HttpClient _sseClient;
        private readonly string _endpointUrl = "https://xiv.naguya.tech/api/local/sync";
        private readonly string _streamUrl = "https://xiv.naguya.tech/api/local/stream";
        private readonly IPluginLog _log;
        
        private CancellationTokenSource _streamCts;

        public event Action<string, string> OnServerEventReceived;
        public event Action<string> OnError;
        public event Action<string> OnSuccess;

        public string CurrentUserRole { get; private set; } = "user";

        public int TotalSyncs { get; private set; } = 0;
        public int FailedSyncs { get; private set; } = 0;
        public DateTime LastSyncTime { get; private set; } = DateTime.MinValue;
        public string LastSyncStatus { get; private set; } = "Never synced";

        public DataSender(IPluginLog log, Configuration config)
        {
            _log = log;
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(15);
            var version = typeof(DataSender).Assembly.GetName().Version?.ToString() ?? "1.0";
            _client.DefaultRequestHeaders.Add("User-Agent", $"XIVHubCompanion/{version}");
            _sseClient = new HttpClient();
            _sseClient.Timeout = Timeout.InfiniteTimeSpan;
            _sseClient.DefaultRequestHeaders.Add("User-Agent", $"XIVHubCompanion/{version}");
            
            AttachAuthHeader(config);
        }

        public async Task<string> VerifyUserAsync(string token, string expectedName)
        {
            try
            {
                _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                
                var url = "https://xiv.naguya.tech/api/local/verify";
                _log.Debug($"[Verify] Requesting: {url}");
                var response = await _client.GetAsync(url);
                _log.Debug($"[Verify] Response Code: {response.StatusCode}");
                
                var json = await response.Content.ReadAsStringAsync();
                _log.Debug($"[Verify] Response Content: {json}");

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonNode.Parse(json);
                    bool isVerified = data?["verified"]?.GetValue<bool>() ?? false;
                    string role = data?["role"]?.ToString() ?? "user";
                    string charName = data?["name"]?.ToString() ?? "";
                    charName = System.Net.WebUtility.HtmlDecode(charName);
                    
                    if (!isVerified) return "Invalid token";
                    
                    if (charName != expectedName) {
                        return $"Character mismatch. Token belongs to {charName}, but you are playing as {expectedName}.";
                    }

                    CurrentUserRole = role;
                    return "Success";
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                    return "Invalid token";
                }
                
                return $"Error: {response.StatusCode}"; 
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to verify user.");
                return $"Exception: {ex.Message}";
            }
        }

        public void AttachAuthHeader(Configuration config)
        {
            if (!string.IsNullOrEmpty(config?.XivHubId))
            {
                _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.XivHubId);
                _sseClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.XivHubId);
            }
        }

        public void StartStreaming()
        {
            if (_streamCts != null) return;
            _streamCts = new CancellationTokenSource();
            Task.Run(() => ConnectToStream(_streamCts.Token));
        }

        public void StopStreaming()
        {
            _streamCts?.Cancel();
            _streamCts = null;
        }

        private async Task ConnectToStream(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _log.Debug($"Connecting to SSE stream at {_streamUrl}...");
                    using var request = new HttpRequestMessage(HttpMethod.Get, _streamUrl);
                    request.Headers.Add("Accept", "text/event-stream");
                    var version = typeof(DataSender).Assembly.GetName().Version?.ToString() ?? "1.0";
                    request.Headers.Add("User-Agent", $"XIVHubCompanion/{version}");

                    using var response = await _sseClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    
                    _log.Debug("SSE stream connected successfully.");

                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var reader = new StreamReader(stream);

                    string eventType = "message";
                    
                    while (!token.IsCancellationRequested)
                    {
                        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        readTimeoutCts.CancelAfter(TimeSpan.FromSeconds(45)); // Read timeout for pings
                        
                        var line = await reader.ReadLineAsync(readTimeoutCts.Token);
                        if (line == null) break;
                        
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (line.StartsWith("event:"))
                        {
                            eventType = line.Substring(6).Trim();
                        }
                        else if (line.StartsWith("data:"))
                        {
                            var data = line.Substring(5).Trim();
                            if (eventType != "ping")
                            {
                                OnServerEventReceived?.Invoke(eventType, data);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) 
                { 
                    if (token.IsCancellationRequested) break;
                    _log.Debug("SSE Stream timed out waiting for heartbeat. Reconnecting in 5s...");
                    try { try { await Task.Delay(5000, token); } catch { break; } } catch { break; }
                }
                catch (Exception ex)
                {
                    _log.Debug($"SSE Stream error: {ex.Message}. Reconnecting in 5s...");
                    try { await Task.Delay(5000, token); } catch { break; }
                }
            }
        }

        public void SendDataAsync(object data)
        {
            Task.Run(async () =>
            {
                try
                {
                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    var response = await _client.PostAsync(_endpointUrl, content);
                    LastSyncTime = DateTime.Now;
                    TotalSyncs++;

                    if (!response.IsSuccessStatusCode)
                    {
                        FailedSyncs++;
                        LastSyncStatus = $"Error: {response.StatusCode}";
                        // Don't spam warnings if it fails repeatedly
                        _log.Debug($"Failed to sync to XIV Hub: {response.StatusCode}");
                    }
                    else
                    {
                        LastSyncStatus = "Success";
                        _log.Debug($"Synced data successfully.");
                    }
                }
                catch (Exception ex)
                {
                    LastSyncTime = DateTime.Now;
                    TotalSyncs++;
                    FailedSyncs++;
                    LastSyncStatus = $"Exception: {ex.Message}";
                    _log.Debug($"Could not connect to XIV Hub: {ex.Message}");
                }
            });
        }

        public void SendActionAsync(object data)
        {
            Task.Run(async () =>
            {
                try
                {
                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await _client.PostAsync("https://xiv.naguya.tech/api/local/action", content);
                }
                catch (Exception ex)
                {
                    _log.Debug($"Failed to send action: {ex.Message}");
                }
            });
        }

        public async Task<string> CalculateRouteAsync(object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _client.PostAsync("https://xiv.naguya.tech/api/market/calculate", content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to calculate route remotely.");
                return null;
            }
        }

        public async Task<string> PullMarketStateAsync(string characterName)
        {
            try
            {
                var response = await _client.GetAsync($"https://xiv.naguya.tech/api/local/market/sync?name={Uri.EscapeDataString(characterName)}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to pull market state.");
            }
            return null;
        }

        public async Task PushMarketStateAsync(object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                await _client.PostAsync("https://xiv.naguya.tech/api/local/market/sync", content);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to push market state.");
            }
        }

        public async Task<string> SearchItemsAsync(string query)
        {
            try
            {
                var response = await _client.GetAsync($"https://xiv.naguya.tech/api/market/search?q={Uri.EscapeDataString(query)}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to search items.");
            }
            return null;
        }

        // Removed legacy VerifyUserAsync

        public async Task<string> FetchEventsAsync(string name, string world)
        {
            try
            {
                var response = await _client.GetAsync($"https://xiv.naguya.tech/api/events/custom?name={Uri.EscapeDataString(name)}&world={Uri.EscapeDataString(world)}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch events.");
            }
            return null;
        }

        public async Task WipeCalendarAsync(string name, string world)
        {
            try
            {
                var payload = new { name = name, world = world };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _client.PostAsync("https://xiv.naguya.tech/api/events/wipe", content);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to wipe calendar");
            }
        }

                public async Task<string> FetchClientStateAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/api/local/clientstate");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch client state from XIV Hub.");
            }
            return null;
        }

        public async Task PushClientStateAsync(string key, object value)
        {
            try
            {
                var payload = new { key = key, value = value };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                await _client.PostAsync("https://xiv.naguya.tech/api/local/clientstate", content);
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Failed to push client state for key {key}");
            }
        }

        public async Task<string> FetchLodestoneEventsAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/api/events/lodestone");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch lodestone events.");
            }
            return null;
        }

        public async Task<bool> DeleteCustomEventAsync(string id)
        {
            try
            {
                var response = await _client.DeleteAsync($"https://xiv.naguya.tech/api/events/custom?id={Uri.EscapeDataString(id)}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to delete custom event.");
            }
            return false;
        }

        public async Task<string> ImportPartakeAsync(string url, string name, string world)
        {
            try
            {
                var payload = new { url = url, playerName = name, playerWorld = world };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                string endpoint = "https://xiv.naguya.tech/api/events/partake";
                if (url.Contains("partake.gg/venues/") || url.Contains("partake.gg/teams/"))
                {
                    endpoint = "https://xiv.naguya.tech/api/events/venues";
                }
                var response = await _client.PostAsync(endpoint, content);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to import from partake.");
            }
            return null;
        }

        public async Task<string> SubscribeVenueAsync(string url, string name, string world)
        {
            try
            {
                var payload = new { url = url, playerName = name, playerWorld = world };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync("https://xiv.naguya.tech/api/events/venues", content);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to subscribe venue.");
            }
            return null;
        }

        public async Task<bool> DeleteVenueAsync(string id, string name, string world)
        {
            try
            {
                var response = await _client.DeleteAsync($"https://xiv.naguya.tech/api/events/venues?id={Uri.EscapeDataString(id)}&name={Uri.EscapeDataString(name)}&world={Uri.EscapeDataString(world)}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to delete venue.");
            }
            return false;
        }

        public async Task<string> SyncVenuesAsync(string name, string world)
        {
            try
            {
                var payload = new { playerName = name, playerWorld = world };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync("https://xiv.naguya.tech/api/events/venues/sync", content);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to sync venues.");
            }
            return null;
        }

        public async Task<string> CreateCustomEventAsync(object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _client.PostAsync("https://xiv.naguya.tech/api/events/custom", content);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to create custom event.");
            }
            return null;
        }

        public async Task<string> GetVenuesAsync(string name, string world)
        {
            try
            {
                var response = await _client.GetAsync($"https://xiv.naguya.tech/api/events/venues?name={Uri.EscapeDataString(name)}&world={Uri.EscapeDataString(world)}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch venues.");
            }
            return null;
        }
        public async Task<string> FetchMarketListingsAsync(string scope, uint itemId)
        {
            try
            {
                var response = await _client.GetAsync($"https://xiv.naguya.tech/api/market/listings?world={Uri.EscapeDataString(scope)}&itemId={itemId}");
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch market listings.");
            }
            return null;
        }
        public async Task<string> FetchCategoriesAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/api/market/category");
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch market categories.");
            }
            return null;
        }

        public async Task<string> FetchCategoryItemsAsync(int categoryId, int page, string minLevel, string maxLevel, string job)
        {
            try
            {
                var queryParams = new List<string>
                {
                    $"id={categoryId}",
                    $"page={page}",
                    "limit=50"
                };

                if (!string.IsNullOrEmpty(minLevel)) queryParams.Add($"minLevel={Uri.EscapeDataString(minLevel)}");
                if (!string.IsNullOrEmpty(maxLevel)) queryParams.Add($"maxLevel={Uri.EscapeDataString(maxLevel)}");
                if (!string.IsNullOrEmpty(job)) queryParams.Add($"job={Uri.EscapeDataString(job)}");

                var url = $"https://xiv.naguya.tech/api/market/category?{string.Join("&", queryParams)}";
                var response = await _client.GetAsync(url);
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch category items.");
            }
            return null;
        }
    
        public async Task<string> FetchGatheringNodesAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/api/gathering/nodes");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch gathering nodes");
            }
            return null;
        }

        public async Task<string> FetchAquaticNodesAsync()
        {
            try
            {
                // During dev it could be xiv.naguya.tech/data/... or we can just point to the live server
                var response = await _client.GetAsync($"https://xiv.naguya.tech/data/xhub_aquatic_nodes_v3.json?t={DateTime.UtcNow.Ticks}");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch aquatic nodes");
            }
            return null;
        }
    
        public async Task<string> FetchGatheringLogPagesAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/data/gathering-log-pages.json");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch gathering log pages.");
            }
            return null;
        }

        public async Task<string> FetchDataNodesAsync()
        {
            try
            {
                var response = await _client.GetAsync("https://xiv.naguya.tech/data/nodes.json");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to fetch data nodes.");
            }
            return null;
        }
    }
}

