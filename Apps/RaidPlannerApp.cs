using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVHubCompanion.Apps
{
    // Raid Planner: the website's Raid Planner 2 (RaidsTab.tsx, components/widgets/raids/*, lib/raids/model.ts).
    // Statics live on the server (/api/raids); every change is one PUT action the server checks, exactly like the website.
    // Data, model helpers, sync and actions here; the screens are in RaidPlannerApp_Ui.cs, _Schedule.cs and _Loot.cs.
    public partial class RaidPlannerApp : IApp
    {
        public string Name => "Raid Planner";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.Khanda).ToString();
        public bool HasSettings => false;
        public bool LargeText => true;
        public void DrawSettings() { }
        public void Dispose() { }

        private readonly HubClient _sender;
        private readonly IPluginLog _log;

        public RaidPlannerApp(HubClient sender, IPluginLog log)
        {
            _sender = sender;
            _log = log;
        }

        // =============================================================================================================
        // Model (lib/raids/model.ts). Field names match the JSON (Newtonsoft matches them case-insensitively).
        // =============================================================================================================
#pragma warning disable CS0649 // filled by Newtonsoft
        private sealed class Member { public string CharacterId = "", PlayerName = ""; public string? World; public bool Claimed; }
        private sealed class Slot { public string Id = "", Role = "dps"; public Member? Player; public string? Job; }
        private sealed class Session { public string Id = "", Date = ""; public string? Title, Category, Note; public int? DurationMin; }
        private sealed class Exc { public bool? Cancelled; public string? Time, Note; }
        private sealed class Rule
        {
            public string Id = "", Time = "20:00", From = ""; public int Weekday, DurationMin = 180;
            public string? Title, Category, Until; public Dictionary<string, Exc>? Exceptions;
        }
        private sealed class RsvpEntry { public string S = ""; public string? Note; }
        private sealed class Absence { public string Id = "", MemberId = "", From = "", To = ""; public string? Reason; }
        private sealed class TierSettings { public string Name = "", Start = "", Priority = "role"; public List<string>? Floors; }
        private sealed class LogEntry { public string Id = "", Item = "", MemberId = "", Method = "drop"; public int Week, Floor; public string? Note; public long At; }
        private sealed class FloorClear { public int Week, Floor; public List<string> Members = new(); }
        private sealed class BisLink { public string Url = ""; public string? Name; }
        private sealed class Note { public string Id = "", Title = "", Body = ""; public string? Fight; public bool Pinned; public long UpdatedAt; }
        private sealed class Board { public string Id = "", Fight = "", Name = ""; public JArray? Steps; }

        private sealed class Group
        {
            public string Id = "", CreatorId = "", Title = "";
            public List<Slot> Roster = new();
            public List<Member> Bench = new();
            public List<Session> Schedule = new();
            public JObject? Loot;
            public long CreatedAt;
            public string? Timezone;
            public List<string>? Leads;
            public bool GuestOpen;
            public List<Rule>? Rules;
            public Dictionary<string, Dictionary<string, RsvpEntry>>? Rsvp;
            public Dictionary<string, Dictionary<string, Member>>? OccSubs;
            public List<Absence>? AbsenceReports;
            public TierSettings? Tier;
            public Dictionary<string, BisLink>? BisLinks;
            public List<LogEntry>? LootLog;
            public List<FloorClear>? Clears;
            public Dictionary<string, int[]>? BookAdj;
            public List<Note>? Notes;
            public List<Board>? Boards;
        }

#pragma warning restore CS0649

        private sealed class Occ
        {
            public string Key = "", Date = "", Title = "", Category = "raid";
            public string? Note, RuleId, SessionId;
            public DateTime Start, End; // UTC
            public bool Cancelled;
            public DateTime LocalStart => Start.ToLocalTime();
            public DateTime LocalEnd => End.ToLocalTime();
        }

        private static readonly JsonSerializerSettings JsonIn = new() { DateParseHandling = DateParseHandling.None, MissingMemberHandling = MissingMemberHandling.Ignore, Error = (_, e) => e.ErrorContext.Handled = true };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string Or(string? s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;

        // ---- game numbers (Dawntrail savage) -----------------------------------------------------------------------
        private static readonly Dictionary<string, string[]> Jobs = new()
        {
            ["tank"] = new[] { "PLD", "WAR", "DRK", "GNB" },
            ["healer"] = new[] { "WHM", "SCH", "AST", "SGE" },
            ["dps"] = new[] { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR", "BRD", "MCH", "DNC", "BLM", "SMN", "RDM", "PCT", "BLU" },
        };
        private static readonly HashSet<string> Melee = new() { "MNK", "DRG", "NIN", "SAM", "RPR", "VPR" };
        private static readonly HashSet<string> Ranged = new() { "BRD", "MCH", "DNC" };
        private static readonly HashSet<string> Caster = new() { "BLM", "SMN", "RDM", "PCT", "BLU" };
        private static readonly Dictionary<string, string> Position = new()
        { ["tank1"] = "MT", ["tank2"] = "OT", ["healer1"] = "H1", ["healer2"] = "H2", ["dps1"] = "M1", ["dps2"] = "M2", ["dps3"] = "R1", ["dps4"] = "R2" };

        private static readonly string[] GearSlots = { "weapon", "head", "body", "hands", "legs", "feet", "earring", "necklace", "bracelet", "ring", "ring2" };
        private static readonly string[] Materials = { "glaze", "twine", "solvent", "tomestone" };
        private static readonly Dictionary<string, int> FloorOf = new()
        { ["earring"] = 1, ["necklace"] = 1, ["bracelet"] = 1, ["ring"] = 1, ["ring2"] = 1, ["head"] = 2, ["hands"] = 2, ["feet"] = 2, ["body"] = 3, ["legs"] = 3, ["weapon"] = 4 };
        private static readonly Dictionary<string, int> MaterialFloor = new() { ["glaze"] = 2, ["tomestone"] = 2, ["twine"] = 3, ["solvent"] = 3 };
        private static readonly Dictionary<string, int> BookCost = new()
        {
            ["earring"] = 3, ["necklace"] = 3, ["bracelet"] = 3, ["ring"] = 3, ["ring2"] = 3, ["head"] = 4, ["hands"] = 4, ["feet"] = 4,
            ["body"] = 6, ["legs"] = 6, ["weapon"] = 8, ["glaze"] = 4, ["twine"] = 4, ["solvent"] = 0, ["tomestone"] = 0,
        };
        private static readonly Dictionary<string, int> TomeCost = new()
        { ["weapon"] = 500, ["body"] = 825, ["legs"] = 825, ["head"] = 495, ["hands"] = 495, ["feet"] = 495, ["earring"] = 375, ["necklace"] = 375, ["bracelet"] = 375, ["ring"] = 375, ["ring2"] = 375 };
        private const int WeeklyTomeCap = 450;
        private static readonly Dictionary<string, float> SlotWeight = new()
        { ["weapon"] = 3, ["body"] = 1.5f, ["legs"] = 1.5f, ["head"] = 1, ["hands"] = 1, ["feet"] = 1, ["earring"] = 0.8f, ["necklace"] = 0.8f, ["bracelet"] = 0.8f, ["ring"] = 0.8f, ["ring2"] = 0.8f };
        private static readonly HashSet<string> Accessory = new() { "earring", "necklace", "bracelet", "ring", "ring2" };
        private static readonly Dictionary<string, string> GearLabel = new()
        { ["weapon"] = "Weapon", ["head"] = "Head", ["body"] = "Body", ["hands"] = "Hands", ["legs"] = "Legs", ["feet"] = "Feet", ["earring"] = "Ears", ["necklace"] = "Neck", ["bracelet"] = "Wrist", ["ring"] = "Ring", ["ring2"] = "Ring 2" };
        private static readonly Dictionary<string, string> MaterialLabel = new() { ["glaze"] = "Glaze", ["twine"] = "Twine", ["solvent"] = "Solvent", ["tomestone"] = "Universal Tomestone" };
        private static string ItemLabel(string i) => GearLabel.TryGetValue(i, out var g) ? g : MaterialLabel.TryGetValue(i, out var m) ? m : i;
        private static readonly string[] Categories = { "raid", "prog", "reclear", "farm", "ultimate", "social", "other" };

        // ---- members -----------------------------------------------------------------------------------------------
        private static List<Member> AllMembers(Group g)
        {
            var seen = new HashSet<string>();
            var list = new List<Member>();
            foreach (var m in g.Roster.Where(s => s.Player != null).Select(s => s.Player!).Concat(g.Bench))
                if (seen.Add(m.CharacterId)) list.Add(m);
            return list;
        }
        private static bool IsManual(string id) => id.StartsWith("m-");
        private static bool IsManager(Group g, string id) => id.Length > 0 && (g.CreatorId == id || (g.Leads?.Contains(id) ?? false));
        private static string NameOf(Group g, string id) => AllMembers(g).FirstOrDefault(m => m.CharacterId == id)?.PlayerName ?? "Former member";

        private static string SubRole(Slot s)
        {
            if (s.Role != "dps") return s.Role;
            if (s.Job != null && Melee.Contains(s.Job)) return "melee";
            if (s.Job != null && Ranged.Contains(s.Job)) return "ranged";
            if (s.Job != null && Caster.Contains(s.Job)) return "caster";
            return s.Id is "dps1" or "dps2" ? "melee" : "caster";
        }

        // ---- dates and time zones ----------------------------------------------------------------------------------
        private static readonly ConcurrentDictionary<string, TimeZoneInfo> TzCache = new();

        private static TimeZoneInfo FindTz(string id)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
            try { if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win)) return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
            return null!;
        }

        /// <summary>The static's time zone (website groupTz: default Europe/Berlin).</summary>
        private static TimeZoneInfo GroupTz(Group g)
        {
            var id = string.IsNullOrEmpty(g.Timezone) ? "Europe/Berlin" : g.Timezone;
            return TzCache.GetOrAdd(id, k => FindTz(k) ?? FindTz("Europe/Berlin") ?? TimeZoneInfo.Local);
        }

        private static string Ds(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
        private static bool TryDs(string? s, out DateOnly d) => DateOnly.TryParseExact(s ?? "", "yyyy-MM-dd", Inv, DateTimeStyles.None, out d);
        private static DateOnly DateInTz(DateTime utc, TimeZoneInfo tz) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));

        private static bool TryTime(string? s, out int h, out int m)
        {
            h = m = 0;
            var p = (s ?? "").Split(':');
            return p.Length == 2 && int.TryParse(p[0], out h) && int.TryParse(p[1], out m) && h is >= 0 and < 24 && m is >= 0 and < 60;
        }

        private static DateTime ZonedToUtc(DateOnly d, int h, int m, TimeZoneInfo tz)
        {
            var local = DateTime.SpecifyKind(d.ToDateTime(new TimeOnly(h, m)), DateTimeKind.Unspecified);
            if (tz.IsInvalidTime(local)) local = local.AddHours(1); // inside a DST gap
            return TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }

        private static bool TryUtc(string? iso, out DateTime utc)
        {
            utc = default;
            if (!DateTimeOffset.TryParse(iso ?? "", Inv, DateTimeStyles.AssumeUniversal, out var dto)) return false;
            utc = dto.UtcDateTime;
            return true;
        }

        private static long Ms(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalMilliseconds;
        private static DateTime FromMs(long ms) => DateTime.UnixEpoch.AddMilliseconds(ms);

        /// <summary>All sessions (one-off + weekly nights) starting in [from, to), UTC; website occurrences().</summary>
        private static List<Occ> Occurrences(Group g, DateTime from, DateTime to)
        {
            var tz = GroupTz(g);
            var list = new List<Occ>();
            foreach (var s in g.Schedule)
            {
                if (!TryUtc(s.Date, out var start) || start < from || start >= to) continue;
                list.Add(new Occ
                {
                    Key = s.Id, Start = start, End = start.AddMinutes(s.DurationMin is > 0 ? s.DurationMin.Value : 180), Date = Ds(DateInTz(start, tz)),
                    Title = Or(s.Title, "Raid"), Category = Or(s.Category, "raid"), Note = s.Note, SessionId = s.Id,
                });
            }
            var first = DateInTz(from.AddDays(-1), tz);
            var last = DateInTz(to.AddDays(1), tz);
            foreach (var r in g.Rules ?? new())
            {
                if (!TryDs(r.From, out var rf)) continue;
                var d = first < rf ? rf : first;
                var end = TryDs(r.Until, out var ru) && ru < last ? ru : last;
                d = d.AddDays((r.Weekday - (int)d.DayOfWeek + 7) % 7);
                for (int i = 0; d <= end && i < 400; i++, d = d.AddDays(7))
                {
                    var key = Ds(d);
                    Exc? ex = null;
                    r.Exceptions?.TryGetValue(key, out ex);
                    if (!TryTime(Or(ex?.Time, r.Time), out int h, out int m)) continue;
                    var start = ZonedToUtc(d, h, m, tz);
                    if (start < from || start >= to) continue;
                    list.Add(new Occ
                    {
                        Key = $"{r.Id}@{key}", Start = start, End = start.AddMinutes(r.DurationMin > 0 ? r.DurationMin : 180), Date = key,
                        Title = Or(r.Title, "Raid night"), Category = Or(r.Category, "raid"), Note = ex?.Note, Cancelled = ex?.Cancelled == true, RuleId = r.Id,
                    });
                }
            }
            list.Sort((a, b) => a.Start.CompareTo(b.Start));
            return list;
        }

        /// <summary>Effective answer: explicit RSVP, else an absence report covering the date.</summary>
        private static (string? S, string? Note, Absence? Away, bool Explicit) RsvpOf(Group g, Occ o, string mid)
        {
            RsvpEntry? e = null;
            if (g.Rsvp != null && g.Rsvp.TryGetValue(o.Key, out var byMember)) byMember.TryGetValue(mid, out e);
            var away = g.AbsenceReports?.FirstOrDefault(a => a.MemberId == mid && string.CompareOrdinal(a.From, o.Date) <= 0 && string.CompareOrdinal(a.To, o.Date) >= 0);
            if (e != null) return (e.S, e.Note, away, true);
            if (away != null) return ("no", away.Reason, away, false);
            return (null, null, null, false);
        }

        private static Member? SubOf(Group g, string key, string slotId)
            => g.OccSubs != null && g.OccSubs.TryGetValue(key, out var subs) && subs.TryGetValue(slotId, out var m) ? m : null;

        /// <summary>Ready count: roster players not answering "no", plus subs (website readiness()).</summary>
        private static (int Ready, int Yes, int Maybe, int No, int Subs) Readiness(Group g, Occ o)
        {
            int filled = 0, yes = 0, maybe = 0, no = 0, subs = 0;
            foreach (var s in g.Roster)
            {
                if (s.Player == null) continue;
                filled++;
                var r = RsvpOf(g, o, s.Player.CharacterId).S;
                if (r == "no") { no++; if (SubOf(g, o.Key, s.Id) != null) subs++; }
                else if (r == "maybe") maybe++;
                else if (r == "yes") yes++;
            }
            return (filled - no + subs, yes, maybe, no, subs);
        }

        // ---- loot --------------------------------------------------------------------------------------------------
        private struct Gear { public string? Bis; public bool Has, Aug; public string? Item; public int Ilvl; }

        private static Gear GearOf(Group g, string cid, string slot)
        {
            var t = (g.Loot?[cid] as JObject)?[slot];
            if (t == null) return default;
            if (t.Type == JTokenType.Boolean) return new Gear { Has = (bool)t };
            if (t is not JObject o) return default;
            return new Gear
            {
                Bis = o["bis"]?.Type == JTokenType.String ? (string?)o["bis"] : null,
                Has = o["has"]?.Type == JTokenType.Boolean && (bool)o["has"]!,
                Aug = o["aug"]?.Type == JTokenType.Boolean && (bool)o["aug"]!,
                Item = (o["item"] as JObject)?["name"]?.ToString(),
                Ilvl = (o["item"] as JObject)?["ilvl"]?.Type == JTokenType.Integer ? (int)o["item"]!["ilvl"]! : 0,
            };
        }
        private static bool NeedsRaid(Gear e) => (e.Bis == "raid" || e.Bis == null) && !e.Has;
        private static bool NeedsTome(Gear e) => e.Bis == "tome" && !e.Has;
        private static bool NeedsUpgrade(Gear e) => e.Bis == "tome" && e.Has && !e.Aug;
        private static bool SlotDone(Gear e) => e.Bis == "other" || (e.Bis == "tome" ? e.Aug : e.Has);

        /// <summary>Weekly reset: Tuesday 08:00 UTC.</summary>
        private static DateTime LastResetBefore(DateTime utc)
        {
            var t = utc.Date.AddHours(8).AddDays(-(((int)utc.DayOfWeek - 2 + 7) % 7));
            if (t > utc) t = t.AddDays(-7);
            return t;
        }
        private static DateTime TierStart(Group g)
            => LastResetBefore(TryDs(g.Tier?.Start, out var d) ? d.ToDateTime(new TimeOnly(12, 0)) : FromMs(g.CreatedAt));
        private static int WeekNumber(Group g) => Math.Max(1, (int)Math.Floor((DateTime.UtcNow - TierStart(g)).TotalDays / 7) + 1);

        private static int DropsReceived(Group g, string cid) => (g.LootLog ?? new()).Count(l => l.MemberId == cid && GearSlots.Contains(l.Item));

        private static readonly Dictionary<string, int> RoleScore = new() { ["melee"] = 125, ["ranged"] = 100, ["caster"] = 75, ["tank"] = 50, ["healer"] = 25 };

        /// <summary>Who needs this drop, best first (website priorityFor()).</summary>
        private static List<(Slot Slot, int Score, string Why)> PriorityFor(Group g, string item)
        {
            bool byRole = (g.Tier?.Priority ?? "role") != "even";
            var list = new List<(Slot, int, string)>();
            foreach (var slot in g.Roster)
            {
                if (slot.Player == null) continue;
                var cid = slot.Player.CharacterId;
                bool wanted;
                if (GearSlots.Contains(item))
                {
                    var slots = item is "ring" or "ring2" ? new[] { "ring", "ring2" } : new[] { item };
                    wanted = slots.Any(s => NeedsRaid(GearOf(g, cid, s)));
                }
                else if (item == "glaze") wanted = GearSlots.Any(s => Accessory.Contains(s) && NeedsUpgrade(GearOf(g, cid, s)));
                else if (item == "twine") wanted = GearSlots.Any(s => !Accessory.Contains(s) && s != "weapon" && NeedsUpgrade(GearOf(g, cid, s)));
                else if (item == "solvent") wanted = NeedsUpgrade(GearOf(g, cid, "weapon"));
                else if (item == "tomestone") { var w = GearOf(g, cid, "weapon"); wanted = w.Bis == "tome" && !w.Has; }
                else wanted = false;
                if (!wanted) continue;
                float need = GearSlots.Sum(s => SlotDone(GearOf(g, cid, s)) ? 0 : SlotWeight[s]);
                int got = DropsReceived(g, cid);
                int role = byRole ? RoleScore[SubRole(slot)] : 0;
                int score = (int)Math.Round(role + need * 10 - got * 15, MidpointRounding.AwayFromZero);
                list.Add((slot, score, $"{(byRole ? $"role {role} + " : "")}need {Math.Round(need * 10)} - {got} drop{(got == 1 ? "" : "s")} x15"));
            }
            return list.OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.Player!.PlayerName, StringComparer.Ordinal).ToList();
        }

        /// <summary>Books per floor: clears + manual adjustment - book purchases.</summary>
        private static int[] Books(Group g, string cid)
        {
            var res = new int[4];
            foreach (var c in g.Clears ?? new()) if (c.Members.Contains(cid) && c.Floor is >= 1 and <= 4) res[c.Floor - 1]++;
            foreach (var l in g.LootLog ?? new())
                if (l.MemberId == cid && l.Method == "book")
                {
                    int f = FloorOf.TryGetValue(l.Item, out var gf) ? gf : l.Item == "glaze" ? 2 : 3;
                    res[f - 1] -= BookCost.TryGetValue(l.Item, out var c) ? c : 0;
                }
            if (g.BookAdj != null && g.BookAdj.TryGetValue(cid, out var adj))
                for (int i = 0; i < 4 && i < adj.Length; i++) res[i] += adj[i];
            return res;
        }

        private static (int Raid, int Tome, int Tomes, int Weeks, int Twine, int Glaze, int Solvent) NeedSummary(Group g, string cid)
        {
            int raid = 0, tome = 0, tomes = 0, twine = 0, glaze = 0, solvent = 0;
            foreach (var s in GearSlots)
            {
                var e = GearOf(g, cid, s);
                if (NeedsRaid(e)) raid++;
                if (NeedsTome(e)) { tome++; tomes += TomeCost[s]; }
                if (e.Bis == "tome" && !e.Aug) { if (Accessory.Contains(s)) glaze++; else if (s == "weapon") solvent++; else twine++; }
            }
            return (raid, tome, tomes, (int)Math.Ceiling(tomes / (double)WeeklyTomeCap), twine, glaze, solvent);
        }

        private List<string> Floors(Group g)
        {
            var f = g.Tier?.Floors;
            return f != null && f.Count == 4 ? f : new List<string> { "Floor 1", "Floor 2", "Floor 3", "Floor 4" };
        }

        // =============================================================================================================
        // Data: GET /api/raids (the caller's statics, full and sanitised), refreshed like the website (every minute)
        // =============================================================================================================
        private List<Group> _groups = new();
        private bool _loading = true;
        private string _loadError = "";
        private volatile bool _fetching;
        private DateTime _lastFetch = DateTime.MinValue;
        private bool _fetchedSignedIn;
        private double _lastDrawTime = -100;

        // the signed-in character (from /api/local/verify), to find "you" in the roster
        private string _me = "", _myName = "", _myWorld = "";
        private DateTime _meTried = DateTime.MinValue;

        private bool SignedIn => _sender.HasToken && !_sender.AuthRejected;

