using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace XIVHubCompanion.Apps
{
    public class RoutineTask
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("categoryId")]
        public string CategoryId { get; set; }
        [JsonProperty("label")]
        public string Label { get; set; }
    }

    public class RoutineCategory
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
    }

    public class RoutinesApp : IApp
    {
        public string Name => "Routines";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.ClipboardCheck).ToString();
        public bool HasSettings => true;
        public void DrawSettings() 
        { 
            ImGui.BeginGroup();
            ImGui.SetWindowFontScale(1.1f);
            
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), "Core Settings");
            ImGui.Dummy(new Vector2(0, 5));
            ImGui.Indent(20);
            
            bool audioEnabled = _config.RetainerAudioEnabled;
            if (UIHelper.DrawPremiumSwitchWithText("chk_ret_audio", "Play Audio Alert on Venture Return", ref audioEnabled))
            {
                _config.RetainerAudioEnabled = audioEnabled;
                _config.Save();
            }
            
            if (audioEnabled)
            {
                ImGui.Indent(20);
                bool fireOnce = _config.RetainerAudioFireOnce;
                if (UIHelper.DrawPremiumSwitchWithText("chk_ret_once", "Fire Only Once", ref fireOnce))
                {
                    _config.RetainerAudioFireOnce = fireOnce;
                    _config.Save();
                }
                ImGui.Unindent(20);
            }
            ImGui.Unindent(20);
            
            ImGui.SetWindowFontScale(1.0f);
            ImGui.EndGroup();
        }
        public void Update() { }

        private readonly DataSender _sender;
        private readonly Configuration _config;

        private List<RoutineCategory> _categories;
        private List<RoutineTask> _defaultTasks;
        private List<RoutineTask> _customTasks = new List<RoutineTask>();

        private string _activeTab = "daily";
        private bool _isEditMode = false;
        public enum ModalType { None, AddTask, ResetConfirm }
        private ModalType _activeModal = ModalType.None;

        private string _customTitle = "";
        private string _customType = "daily";
        private string _customCategory = "";

        private string _dailyCountdown = "";
        private string _dailyLocalTime = "";
        private string _weeklyCountdown = "";
        private string _weeklyLocalTime = "";

        public RoutinesApp(DataSender sender, Configuration config)
        {
            _sender = sender;
            _config = config;

            _categories = new List<RoutineCategory>
            {
                new RoutineCategory { Id = "daily_roulette", Label = "Duty Roulettes", Type = "daily" },
                new RoutineCategory { Id = "daily_quests", Label = "Quests & Delivery", Type = "daily" },
                new RoutineCategory { Id = "daily_gold_saucer", Label = "Gold Saucer", Type = "daily" },
                new RoutineCategory { Id = "weekly_raids", Label = "Raids & Tomestones", Type = "weekly" },
                new RoutineCategory { Id = "weekly_quests", Label = "Deliveries & Logs", Type = "weekly" },
                new RoutineCategory { Id = "weekly_gold_saucer", Label = "Gold Saucer", Type = "weekly" },
                new RoutineCategory { Id = "custom_daily", Label = "Custom Tasks", Type = "daily" },
                new RoutineCategory { Id = "custom_weekly", Label = "Custom Tasks", Type = "weekly" }
            };

            _defaultTasks = new List<RoutineTask>
            {
                new RoutineTask { Id = "daily_expert", Type = "daily", CategoryId = "daily_roulette", Label = "Expert Roulette" },
                new RoutineTask { Id = "daily_leveling", Type = "daily", CategoryId = "daily_roulette", Label = "Leveling Roulette" },
                new RoutineTask { Id = "daily_trials", Type = "daily", CategoryId = "daily_roulette", Label = "Trials Roulette" },
                new RoutineTask { Id = "daily_alliance", Type = "daily", CategoryId = "daily_roulette", Label = "Alliance Raid Roulette" },
                new RoutineTask { Id = "daily_msq", Type = "daily", CategoryId = "daily_roulette", Label = "Main Scenario Roulette" },
                new RoutineTask { Id = "daily_normal_raid", Type = "daily", CategoryId = "daily_roulette", Label = "Normal Raid Roulette" },
                new RoutineTask { Id = "daily_frontline", Type = "daily", CategoryId = "daily_roulette", Label = "Frontline Roulette" },

                new RoutineTask { Id = "daily_beast_tribe", Type = "daily", CategoryId = "daily_quests", Label = "Tribal Quests (0/12)" },
                new RoutineTask { Id = "daily_gc_turnin", Type = "daily", CategoryId = "daily_quests", Label = "GC Supply & Provisioning" },
                new RoutineTask { Id = "daily_treasure_map", Type = "daily", CategoryId = "daily_quests", Label = "Gather Daily Treasure Map" },
                new RoutineTask { Id = "daily_retainer", Type = "daily", CategoryId = "daily_quests", Label = "Retainer Ventures" },

                new RoutineTask { Id = "daily_cactpot", Type = "daily", CategoryId = "daily_gold_saucer", Label = "Mini Cactpot (x3)" },

                new RoutineTask { Id = "weekly_tome_cap", Type = "weekly", CategoryId = "weekly_raids", Label = "Cap Current Tomestones (450)" },
                new RoutineTask { Id = "weekly_savage", Type = "weekly", CategoryId = "weekly_raids", Label = "Current Savage Reclears" },
                new RoutineTask { Id = "weekly_alliance_coin", Type = "weekly", CategoryId = "weekly_raids", Label = "Current Alliance Raid Coin" },

                new RoutineTask { Id = "weekly_deliveries", Type = "weekly", CategoryId = "weekly_quests", Label = "Custom Deliveries (0/12)" },
                new RoutineTask { Id = "weekly_tails", Type = "weekly", CategoryId = "weekly_quests", Label = "Wondrous Tails (Khloe)" },
                new RoutineTask { Id = "weekly_faux", Type = "weekly", CategoryId = "weekly_quests", Label = "Faux Hollows (Unreal)" },
                new RoutineTask { Id = "weekly_doman", Type = "weekly", CategoryId = "weekly_quests", Label = "Doman Enclave Reconstruction" },

                new RoutineTask { Id = "weekly_jumbo_cactpot", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Jumbo Cactpot" },
                new RoutineTask { Id = "weekly_fashion_report", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Fashion Report (80+ pts)" },
                new RoutineTask { Id = "weekly_lord_of_verminion", Type = "weekly", CategoryId = "weekly_gold_saucer", Label = "Lord of Verminion (x5)" }
            };

            LoadCustomTasks();
            SyncStateFromServer();
            _sender.OnServerEventReceived += OnServerEvent;
        }

        private void LoadCustomTasks()
        {
            try
            {
                if (!string.IsNullOrEmpty(_config.RoutinesCustomTasksJson))
                {
                    _customTasks = JsonConvert.DeserializeObject<List<RoutineTask>>(_config.RoutinesCustomTasksJson) ?? new List<RoutineTask>();
                }
            }
            catch { _customTasks = new List<RoutineTask>(); }
        }

        private void OnServerEvent(string type, string data)
        {
            if (type == "clientStateUpdate")
            {
                try
                {
                    var cs = JObject.Parse(data);
                    if (cs["ffxiv-hub-checklist"] != null)
                    {
                        var chk = cs["ffxiv-hub-checklist"].ToString();
                        _config.RoutinesChecklist = JsonConvert.DeserializeObject<Dictionary<string, bool>>(chk) ?? new Dictionary<string, bool>();
                    }
                    if (cs["ffxiv-hub-hidden-tasks"] != null)
                    {
                        var hid = cs["ffxiv-hub-hidden-tasks"].ToString();
                        _config.RoutinesHiddenTasks = JsonConvert.DeserializeObject<List<string>>(hid) ?? new List<string>();
                    }
                    if (cs["ffxiv-hub-custom-tasks"] != null)
                    {
                        _config.RoutinesCustomTasksJson = cs["ffxiv-hub-custom-tasks"].ToString();
                        LoadCustomTasks();
                    }
                }
                catch { }
            }
        }

        private async void SyncStateFromServer()
        {
            var json = await _sender.FetchClientStateAsync();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JObject.Parse(json);
                    if (data["clientState"] != null)
                    {
                        var cs = data["clientState"];
                        if (cs["ffxiv-hub-checklist"] != null)
                        {
                            var chk = cs["ffxiv-hub-checklist"].ToString();
                            _config.RoutinesChecklist = JsonConvert.DeserializeObject<Dictionary<string, bool>>(chk) ?? new Dictionary<string, bool>();
                        }
                        if (cs["ffxiv-hub-hidden-tasks"] != null)
                        {
                            var hid = cs["ffxiv-hub-hidden-tasks"].ToString();
                            _config.RoutinesHiddenTasks = JsonConvert.DeserializeObject<List<string>>(hid) ?? new List<string>();
                        }
                        if (cs["ffxiv-hub-custom-tasks"] != null)
                        {
                            _config.RoutinesCustomTasksJson = cs["ffxiv-hub-custom-tasks"].ToString();
                            LoadCustomTasks();
                        }
                        _config.Save();
                    }
                }
                catch { }
            }
        }

        private void SaveAndPushChecklist()
        {
            _config.Save();
            var json = JsonConvert.SerializeObject(_config.RoutinesChecklist);
            _ = _sender.PushClientStateAsync("ffxiv-hub-checklist", json);
        }

        private void SaveAndPushHidden()
        {
            _config.Save();
            var json = JsonConvert.SerializeObject(_config.RoutinesHiddenTasks);
            _ = _sender.PushClientStateAsync("ffxiv-hub-hidden-tasks", json);
        }

        private void SaveAndPushCustom()
        {
            var json = JsonConvert.SerializeObject(_customTasks);
            _config.RoutinesCustomTasksJson = json;
            _config.Save();
            _ = _sender.PushClientStateAsync("ffxiv-hub-custom-tasks", json);
        }

        private void ResetChecklist(string type)
        {
            var keysToRemove = new List<string>();
            var allTasks = new List<RoutineTask>(_defaultTasks);
            allTasks.AddRange(_customTasks);

            foreach (var kvp in _config.RoutinesChecklist)
            {
                var task = allTasks.Find(t => t.Id == kvp.Key);
                if (task != null && task.Type == type)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _config.RoutinesChecklist.Remove(key);
            }

            SaveAndPushChecklist();
        }

        private void CheckResets()
        {
            var now = DateTime.UtcNow;

            // Daily Reset 15:00 UTC
            var nextDaily = now.Date.AddHours(15);
            if (now >= nextDaily) nextDaily = nextDaily.AddDays(1);
            
            var diffDaily = nextDaily - now;
            _dailyCountdown = $"{(int)diffDaily.TotalHours}h {diffDaily.Minutes}m {diffDaily.Seconds}s";
            _dailyLocalTime = nextDaily.ToLocalTime().ToString("HH:mm");

            long nextDailyTicks = nextDaily.Ticks;
            long dailyResetThreshold = nextDaily.AddDays(-1).Ticks;
            if (_config.LastDailyResetTime == 0)
            {
                _config.LastDailyResetTime = nextDailyTicks;
                _config.Save();
            }
            else if (_config.LastDailyResetTime < dailyResetThreshold)
            {
                ResetChecklist("daily");
                _config.LastDailyResetTime = nextDailyTicks;
                _config.Save();
            }

            // Weekly Reset Tuesday 08:00 UTC
            var nextWeekly = now.Date.AddHours(8);
            int daysUntilTuesday = ((int)DayOfWeek.Tuesday - (int)nextWeekly.DayOfWeek + 7) % 7;
            if (daysUntilTuesday == 0 && now >= nextWeekly) daysUntilTuesday = 7;
            nextWeekly = nextWeekly.AddDays(daysUntilTuesday);

            var diffWeekly = nextWeekly - now;
            _weeklyCountdown = $"{(int)diffWeekly.TotalDays}d {diffWeekly.Hours}h {diffWeekly.Minutes}m";
            _weeklyLocalTime = nextWeekly.ToLocalTime().ToString("ddd HH:mm");

            long nextWeeklyTicks = nextWeekly.Ticks;
            long weeklyResetThreshold = nextWeekly.AddDays(-7).Ticks;
            if (_config.LastWeeklyResetTime == 0)
            {
                _config.LastWeeklyResetTime = nextWeeklyTicks;
                _config.Save();
            }
            else if (_config.LastWeeklyResetTime < weeklyResetThreshold)
            {
                ResetChecklist("weekly");
                _config.LastWeeklyResetTime = nextWeeklyTicks;
                _config.Save();
            }
        }

        private DateTime _lastSync = DateTime.MinValue;

        public void Draw()
        {
            var contentPos = ImGui.GetCursorScreenPos();
            var contentSize = ImGui.GetContentRegionAvail();

            if ((DateTime.UtcNow - _lastSync).TotalSeconds > 15)
            {
                _lastSync = DateTime.UtcNow;
                SyncStateFromServer();
            }

            CheckResets();

            // Custom Top Bar with Toggle
            var cursorStart = contentPos;
            var availWidth = contentSize.X;
            
            // Draw a subtle background panel for the header
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(cursorStart, cursorStart + new Vector2(availWidth, 110), ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 0.8f)), 12f);
            drawList.AddRect(cursorStart, cursorStart + new Vector2(availWidth, 110), ImGui.GetColorU32(new Vector4(0.3f, 0.3f, 0.3f, 0.3f)), 12f);

            ImGui.SetCursorScreenPos(cursorStart + new Vector2(25, 25));
            ImGui.BeginGroup();
            
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), ((char)Dalamud.Interface.FontAwesomeIcon.Tasks).ToString());
            ImGui.PopFont();
            
            ImGui.SameLine(0, 15);
            ImGui.SetWindowFontScale(1.5f);
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), _activeTab == "daily" ? "Daily Routines" : "Weekly Routines");
            ImGui.SetWindowFontScale(1.0f);
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "RESET IN:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), _activeTab == "daily" ? _dailyCountdown : _weeklyCountdown);
            
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "LOCAL TIME:");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), _activeTab == "daily" ? _dailyLocalTime : _weeklyLocalTime);
            
            ImGui.EndGroup();

            // Right side - Progress Circle and Tab Toggles
            var allTasks = new List<RoutineTask>(_defaultTasks);
            allTasks.AddRange(_customTasks);

            var activeTasks = new List<RoutineTask>();
            foreach(var t in allTasks)
            {
                if (t.Type == _activeTab && (_isEditMode || !_config.RoutinesHiddenTasks.Contains(t.Id)))
                {
                    activeTasks.Add(t);
                }
            }

            int completed = 0;
            foreach(var t in activeTasks)
            {
                if (_config.RoutinesChecklist.TryGetValue(t.Id, out bool isChecked) && isChecked)
                {
                    completed++;
                }
            }

            float percent = activeTasks.Count > 0 ? (float)completed / activeTasks.Count : 0f;

            // Progress Circle (Bigger)
            var circleCenter = cursorStart + new Vector2(availWidth - 240, 55);
            float radius = 40f;
            drawList.AddCircle(circleCenter, radius, ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.5f)), 40, 10f);
            
            if (percent > 0)
            {
                float endAngle = (percent * (float)Math.PI * 2f) - ((float)Math.PI / 2f);
                float startAngle = -((float)Math.PI / 2f);
                if (endAngle > startAngle)
                {
                    drawList.PathArcTo(circleCenter, radius, startAngle, endAngle, 40);
                    drawList.PathStroke(ImGui.GetColorU32(new Vector4(0.79f, 0.66f, 0.41f, 1.0f)), ImDrawFlags.None, 10f);
                }
            }

            // Text inside circle
            string percentText = $"{Math.Round(percent * 100)}%";
            var textSize = ImGui.CalcTextSize(percentText);
            ImGui.SetCursorScreenPos(circleCenter - new Vector2(textSize.X / 2, textSize.Y / 2 + 5));
            ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), percentText);
            
            string fractionText = $"{completed}/{activeTasks.Count}";
            var fracSize = ImGui.CalcTextSize(fractionText);
            ImGui.SetCursorScreenPos(circleCenter - new Vector2(fracSize.X / 2, -(textSize.Y / 2 - 5)));
            ImGui.SetWindowFontScale(0.8f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), fractionText);
            ImGui.SetWindowFontScale(1.0f);

            // Tab Toggles
            ImGui.SetCursorScreenPos(cursorStart + new Vector2(availWidth - 140, 25));
            ImGui.BeginGroup();
            
            string[] tabs = new string[] { "Daily", "Weekly" };
            int activeTabIdx = _activeTab == "daily" ? 0 : 1;
            if (UIHelper.DrawPremiumTabSegment(tabs, ref activeTabIdx, 110))
            {
                _activeTab = activeTabIdx == 0 ? "daily" : "weekly";
            }
            
            ImGui.EndGroup();

            // Set cursor below the header
            ImGui.SetCursorScreenPos(cursorStart + new Vector2(0, 130));

            // Controls
            ImGui.BeginGroup();
            
            Vector4 baseBg = new Vector4(0.12f, 0.12f, 0.14f, 1f);
            Vector4 activeBg = new Vector4(0.0f, 0.65f, 1.0f, 1f);
            Vector4 textCol = new Vector4(0.9f, 0.9f, 0.9f, 1f);
            Vector4 activeTextCol = new Vector4(1f, 1f, 1f, 1f);
            
            if (UIHelper.DrawPremiumButton("btn_edit_tasks", ImGui.GetCursorScreenPos(), new Vector2(120, 30), _isEditMode ? "Done Editing" : "Edit Tasks", _isEditMode ? activeBg : baseBg, activeBg, _isEditMode ? activeTextCol : textCol, activeTextCol))
            {
                _isEditMode = !_isEditMode;
            }
            
            ImGui.SameLine(0, 15);
            if (UIHelper.DrawPremiumButton("btn_add_custom", ImGui.GetCursorScreenPos(), new Vector2(120, 30), "Add Custom", baseBg, activeBg, textCol, activeTextCol))
            {
                _activeModal = ModalType.AddTask;
                _customType = _activeTab;
            }
            
            ImGui.SameLine(0, 15);
            if (UIHelper.DrawPremiumWarningButton("btn_reset_prog", ImGui.GetCursorScreenPos(), new Vector2(130, 30), "Reset Progress"))
            {
                _activeModal = ModalType.ResetConfirm;
            }
            ImGui.EndGroup();

            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            // Category Grid - 3 Columns to fill up the free space
            var catList = new List<RoutineCategory>();
            foreach(var cat in _categories)
            {
                if (cat.Type == _activeTab) catList.Add(cat);
            }
            
            // Only keep categories that have active tasks (or if in edit mode, any tasks)
            var validCatList = new List<RoutineCategory>();
            foreach (var cat in catList)
            {
                if (activeTasks.Exists(t => t.CategoryId == cat.Id) || (_isEditMode && allTasks.Exists(t => t.CategoryId == cat.Id && t.Type == _activeTab)))
                {
                    validCatList.Add(cat);
                }
            }

            if (validCatList.Count > 0)
            {
                ImGui.Columns(3, "CategoryGrid", false);

                for (int i = 0; i < validCatList.Count; i++)
                {
                    var cat = validCatList[i];
                    var catTasks = allTasks.FindAll(t => t.CategoryId == cat.Id && t.Type == _activeTab);
                    if (!_isEditMode) catTasks = activeTasks.FindAll(t => t.CategoryId == cat.Id);

                    if (catTasks.Count == 0) continue;

                    UIHelper.BeginSmoothChild($"CatChild_{cat.Id}", new Vector2(0, catTasks.Count * 35 + 40), true, ImGuiWindowFlags.NoScrollbar);
                    
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), ((char)Dalamud.Interface.FontAwesomeIcon.ListUl).ToString());
                    ImGui.PopFont();
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), cat.Label.ToUpper());
                    
                    ImGui.Spacing();

                    foreach(var task in catTasks)
                    {
                        bool isHidden = _config.RoutinesHiddenTasks.Contains(task.Id);
                        bool isChecked = false;
                        _config.RoutinesChecklist.TryGetValue(task.Id, out isChecked);

                        if (_isEditMode)
                        {
                            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                            bool show = !isHidden;
                            
                            // Use Eye icons
                            string icon = show ? ((char)Dalamud.Interface.FontAwesomeIcon.Eye).ToString() : ((char)Dalamud.Interface.FontAwesomeIcon.EyeSlash).ToString();
                            if (!show) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                            else ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 0.65f, 1.0f, 1.0f));
                            
                            if (ImGui.Button($"{icon}###toggle_{task.Id}", new Vector2(24, 24)))
                            {
                                if (show) _config.RoutinesHiddenTasks.Add(task.Id);
                                else _config.RoutinesHiddenTasks.Remove(task.Id);
                                SaveAndPushHidden();
                            }
                            ImGui.PopStyleColor();
                            ImGui.PopFont();
                            
                            ImGui.SameLine();
                            if (isHidden) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                            ImGui.AlignTextToFramePadding();
                            ImGui.Text(task.Label);
                            if (isHidden) ImGui.PopStyleColor();
                            
                            if (task.Id.StartsWith("custom_"))
                            {
                                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30);
                                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.83f, 0.69f, 0.22f, 1.0f));
                                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));
                                if (ImGui.Button($"{((char)Dalamud.Interface.FontAwesomeIcon.Trash)}###del_{task.Id}", new Vector2(24, 24)))
                                {
                                    _customTasks.RemoveAll(t => t.Id == task.Id);
                                    SaveAndPushCustom();
                                }
                                ImGui.PopStyleColor(2);
                                ImGui.PopFont();
                            }
                        }
                        else
                        {
                            bool wasChecked = isChecked;
                            ImGui.PushStyleColor(ImGuiCol.CheckMark, new Vector4(0.79f, 0.66f, 0.41f, 1.0f));
                            if (wasChecked) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                            
                            if (ImGui.Checkbox($"{task.Label}###{task.Id}", ref isChecked))
                            {
                                _config.RoutinesChecklist[task.Id] = isChecked;
                                SaveAndPushChecklist();
                            }
                            
                            if (wasChecked) ImGui.PopStyleColor();
                            ImGui.PopStyleColor();
                        }
                    }
                    ImGui.EndChild();
                    ImGui.NextColumn();
                }
                ImGui.Columns(1);
            }

            if (activeTasks.Count == 0 && !_isEditMode)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("No active tasks found. Click 'Edit Tasks' to unhide some, or add a custom task!");
            }

            DrawRetainers(availWidth);

            DrawModals(contentPos, contentSize);
        }

        private void DrawModals(Vector2 contentPos, Vector2 contentSize)
        {
            bool isAddOpen = _activeModal == ModalType.AddTask;
            bool isResetOpen = _activeModal == ModalType.ResetConfirm;

            if (UIHelper.BeginPremiumModal("Add Custom Task", ref isAddOpen, contentPos, contentSize, new Vector2(400, 310) * PluginUI.AppScale, out float alphaAdd))
            {
                ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f * alphaAdd), "Add Custom Task");
                ImGui.Separator();
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Task Name");
                ImGui.InputText("##TaskName", ref _customTitle, 100);
                
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Frequency");
                if (ImGui.BeginCombo("##Frequency", _customType == "daily" ? "Daily" : "Weekly"))
                {
                    if (ImGui.Selectable("Daily", _customType == "daily")) { _customType = "daily"; _customCategory = ""; }
                    if (ImGui.Selectable("Weekly", _customType == "weekly")) { _customType = "weekly"; _customCategory = ""; }
                    ImGui.EndCombo();
                }

                ImGui.Spacing();

                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Category");
                var typeCats = _categories.FindAll(c => c.Type == _customType);
                string catLabel = "Select Category";
                var selectedCat = typeCats.Find(c => c.Id == _customCategory);
                if (selectedCat != null) catLabel = selectedCat.Label;

                if (ImGui.BeginCombo("##Category", catLabel))
                {
                    foreach (var c in typeCats)
                    {
                        if (ImGui.Selectable(c.Label, _customCategory == c.Id)) _customCategory = c.Id;
                    }
                    ImGui.EndCombo();
                }

                ImGui.Spacing(); ImGui.Spacing();
                
                float availWidth = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorPosX(availWidth - 210 * PluginUI.AppScale); // align right
                
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, alphaAdd);
                Vector4 btnHover = new Vector4(0.2f, 0.2f, 0.25f, alphaAdd);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, alphaAdd);
                
                if (UIHelper.DrawPremiumButton("btn_cancel_add", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Cancel", btnBg, btnHover, btnText, new Vector4(1, 1, 1, alphaAdd)))
                {
                    isAddOpen = false;
                }
                ImGui.SameLine();
                
                bool canAdd = !string.IsNullOrEmpty(_customTitle) && !string.IsNullOrEmpty(_customCategory);
                if (!canAdd) ImGui.BeginDisabled();
                if (UIHelper.DrawPremiumButton("btn_confirm_add", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Add Task", new Vector4(0.0f, 0.65f, 1.0f, alphaAdd), new Vector4(0.2f, 0.75f, 1.0f, alphaAdd), new Vector4(1, 1, 1, alphaAdd), new Vector4(1, 1, 1, alphaAdd)))
                {
                    var id = $"custom_{Guid.NewGuid().ToString().Substring(0, 8)}";
                    _customTasks.Add(new RoutineTask { Id = id, Type = _customType, CategoryId = _customCategory, Label = _customTitle });
                    SaveAndPushCustom();
                    _customTitle = "";
                    isAddOpen = false;
                }
                if (!canAdd) ImGui.EndDisabled();

                UIHelper.EndPremiumModal();
            }

            if (!isAddOpen && _activeModal == ModalType.AddTask) _activeModal = ModalType.None;

            if (UIHelper.BeginPremiumModal("Confirm Reset", ref isResetOpen, contentPos, contentSize, new Vector2(400, 210) * PluginUI.AppScale, out float alphaReset))
            {
                ImGui.TextColored(new Vector4(0.83f, 0.69f, 0.22f, alphaReset), "Confirm Reset");
                ImGui.Separator();
                ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaReset), $"Are you sure you want to reset all {_activeTab} progress?");
                ImGui.TextColored(new Vector4(0.83f, 0.69f, 0.22f, alphaReset), "This action cannot be undone.");
                
                ImGui.Spacing(); ImGui.Spacing();
                
                float availWidth = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorPosX(availWidth - 210 * PluginUI.AppScale);
                
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, alphaReset);
                Vector4 btnHover = new Vector4(0.2f, 0.2f, 0.25f, alphaReset);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, alphaReset);
                
                if (UIHelper.DrawPremiumButton("btn_cancel_reset", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Cancel", btnBg, btnHover, btnText, new Vector4(1, 1, 1, alphaReset)))
                {
                    isResetOpen = false;
                }
                ImGui.SameLine();
                
                if (UIHelper.DrawPremiumWarningButton("btn_confirm_reset", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Yes, Reset"))
                {
                    ResetChecklist(_activeTab);
                    if (_activeTab == "daily") _config.LastDailyResetTime = DateTime.UtcNow.Ticks;
                    else _config.LastWeeklyResetTime = DateTime.UtcNow.Ticks;
                    _config.Save();
                    isResetOpen = false;
                }
                UIHelper.EndPremiumModal();
            }

            if (!isResetOpen && _activeModal == ModalType.ResetConfirm) _activeModal = ModalType.None;
        }

        public void Dispose()
        {
            _sender.OnServerEventReceived -= OnServerEvent;
        }

        private unsafe void DrawRetainers(float availWidth)
        {
            var rm = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (rm == null) return;
            
            // Check active retainers
            int activeCount = 0;
            int returnedCount = 0;
            
            ImGui.Spacing();
            ImGui.Spacing();
            
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), ((char)Dalamud.Interface.FontAwesomeIcon.Briefcase).ToString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.79f, 0.66f, 0.41f, 1.0f), "RETAINER VENTURES");
            ImGui.SameLine();

            float contentWidth = ImGui.GetContentRegionAvail().X;
            int columns = (int)(contentWidth / 170);
            if (columns < 1) columns = 1;
            
            if (ImGui.BeginTable("RetainerTable", columns))
            {
                for (uint i = 0; i < 10; i++)
                {
                    var ret = rm->GetRetainerBySortedIndex(i);
                    if (ret == null) continue;
                    if (string.IsNullOrEmpty(ret->NameString)) continue;
                    
                    activeCount++;
                    ImGui.TableNextColumn();
                    
                    ImGui.BeginGroup();
                    // Retainer Card Background
                    Vector2 cursorPos = ImGui.GetCursorScreenPos();
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(160, 60), UIHelper.Vec4ToU32(new Vector4(0.08f, 0.08f, 0.1f, 0.8f)), 4f);
                    drawList.AddRect(cursorPos, cursorPos + new Vector2(160, 60), UIHelper.Vec4ToU32(new Vector4(0.3f, 0.3f, 0.35f, 0.5f)), 4f);
                    
                    ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 8));
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), ret->NameString);
                    
                    float fullness = Math.Min(1.0f, (float)ret->ItemCount / 175f);
                    ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 28));
                    ImGui.ProgressBar(fullness, new Vector2(140, 6), "");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip($"Inventory: {ret->ItemCount} / 175");
                    }
                    
                    ImGui.SetCursorScreenPos(cursorPos + new Vector2(10, 40));
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (ret->VentureId != 0)
                    {
                        long diff = ret->VentureComplete - now;
                        if (diff <= 0)
                        {
                            ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1f), "Returned!");
                            returnedCount++;
                        }
                        else
                        {
                            var ts = TimeSpan.FromSeconds(diff);
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), $"{ts.Hours:D2}h {ts.Minutes:D2}m {ts.Seconds:D2}s");
                        }
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "No Venture");
                    }
                    
                    ImGui.EndGroup();
                    ImGui.Dummy(new Vector2(160, 5)); // Spacing below group just in case
                }
                ImGui.EndTable();
            }
            
            ImGui.EndChild();
        }
    }
}
