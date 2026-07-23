using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Dalamud.Plugin.Services;

namespace XIVHubCompanion
{
    public class DataSender
    {
        private readonly HttpClient _client;
        private readonly string _endpointUrl = "https://xiv.naguya.tech/api/local/sync";
        private readonly IPluginLog _log;

        public int TotalSyncs { get; private set; } = 0;
        public int FailedSyncs { get; private set; } = 0;
        public DateTime LastSyncTime { get; private set; } = DateTime.MinValue;
        public string LastSyncStatus { get; private set; } = "Never synced";

        public DataSender(IPluginLog log)
        {
            _log = log;
            _client = new HttpClient();
            _client.Timeout = TimeSpan.FromSeconds(3);
        }

        public void SendDataAsync(object data)
        {
            Task.Run(async () =>
            {
                try
                {
                    var json = JsonConvert.SerializeObject(data);
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
    }
}