#pragma warning disable CS0649
        private sealed class ListResp { public List<Group>? Groups; public string? Error; }
        private sealed class OneResp { public Group? Group; public string? Error; public bool Success; }
#pragma warning restore CS0649

        private void Fetch()
        {
            if (_fetching) return;
            _fetching = true;
            _lastFetch = DateTime.Now;
            _fetchedSignedIn = SignedIn;
            bool needMe = _me.Length == 0 && (DateTime.Now - _meTried).TotalSeconds > 30;
            if (needMe) _meTried = DateTime.Now;
            Task.Run(async () =>
            {
                try
                {
                    if (needMe)
                    {
                        var v = await _sender.Get("/api/local/verify", okOnly: false);
                        try
                        {
                            var o = v == null ? null : JsonConvert.DeserializeObject<JObject>(v, JsonIn);
                            if (o?["characterId"] != null)
                            {
                                _myName = System.Net.WebUtility.HtmlDecode(o["name"]?.ToString() ?? "");
                                _myWorld = o["world"]?.ToString() ?? "";
                                _me = o["characterId"]!.ToString();
                            }
                        }
                        catch { }
                    }
                    var json = await _sender.Get("/api/raids", okOnly: false);
                    var res = json == null ? null : JsonConvert.DeserializeObject<ListResp>(json, JsonIn);
                    if (res?.Groups != null) { _groups = res.Groups; _loadError = ""; }
                    else _loadError = Or(res?.Error, "Could not load your statics");
                }
                catch (Exception ex)
                {
                    _loadError = "Could not load your statics";
                    _log.Debug($"[Aetherhold] raids fetch failed: {ex.Message}");
                }
                finally
                {
                    _loading = false;
                    _fetching = false;
                }
            });
        }

        /// <summary>First load, on reopen after 20 s, every minute while open, and after sign-in.</summary>
        private void EnsureFresh()
        {
            if (_fetching || !SignedIn) return;
            double now = Dalamud.Bindings.ImGui.ImGui.GetTime();
            bool reopened = now - _lastDrawTime > 2;
            _lastDrawTime = now;
            var age = DateTime.Now - _lastFetch;
            if (_lastFetch == DateTime.MinValue || SignedIn != _fetchedSignedIn || (reopened && age.TotalSeconds > 20) || age.TotalSeconds > 60)
                Fetch();
        }

        // =============================================================================================================
        // View prefs: prefs.raids { open, tab, cal, loot } shared with the website, read every frame
        // =============================================================================================================
        private const string SecPrefs = "prefs.raids";
        private static readonly string[] Tabs = { "schedule", "roster", "loot", "strategy", "notes", "settings" };
        private string? _open;
        private string _tab = "schedule", _cal = "calendar", _lootSec = "gear";
        private JToken? _prefsTok;

        public void Update()
        {
            if (!SignedIn) return;
            var t = _sender.GetState(SecPrefs);
            if (ReferenceEquals(t, _prefsTok)) return;
            _prefsTok = t;
            var o = t as JObject;
            _open = o?["open"]?.Type == JTokenType.String ? o["open"]!.ToString() : null;
            var tab = o?["tab"]?.ToString();
            _tab = tab != null && Tabs.Contains(tab) ? tab : "schedule";
            _cal = o?["cal"]?.ToString() == "list" ? "list" : "calendar";
            var loot = o?["loot"]?.ToString();
            _lootSec = loot is "week" or "log" or "books" ? loot : "gear";
        }

        /// <summary>Merged write like the website's savePrefs (unknown keys kept).</summary>
        private void SavePrefs(string? open = null, bool setOpen = false, string? tab = null, string? cal = null, string? loot = null)
        {
            if (setOpen) _open = open;
            if (tab != null) _tab = tab;
            if (cal != null) _cal = cal;
            if (loot != null) _lootSec = loot;
            if (!SignedIn) return;
            var o = _prefsTok is JObject old ? (JObject)old.DeepClone() : new JObject();
            if (setOpen) o["open"] = open == null ? JValue.CreateNull() : open;
            if (tab != null) o["tab"] = tab;
            if (cal != null) o["cal"] = cal;
            if (loot != null) o["loot"] = loot;
            _sender.SetState(SecPrefs, o);
            _prefsTok = _sender.GetState(SecPrefs);
        }

        // =============================================================================================================
        // Actions: PUT /api/raids/<code> { action, ...fields }; the plugin token says who we are
        // =============================================================================================================
        private volatile bool _busy;
        private string _msg = "";
        private bool _msgBad;
        private DateTime _msgAt;

        private void Flash(string text, bool bad = false) { _msg = text; _msgBad = bad; _msgAt = DateTime.Now; }

        private void Act(string code, Dictionary<string, object?> body, Action? onOk = null, string? okText = null)
        {
            if (_busy) return;
            if (!SignedIn) { Flash("Sign in to Aetherhold first.", true); return; }
            _busy = true;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Send(HttpMethod.Put, $"/api/raids/{code}", body, okOnly: false);
                    if (json == null) { Flash("Network error", true); return; }
                    var res = JsonConvert.DeserializeObject<OneResp>(json, JsonIn);
                    if (res == null || res.Error != null) { Flash(Or(res?.Error, "That did not work"), true); _lastFetch = DateTime.MinValue; return; }
                    if (res.Group != null)
                    {
                        var g = res.Group;
                        _groups = _groups.Select(x => x.Id == code ? g : x).ToList();
                        if (g.Id != code) SavePrefs(g.Id, true);
                    }
                    else
                    {
                        _groups = _groups.Where(x => x.Id != code).ToList();
                        SavePrefs(null, true);
                    }
                    if (okText != null) Flash(okText);
                    onOk?.Invoke();
                }
                catch (Exception ex) { Flash("That did not work", true); _log.Debug($"[Aetherhold] raid action failed: {ex.Message}"); }
                finally { _busy = false; }
            });
        }

        private static Dictionary<string, object?> B(string action, params (string Key, object? Value)[] fields)
        {
            var d = new Dictionary<string, object?> { ["action"] = action };
            foreach (var (k, v) in fields) d[k] = v;
            return d;
        }

        private static string LocalIanaZone()
        {
            var id = TimeZoneInfo.Local.Id;
            try { if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana)) return iana; } catch { }
            return id.Contains('/') ? id : "Europe/Berlin";
        }

        private void Create(string title)
        {
            if (_busy || title.Trim().Length == 0) return;
            _busy = true;
            var body = new { title = title.Trim(), playerName = _myName, world = _myWorld, timezone = LocalIanaZone() };
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Post("/api/raids", body, okOnly: false);
                    var res = json == null ? null : JsonConvert.DeserializeObject<OneResp>(json, JsonIn);
                    if (res?.Group == null) { Flash(Or(res?.Error, "Could not create the static"), true); return; }
                    _newTitle = "";
                    _groups = new[] { res.Group }.Concat(_groups.Where(x => x.Id != res.Group.Id)).ToList();
                    SavePrefs(res.Group.Id, true, tab: "roster");
                }
                finally { _busy = false; }
            });
        }

        private void Join(string code)
        {
            code = code.Trim().ToUpperInvariant();
            if (_busy || code.Length != 6) return;
            _busy = true;
            var body = new { playerName = _myName, world = _myWorld };
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Post($"/api/raids/{code}/join", body, okOnly: false);
                    var res = json == null ? null : JsonConvert.DeserializeObject<OneResp>(json, JsonIn);
                    if (res == null || res.Error != null) { Flash(Or(res?.Error, "Could not join"), true); return; }
                    _joinCode = "";
                    if (res.Group != null)
                    {
                        _groups = new[] { res.Group }.Concat(_groups.Where(x => x.Id != res.Group.Id)).ToList();
                        SavePrefs(res.Group.Id, true, tab: "schedule");
                    }
                    Flash("Joined. The leader can now place you on the roster.");
                }
                finally { _busy = false; }
            });
        }

        private void DeleteStatic(string code)
        {
            if (_busy) return;
            _busy = true;
            Task.Run(async () =>
            {
                try
                {
                    var json = await _sender.Send(HttpMethod.Delete, $"/api/raids/{code}", null, okOnly: false);
                    var res = json == null ? null : JsonConvert.DeserializeObject<OneResp>(json, JsonIn);
                    if (res == null || res.Error != null) { Flash(Or(res?.Error, "Could not delete"), true); return; }
                    _groups = _groups.Where(x => x.Id != code).ToList();
                    SavePrefs(null, true);
                }
                finally { _busy = false; }
            });
        }

        /// <summary>Opens the website's Raid Planner; it lands on the same static and tab (prefs.raids).</summary>
        private static void OpenWebsite() => Dalamud.Utility.Util.OpenLink(HubClient.BaseUrl + "/dashboard?tab=raids");
    }
}
