using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Collections;

namespace XIVHubCompanion.Apps
{
    /// <summary>
    /// Extra facts per item from FFXIV Collect, the same master lists the website uses: patch, how many players own it,
    /// sources, movement/seats. Owned state always comes from the game. Plain GETs, no character data sent.
    /// </summary>
    public partial class CollectionApp
    {
        private sealed class CollectInfo
        {
            public string Patch = "";
            public string Owned = "";      // "12.3%"
            public float OwnedPct = 100f;  // 100 when unknown (sorts last under "Rarest first")
            public string[] Sources = Array.Empty<string>();
            public string Description = "";
            public string Extra = "";      // movement · seats · race · behaviour
        }

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly ConcurrentDictionary<CollectionCategory, Dictionary<string, CollectInfo>> _info = new();
        private readonly ConcurrentDictionary<CollectionCategory, DateTime> _infoFailedAt = new();
        private int _infoVersion;
        private CollectionCategory? _infoLoading;
        private string _infoError = "";

        private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

        private CollectInfo? Info(CollectionItem it)
        {
            if (!_info.TryGetValue(it.Category, out var d)) return null;
            if (it.Category == CollectionCategory.Achievements && d.TryGetValue("#" + it.Id, out var byId)) return byId;
            return d.TryGetValue(Norm(it.Name), out var i) ? i : null;
        }

        private bool InfoLoaded(CollectionCategory c) => _info.ContainsKey(c);
        private bool InfoFailed(CollectionCategory c) => !_info.ContainsKey(c) && _infoFailedAt.ContainsKey(c);

        /// <summary>One list at a time: the open category first, the others after it. A failed list retries after 5 minutes.</summary>
        private void PumpInfo()
        {
            if (_infoLoading != null) return;
            foreach (var c in new[] { _cat }.Concat(Cats.Select(x => x.Cat)))
            {
                var path = CatDef(c).Collect;
                if (path == null || _info.ContainsKey(c)) continue;
                if (_infoFailedAt.TryGetValue(c, out var at) && (DateTime.UtcNow - at).TotalMinutes < 5) continue;
                _infoLoading = c;
                _ = Task.Run(() => LoadInfo(c, path));
                return;
            }
        }

        private async Task LoadInfo(CollectionCategory c, string path)
        {
            try
            {
                var json = await Http.GetStringAsync("https://ffxivcollect.com/api/" + path);
                var d = new Dictionary<string, CollectInfo>();
                if (JObject.Parse(json)["results"] is JArray arr)
                    foreach (var o in arr.OfType<JObject>())
                    {
                        var info = new CollectInfo
                        {
                            Patch = Str(o["patch"]),
                            Owned = Str(o["owned"]),
                            Description = Str(o["description"]),
                        };
                        if (float.TryParse(info.Owned.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct)) info.OwnedPct = pct;
                        if (o["sources"] is JArray sa)
                            info.Sources = sa.OfType<JObject>().Select(s => (Str(s["type"]), Str(s["text"])))
                                .Where(s => s.Item2.Length > 0).Select(s => s.Item1.Length > 0 ? $"{s.Item1}: {s.Item2}" : s.Item2).ToArray();
                        int seats = o["seats"]?.Type == JTokenType.Integer ? o["seats"]!.Value<int>() : 0;
                        info.Extra = string.Join(" · ", new[] { Str(o["movement"]), seats > 1 ? $"{seats} seats" : "", Str(o["race"]?["name"]), Str(o["behavior"]?["name"]) }.Where(s => s.Length > 0));
                        var name = Norm(Str(o["name"]));
                        if (name.Length > 0) d.TryAdd(name, info);
                        if (c == CollectionCategory.Achievements && o["id"] != null) d["#" + Str(o["id"])] = info;
                    }
                _info[c] = d;
                _infoFailedAt.TryRemove(c, out _);
            }
            catch (Exception ex)
            {
                _infoFailedAt[c] = DateTime.UtcNow;
                _infoError = $"FFXIV Collect ({path}) not reachable: {ex.Message}";
            }
            finally
            {
                Interlocked.Increment(ref _infoVersion);
                _infoLoading = null;
            }
        }

        private static string Str(JToken? t) => t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Object || t.Type == JTokenType.Array ? "" : t.ToString().Trim();

        private static readonly string[] Expansions = { "A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers", "Endwalker", "Dawntrail" };

        private static string ExpansionOfPatch(string patch)
        {
            if (string.IsNullOrEmpty(patch) || !char.IsDigit(patch[0])) return "";
            int major = patch[0] - '0';
            return major <= 2 ? Expansions[0] : major - 2 < Expansions.Length ? Expansions[major - 2] : "";
        }

        private static float PatchNum(string patch)
            => float.TryParse(patch, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0f;
    }
}
