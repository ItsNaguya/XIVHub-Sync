using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVHubCompanion.Apps
{
    // Events: the website's events page (Lodestone topics + maintenance, Partake imports and venues, custom events).
    // Data, sync and actions here; the screen is in EventsApp_Ui.cs.
    public partial class EventsApp : IApp
    {
        public string Name => "Events";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.CalendarAlt).ToString();
        public bool HasSettings => false;
        public bool LargeText => true;
        public void DrawSettings() { }

        private readonly HubClient _sender;
        private readonly IObjectTable _objectTable;
        private readonly IPluginLog _log;
        private readonly ITextureProvider _textureProvider;
        private readonly IDalamudPluginInterface _pluginInterface;

        private static readonly HttpClient _httpClient = new() { DefaultRequestHeaders = { { "User-Agent", HubClient.UserAgent } } };
        private readonly ConcurrentDictionary<string, ISharedImmediateTexture?> _imageCache = new();

        public EventsApp(HubClient sender, IObjectTable objectTable, IPluginLog log, ITextureProvider textureProvider, IDalamudPluginInterface pluginInterface)
        {
            _sender = sender;
            _objectTable = objectTable;
            _log = log;
            _textureProvider = textureProvider;
            _pluginInterface = pluginInterface;
        }

        public void Dispose() { }

        // =============================================================================================================
        // Data (website: EventsTab.tsx)
        // =============================================================================================================
        public const string KLodestone = "lodestone", KPartake = "partake", KCustom = "custom";
        private static readonly string[] Kinds = { KLodestone, KPartake, KCustom };
        private static readonly TimeSpan Week = TimeSpan.FromDays(7), Horizon = TimeSpan.FromDays(180);

        private sealed class CalEvent
        {
            public string Id = "", Title = "", Image = "", Description = "", Location = "", Url = "", Kind = KCustom;
            public List<string> Categories = new();
            public bool Weekly;
            public DateTime Utc;
            public DateTime Local => Utc.ToLocalTime();
        }

        private sealed class Maint { public string Id = "", Title = "", Tag = "", Url = ""; public DateTime Utc; }
        private sealed class Sub { public string Id = "", Name = "", Url = ""; }

        // swapped as whole lists by the fetch task, read by the UI thread
        private List<CalEvent> _official = new();
        private List<CalEvent> _all = new();      // official + community, weekly ones expanded half a year, sorted
        private List<Maint> _maintenance = new();
        private List<Sub> _subs = new();
        private string _loadError = "";
        private bool _loading = true;
        private volatile bool _fetching;
        private DateTime _lastFetch = DateTime.MinValue;
        private bool _fetchedSignedIn;
        private double _lastDrawTime = -100;

        private static string Str(JToken? t) => t == null || t.Type == JTokenType.Null ? "" : t.ToString();

        private static bool TryDate(JToken? t, out DateTime utc)
        {
            utc = default;
            var s = Str(t);
            if (s.Length == 0) return false;
            if (!DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)) return false;
            utc = dto.UtcDateTime;
            return true;
        }

        private static JObject? ParseObj(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }); }
            catch { return null; }
        }

        private static CalEvent ToEvent(JObject o, string kind, DateTime utc) => new()
        {
            Id = Str(o["id"]), Title = Str(o["title"]) is { Length: > 0 } t ? t : "Untitled", Image = Str(o["image"]),
            Description = Str(o["description"]), Location = Str(o["location"]),
            Url = Str(o["url"]) is { Length: > 0 } u ? u : Str(o["sourceUrl"]),
            Categories = o["categories"] is JArray c ? c.Select(Str).Where(x => x.Length > 0).ToList() : new(),
            Weekly = o["isWeekly"]?.Type == JTokenType.Boolean && (bool)o["isWeekly"]!, Kind = kind, Utc = utc,
        };

        private (string? Name, string? World) Player()
        {
            try
            {
                if (_objectTable[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter p)
                    return (p.Name.ToString(), p.HomeWorld.Value.Name.ExtractText());
            }
            catch { }
            return (null, null);
        }

        private string CharQuery()
        {
            var (n, w) = Player();
            return n != null && w != null ? $"name={HubClient.Q(n)}&world={HubClient.Q(w)}" : "";
        }

        private bool SignedIn => _sender.HasToken && !_sender.AuthRejected;

        /// <summary>Lodestone feed (public) and, signed in, the character's community events and venues.</summary>
        private void Fetch()
        {
            if (_fetching) return;
            _fetching = true;
            _lastFetch = DateTime.Now;
            bool signed = SignedIn;
            _fetchedSignedIn = signed;
            string q = CharQuery();
            Task.Run(async () =>
            {
                try
                {
                    var offTask = _sender.Get("/api/events/lodestone");
                    var comTask = signed ? _sender.Get("/api/events/custom" + (q.Length > 0 ? "?" + q : "")) : Task.FromResult<string?>(null);
                    var subTask = signed ? _sender.Get("/api/events/venues" + (q.Length > 0 ? "?" + q : "")) : Task.FromResult<string?>(null);
                    await Task.WhenAll(offTask, comTask, subTask);

                    var official = new List<CalEvent>();
                    var maint = new List<Maint>();
                    var off = ParseObj(offTask.Result);
                    if (off != null)
                    {
                        if (off["events"] is JArray evs)
                            foreach (var o in evs.OfType<JObject>())
                                if (TryDate(o["date"], out var d)) official.Add(ToEvent(o, KLodestone, d));
                        if (off["maintenance"] is JArray ms)
                            foreach (var o in ms.OfType<JObject>())
                            {
                                TryDate(o["date"], out var d);
                                maint.Add(new Maint { Id = Str(o["id"]), Title = Str(o["title"]), Tag = Str(o["tag"]), Url = Str(o["url"]), Utc = d });
                            }
                        _loadError = "";
                    }
                    else _loadError = "The Lodestone could not be reached.";

                    var all = new List<CalEvent>(official);
                    var com = ParseObj(comTask.Result);
                    if (com?["events"] is JArray cs)
                    {
                        var horizon = DateTime.UtcNow + Horizon;
                        foreach (var o in cs.OfType<JObject>())
                        {
                            if (!TryDate(o["startDate"], out var start)) continue;
                            var kind = Str(o["source"]) == "partake" ? KPartake : KCustom;
                            var e = ToEvent(o, kind, start);
                            all.Add(e);
                            if (!e.Weekly) continue;
                            int n = 0;
                            for (var t = start + Week; t < horizon && n < 50; t += Week, n++)
                                all.Add(ToEvent(o, kind, t));
                        }
                    }
                    all.Sort((a, b) => a.Utc.CompareTo(b.Utc));

                    var subs = new List<Sub>();
                    var sub = ParseObj(subTask.Result);
                    if (sub?["subscriptions"] is JArray ss)
                        foreach (var o in ss.OfType<JObject>())
                            subs.Add(new Sub { Id = Str(o["id"]), Name = Str(o["name"]) is { Length: > 0 } nm ? nm : "Unnamed venue", Url = Str(o["url"]) });

                    _official = official;
                    _maintenance = maint;
                    // keep what we had if the community call failed (e.g. a network blip), like the website
                    if (!signed || com != null) _all = all;
                    else _all = official.Concat(_all.Where(e => e.Kind != KLodestone)).OrderBy(e => e.Utc).ToList();
                    if (!signed || sub != null) _subs = subs;
                }
                catch (Exception ex) { _log.Debug($"[Aetherhold] events fetch failed: {ex.Message}"); }
                finally
                {
                    _loading = false;
                    _fetching = false;
                }
            });
        }

        /// <summary>First load, again when the app is reopened after a minute, every 5 minutes while open, and after sign-in.</summary>
        private void EnsureFresh(bool drawing)
        {
            if (_fetching) return;
            double now = Dalamud.Bindings.ImGui.ImGui.GetTime();
            bool reopened = drawing && now - _lastDrawTime > 2;
            if (drawing) _lastDrawTime = now;
            var age = DateTime.Now - _lastFetch;
            if (_lastFetch == DateTime.MinValue || SignedIn != _fetchedSignedIn
                || (reopened && age.TotalSeconds > 60) || (drawing && age.TotalMinutes > 5))
                Fetch();
        }

        /// <summary>The next upcoming events (home widget and notifications).</summary>
        public List<(string Title, DateTime Start)> NextEvents(int count)
        {
            EnsureFresh(false);
            var now = DateTime.Now;
            return _all.Select(e => (e.Title, Start: e.Local)).Where(x => x.Start >= now.AddHours(-1))
                .OrderBy(x => x.Start).Take(count).ToList();
        }

        // =============================================================================================================
        // View prefs: prefs.events { side, hide } shared with the website, read every frame
        // =============================================================================================================
        private const string SecPrefs = "prefs.events";
        private string _side = "upcoming";
        private readonly HashSet<string> _hidden = new();
        private JToken? _prefsTok;

        public void Update()
        {
            if (!SignedIn) return;
            var t = _sender.GetState(SecPrefs);
            if (ReferenceEquals(t, _prefsTok)) return;
            _prefsTok = t;
            var o = t as JObject;
            var side = Str(o?["side"]);
            _side = side is "maintenance" or "news" ? side : "upcoming";
            _hidden.Clear();
            if (o?["hide"] is JArray h)
                foreach (var k in h.Select(Str)) if (Kinds.Contains(k)) _hidden.Add(k);
        }

        private void SavePrefs()
        {
            if (!SignedIn) return; // signed out the choice stays on this screen only
            var o = _prefsTok is JObject old ? (JObject)old.DeepClone() : new JObject();
            o["side"] = _side;
            o["hide"] = new JArray(_hidden.ToArray());
            _sender.SetState(SecPrefs, o);
            _prefsTok = _sender.GetState(SecPrefs);
        }

        private void SetSide(string side) { if (_side == side) return; _side = side; SavePrefs(); }

        private void ToggleKind(string kind)
        {
            if (!_hidden.Remove(kind)) _hidden.Add(kind);
            SavePrefs();
        }

        // =============================================================================================================
        // Actions (same endpoints and messages as the website)
        // =============================================================================================================
        private string _msg = "";
        private bool _msgBad;
        private DateTime _msgAt;
        private bool _importing, _scanning, _saving;

        private void Flash(string text, bool bad = false) { _msg = text; _msgBad = bad; _msgAt = DateTime.Now; }

        private static string ErrorOf(JObject? o, string fallback) => Str(o?["error"]) is { Length: > 0 } e ? e : fallback;

        private void Import(string url)
        {
            url = url.Trim();
            if (url.Length == 0 || _importing) return;
            if (!SignedIn) { Flash("Sign in to import events.", true); return; }
            _importing = true;
            _msg = "";
            var (n, w) = Player();
            bool venue = url.Contains("/teams/");
            Task.Run(async () =>
            {
                try
                {
                    var res = ParseObj(await _sender.Post(venue ? "/api/events/venues" : "/api/events/partake",
                        new { url, playerName = n, playerWorld = w }, okOnly: false));
                    if (res == null || res["error"] != null) { Flash(ErrorOf(res, "Import failed."), true); return; }
                    _importUrl = "";
                    Flash(venue ? $"Subscribed to {(Str(res["subscription"]?["name"]) is { Length: > 0 } s ? s : "the venue")}."
                                : $"Imported {(Str(res["event"]?["title"]) is { Length: > 0 } t ? t : "the event")}.");
                    _lastFetch = DateTime.MinValue;
                }
                finally { _importing = false; }
            });
        }

        private void Scan()
        {
            if (_scanning || !SignedIn) return;
            _scanning = true;
            _msg = "";
            var (n, w) = Player();
            Task.Run(async () =>
            {
                try
                {
                    var res = ParseObj(await _sender.Post("/api/events/venues/sync", new { playerName = n, playerWorld = w }, okOnly: false));
                    if (res == null || res["error"] != null) { Flash(ErrorOf(res, "Scan failed."), true); return; }
                    int synced = res["synced"]?.Value<int?>() ?? 0, failed = res["failed"]?.Value<int?>() ?? 0;
                    Flash(Str(res["message"]) is { Length: > 0 } m ? m
                        : $"Scanned {synced} venue{(synced == 1 ? "" : "s")}{(failed > 0 ? $", {failed} failed" : "")}.", failed > 0);
                    _lastFetch = DateTime.MinValue;
                }
                catch { Flash("Scan failed.", true); }
                finally { _scanning = false; }
            });
        }

        private void RemoveSub(string id)
        {
            var q = CharQuery();
            Task.Run(async () =>
            {
                var res = await _sender.Send(System.Net.Http.HttpMethod.Delete, $"/api/events/venues?id={HubClient.Q(id)}" + (q.Length > 0 ? "&" + q : ""), null, okOnly: false);
                var o = ParseObj(res);
                if (res == null || o?["error"] != null) Flash(ErrorOf(o, "Could not remove the venue."), true);
                _lastFetch = DateTime.MinValue;
            });
        }

        private void Create(Form f, DateTime localStart)
        {
            if (_saving) return;
            _saving = true;
            var (n, w) = Player();
            var payload = new
            {
                title = f.Title.Trim(), startDate = localStart.ToUniversalTime().ToString("o"), location = f.Location.Trim(),
                description = f.Description, image = f.Image.Trim(), sourceUrl = f.Url.Trim(), isWeekly = f.Weekly, name = n, world = w,
            };
            Task.Run(async () =>
            {
                try
                {
                    var res = await _sender.Post("/api/events/custom", payload, okOnly: false);
                    var o = ParseObj(res);
                    if (res == null || o?["error"] != null) { Flash(ErrorOf(o, "Could not save the event."), true); return; }
                    if (ReferenceEquals(_form, f)) _form = null;
                    _lastFetch = DateTime.MinValue;
                }
                finally { _saving = false; }
            });
        }

        private void DeleteEvent(CalEvent e)
        {
            var q = CharQuery();
            Task.Run(async () =>
            {
                var res = await _sender.Send(System.Net.Http.HttpMethod.Delete, $"/api/events/custom?id={HubClient.Q(e.Id)}" + (q.Length > 0 ? "&" + q : ""), null, okOnly: false);
                var o = ParseObj(res);
                if (res == null || o?["error"] != null) { Flash(ErrorOf(o, "Could not delete the event."), true); return; }
                if (_selected?.Id == e.Id) _selected = null;
                _lastFetch = DateTime.MinValue;
            });
        }

        private static bool IsWebLink(string u) => u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

        private static void OpenLink(string u) { if (IsWebLink(u)) Dalamud.Utility.Util.OpenLink(u); }

        // =============================================================================================================
        // Images (https only, like the website's safeImg), cached on disk
        // =============================================================================================================
        private ISharedImmediateTexture? Image(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            if (_imageCache.TryGetValue(url, out var tex)) return tex;
            _imageCache[url] = null; // one try per url
            Task.Run(async () =>
            {
                try
                {
                    string dir = Path.Combine(_pluginInterface.ConfigDirectory.FullName, "ImageCache");
                    Directory.CreateDirectory(dir);
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    string file = Path.Combine(dir, Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant() + ".png");
                    if (!File.Exists(file)) await File.WriteAllBytesAsync(file, await _httpClient.GetByteArrayAsync(url));
                    _imageCache[url] = _textureProvider.GetFromFile(new FileInfo(file));
                }
                catch (Exception ex) { _log.Debug($"[Aetherhold] event image failed: {url}: {ex.Message}"); }
            });
            return null;
        }
    }
}
