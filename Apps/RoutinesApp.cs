using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XIVHubCompanion.Apps
{
    public class RoutineTask
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";
        [JsonProperty("type")]
        public string Type { get; set; } = "daily";
        [JsonProperty("categoryId")]
        public string CategoryId { get; set; } = "";
        [JsonProperty("label")]
        public string Label { get; set; } = "";
    }

    public class RoutineCategory
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "daily";
    }

    /// <summary>
    /// Daily and weekly checklist, same as the website's routines page (website/src/components/widgets/Checklist.tsx),
    /// plus the plugin-only retainer venture cards. Ticks, hidden tasks and custom tasks are shared with the website
    /// through routines.checklist / routines.hidden / routines.custom, read every frame by token (like Market).
    /// </summary>
    public partial class RoutinesApp : IApp
    {
        public string Name => "Routines";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.ClipboardCheck).ToString();
        public bool LargeText => true;
        public bool HasSettings => true;

        private const string SecTicks = "routines.checklist", SecHidden = "routines.hidden", SecCustom = "routines.custom";

        private readonly HubClient _sender;
        private readonly Configuration _config;

        // Same lists as website/src/data/routinesData.ts
        private static readonly List<RoutineCategory> Categories = new()
        {
            new() { Id = "daily_roulette", Label = "Duty Roulettes", Type = "daily" },
            new() { Id = "daily_quests", Label = "Quests & Delivery", Type = "daily" },
            new() { Id = "daily_gold_saucer", Label = "Gold Saucer", Type = "daily" },
            new() { Id = "weekly_raids", Label = "Raids & Tomestones", Type = "weekly" },
            new() { Id = "weekly_quests", Label = "Deliveries & Logs", Type = "weekly" },
            new() { Id = "weekly_gold_saucer", Label = "Gold Saucer", Type = "weekly" },
            new() { Id = "custom_daily", Label = "Custom Tasks", Type = "daily" },
            new() { Id = "custom_weekly", Label = "Custom Tasks", Type = "weekly" },
        };

        private static readonly List<RoutineTask> DefaultTasks = new()
        {
            new() { Id = "daily_expert", Type = "daily", CategoryId = "daily_roulette", Label = "Expert Roulette" },
            new() { Id = "daily_leveling", Type = "daily", CategoryId = "daily_roulette", Label = "Leveling Roulette" },
            new() { Id = "daily_trials", Type = "daily", CategoryId = "daily_roulette", Label = "Trials Roulette" },
            new() { Id = "daily_alliance", Type = "daily", CategoryId = "daily_roulette", Label = "Alliance Raid Roulette" },
            new() { Id = "daily_msq", Type = "daily", CategoryId = "daily_roulette", Label = "Main Scenario Roulette" },
            new() { Id = "daily_normal_raid", Type = "daily", CategoryId = "daily_roulette", Label = "Normal Raid Roulette" },
            new() { Id = "daily_frontline", Type = "daily", CategoryId = "daily_roulette", Label = "Frontline Roulette" },

            new() { Id = "daily_beast_tribe", Type = "daily", CategoryId = "daily_quests", Label = "Tribal Quests (0/12)" },
            new() { Id = "daily_gc_turnin", Type = "daily", CategoryId = "daily_quests", Label = "GC Supply & Provisioning" },
            new() { Id = "daily_treasure_map", Type = "daily", CategoryId = "daily_quests", Label = "Gather Daily Treasure Map" },
            new() { Id = "daily_retainer", Type = "daily", CategoryId = "daily_quests", Label = "Retainer Ventures" },

            new() { Id = "daily_cactpot", Type = "daily", CategoryId = "daily_gold_saucer", Label = "Mini Cactpot (x3)" },

            new() { Id = "weekly_tome_cap", Type = "weekly", CategoryId = "weekly_raids", Label = "Cap Current Tomestones (450)" },
            new() { Id = "weekly_savage", Type = "weekly", CategoryId = "weekly_raids", Label = "Current Savage Reclears" },
            new() { Id = "weekly_alliance_coin", Type = "weekly", CategoryId = "weekly_raids", Label = "Current Alliance Raid Coin" },

            new() { Id = "weekly_deliveries", Type = "weekly", CategoryId = "weekly_quests", Label = "Custom Deliveries (0/12)" },
            new() { Id = "weekly_tails", Type = "weekly", CategoryId = "weekly_quests", Label = "Wondrous Tails (Khloe)" },
            new() { Id = "weekly_faux", Type = "weekly", CategoryId = "weekly_quests", Label = "Faux Hollows (Unreal)" },
            new() { Id = "weekly_doman", Type = "weekly", CategoryId = "weekly_quests", Label = "Doman Enclave Reconstruction" },

            new() { Id = "weekly_jumbo_cactpot", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Jumbo Cactpot" },
            new() { Id = "weekly_fashion_report", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Fashion Report (80+ pts)" },
            new() { Id = "weekly_lord_of_verminion", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Lord of Verminion (x5)" },
        };

        private List<RoutineTask> _custom = new();

        public RoutinesApp(HubClient sender, Configuration config)
        {
            _sender = sender;
            _config = config;
            _config.RoutinesChecklist ??= new();
            _config.RoutinesHiddenTasks ??= new();
            _custom = CleanCustom(ParseCustom(_config.RoutinesCustomTasksJson));
        }

        public void Dispose() { }

        public void Update()
        {
            SyncIn();
            CheckLocalResets();
        }

        // =============================================================================================================
        // Data
        // =============================================================================================================
        private IEnumerable<RoutineTask> All => DefaultTasks.Concat(_custom);
        private bool Ticked(string id) => _config.RoutinesChecklist.TryGetValue(id, out var v) && v;
        private bool Hidden(string id) => _config.RoutinesHiddenTasks.Contains(id);
        private bool IsCustom(string id) => _custom.Any(t => t.Id == id);
        private static bool KnownCategory(string cat, string type) => Categories.Any(c => c.Id == cat && c.Type == type);

        /// <summary>Done and total visible tasks for "daily" or "weekly" (website rule: hidden tasks never count).</summary>
        public (int Done, int Total) Progress(string type)
        {
            var list = All.Where(t => t.Type == type && !Hidden(t.Id)).ToList();
            return (list.Count(t => Ticked(t.Id)), list.Count);
        }

        private static JToken? ParseCustom(string? json)
        {
            try { return string.IsNullOrEmpty(json) ? null : JToken.Parse(json); } catch { return null; }
        }

        /// <summary>Custom tasks as {id, type, categoryId, label}; drop anything malformed (website: cleanCustom).</summary>
        private static List<RoutineTask> CleanCustom(JToken? t)
        {
            var list = new List<RoutineTask>();
            if (t is not JArray arr) return list;
            foreach (var o in arr.OfType<JObject>())
            {
                string? S(string a, string b) => (o[a] ?? o[b]) is JValue { Type: JTokenType.String } v ? (string?)v : null;
                var id = S("id", "Id"); var type = S("type", "Type"); var label = S("label", "Label");
                if (id == null || label == null || (type != "daily" && type != "weekly")) continue;
                list.Add(new RoutineTask { Id = id, Type = type, Label = label, CategoryId = S("categoryId", "CategoryId") ?? $"custom_{type}" });
            }
            return list;
        }

        // =============================================================================================================
        // Sync with the website. Each section is re-read when HubClient swaps its token (own writes keep the new one).
        // Config keeps a copy so the list still works signed out.
        // =============================================================================================================
        private JToken? _ticksTok, _hiddenTok, _customTok;

        private void SyncIn()
        {
            if (!_sender.HasToken || _sender.AuthRejected) return;
            bool dirty = false;
            var t = _sender.GetState(SecTicks);
            if (t != null && !ReferenceEquals(t, _ticksTok))
            {
                _ticksTok = t;
                var d = new Dictionary<string, bool>();
                if (t is JObject o)
                    foreach (var p in o.Properties())
                        if (p.Value.Type == JTokenType.Boolean && (bool)p.Value) d[p.Name] = true;
                _config.RoutinesChecklist = d;
                dirty = true;
            }
            t = _sender.GetState(SecHidden);
            if (t != null && !ReferenceEquals(t, _hiddenTok))
            {
                _hiddenTok = t;
                _config.RoutinesHiddenTasks = t is JArray a ? a.Where(x => x.Type == JTokenType.String).Select(x => (string)x!).Distinct().ToList() : new();
                dirty = true;
            }
            t = _sender.GetState(SecCustom);
            if (t != null && !ReferenceEquals(t, _customTok))
            {
                _customTok = t;
                _custom = CleanCustom(t);
                _config.RoutinesCustomTasksJson = JsonConvert.SerializeObject(_custom);
                dirty = true;
            }
            if (dirty) _config.Save();
        }

        private JToken? Write(string section, object value, JToken? old)
        {
            _config.Save();
            _sender.SetState(section, value);
            return _sender.GetState(section) ?? old;
        }

        private void WriteTicks() => _ticksTok = Write(SecTicks, _config.RoutinesChecklist, _ticksTok);
        private void WriteHidden() => _hiddenTok = Write(SecHidden, _config.RoutinesHiddenTasks, _hiddenTok);
        private void WriteCustom()
        {
            _config.RoutinesCustomTasksJson = JsonConvert.SerializeObject(_custom);
            _customTok = Write(SecCustom, _custom, _customTok);
        }

        // ---- actions (same rules as the website) ---------------------------------------------------------------------
        private void Toggle(string id)
        {
            // the website stores only ticked ids
            if (Ticked(id)) _config.RoutinesChecklist.Remove(id); else _config.RoutinesChecklist[id] = true;
            WriteTicks();
        }

        private void ToggleHidden(string id)
        {
            if (!_config.RoutinesHiddenTasks.Remove(id)) _config.RoutinesHiddenTasks.Add(id);
            WriteHidden();
        }

        private void UnhideAll(string type)
        {
            _config.RoutinesHiddenTasks.RemoveAll(id => All.FirstOrDefault(t => t.Id == id)?.Type == type);
            WriteHidden();
        }

        private string? TypeOf(string id)
        {
            if (id.StartsWith("daily_")) return "daily";
            if (id.StartsWith("weekly_")) return "weekly";
            return _custom.FirstOrDefault(t => t.Id == id)?.Type;
        }

        /// <summary>Untick every task of a type (also Settings > Data & resets, like the website).</summary>
        public void ResetChecklist(string type)
        {
            foreach (var k in _config.RoutinesChecklist.Keys.Where(k => TypeOf(k) == type).ToList()) _config.RoutinesChecklist.Remove(k);
            WriteTicks();
        }

        private void SaveDraft(Draft d)
        {
            var label = d.Label.Trim();
            if (label.Length > 80) label = label[..80];
            if (d.Id != null)
            {
                var t = _custom.FirstOrDefault(x => x.Id == d.Id);
                if (t != null) { t.Label = label; t.Type = d.Type; t.CategoryId = d.Category; }
            }
            else _custom.Add(new RoutineTask { Id = $"custom_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", Type = d.Type, CategoryId = d.Category, Label = label });
            WriteCustom();
            _tab = d.Type;
        }

        private void RemoveCustom(string id)
        {
            _custom.RemoveAll(t => t.Id == id);
            WriteCustom();
            // the tick and the hidden flag go with it
            if (_config.RoutinesChecklist.Remove(id)) WriteTicks();
            if (_config.RoutinesHiddenTasks.Remove(id)) WriteHidden();
        }

        /// <summary>Swap with the neighbouring custom task of the same category (built-in tasks stay on top).</summary>
        private void Move(string id, int dir)
        {
            int i = _custom.FindIndex(t => t.Id == id);
            if (i < 0) return;
            var cat = _custom[i].CategoryId;
            int j = i + dir;
            while (j >= 0 && j < _custom.Count && _custom[j].CategoryId != cat) j += dir;
            if (j < 0 || j >= _custom.Count) return;
            (_custom[i], _custom[j]) = (_custom[j], _custom[i]);
            WriteCustom();
        }

        /// <summary>Signed out, the plugin clears ticks itself at the game resets (signed in, the server does it).</summary>
        private void CheckLocalResets()
        {
            long nextDaily = Resets.NextDaily().Ticks, nextWeekly = Resets.NextWeekly().Ticks;
            bool local = !_sender.HasToken || _sender.AuthRejected;
            if (_config.LastDailyResetTime == 0 || _config.LastWeeklyResetTime == 0)
            {
                if (_config.LastDailyResetTime == 0) _config.LastDailyResetTime = nextDaily;
                if (_config.LastWeeklyResetTime == 0) _config.LastWeeklyResetTime = nextWeekly;
                _config.Save();
                return;
            }
            if (_config.LastDailyResetTime < nextDaily - TimeSpan.TicksPerDay && local)
            {
                ResetChecklist("daily");
                _config.LastDailyResetTime = nextDaily;
                _config.Save();
            }
            if (_config.LastWeeklyResetTime < nextWeekly - TimeSpan.TicksPerDay * 7 && local)
            {
                ResetChecklist("weekly");
                _config.LastWeeklyResetTime = nextWeekly;
                _config.Save();
            }
        }

        // =============================================================================================================
        // Settings (Settings > Routines)
        // =============================================================================================================
        public void DrawSettings()
        {
            ImGui.BeginGroup();
            using (Tablet.Fonts.Scale(1.1f))
            {
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.TextColored(Theme.Crystal, "Retainers");
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.Indent(20);
                bool audio = _config.RetainerAudioEnabled;
                if (UIHelper.DrawPremiumSwitchWithText("chk_ret_audio", "Play a sound when all ventures are back", ref audio))
                {
                    _config.RetainerAudioEnabled = audio;
                    _config.Save();
                }
                if (audio)
                {
                    ImGui.Indent(20);
                    bool once = _config.RetainerAudioFireOnce;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_ret_once", "Only once (off: every minute until you visit a bell)", ref once))
                    {
                        _config.RetainerAudioFireOnce = once;
                        _config.Save();
                    }
                    ImGui.Unindent(20);
                }
                ImGui.Unindent(20);
            }
            ImGui.EndGroup();
        }
    }
}
