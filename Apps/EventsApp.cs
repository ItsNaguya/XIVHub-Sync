using Dalamud.Bindings.ImGui;
using System.Numerics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Dalamud.Plugin.Services;
using System.Net.Http;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using System.IO;

namespace XIVHubCompanion.Apps
{
    public class EventsApp : IApp
    {
        public string Name => "Events";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.CalendarAlt).ToString(); public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }

        private DataSender _sender;
        private IObjectTable _objectTable;
        private IPluginLog _log;
        private ITextureProvider _textureProvider;
        private IDalamudPluginInterface _pluginInterface;
        
        private static readonly HttpClient _httpClient = new HttpClient();
        private Dictionary<string, ISharedImmediateTexture> _imageCache = new Dictionary<string, ISharedImmediateTexture>();

        private string _importUrl = "";
        private string _customTitle = "";
        private string _customLocation = "";
        private string _customDescription = "";
        private string _customImage = "";
        private string _customUrl = "";
        private bool _customIsWeekly = false;
        
        private int _customYear = DateTime.Now.Year;
        private int _customMonth = DateTime.Now.Month;
        private int _customDay = DateTime.Now.Day;
        private int _customHour = 12;
        private int _customMinute = 0;
        private bool _isImporting = false;
        private bool _isSyncing = false;
        private bool _isFetching = false;
        private string _syncMessage = "";

        private List<dynamic> _upcomingEvents = new List<dynamic>();
        private List<dynamic> _venues = new List<dynamic>();
        
        public enum ModalType { None, EventDetails, AddEvent, Venues }
        private ModalType _activeModal = ModalType.None;
        private ModalType _lastActiveModal = ModalType.None;
        private float _modalAlpha = 0f;
        
        private dynamic _selectedEvent = null;
        
        private int _currentMonth = DateTime.Now.Month;
        private int _currentYear = DateTime.Now.Year;
        
        private bool _initialFetchDone = false;

        public EventsApp(DataSender sender, IObjectTable objectTable, IPluginLog log, ITextureProvider textureProvider, IDalamudPluginInterface pluginInterface)
        {
            _sender = sender;
            _objectTable = objectTable;
            _log = log;
            _textureProvider = textureProvider;
            _pluginInterface = pluginInterface;
        }

        private ISharedImmediateTexture GetEventImageWrap(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            
            if (_imageCache.TryGetValue(url, out var tex)) return tex;
            
            _imageCache[url] = null; // Prevent spam
            
            Task.Run(async () => {
                try {
                    string cacheDir = Path.Combine(_pluginInterface.ConfigDirectory.FullName, "ImageCache");
                    Directory.CreateDirectory(cacheDir);
                    
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
                    string fileName = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant() + ".png";
                    string filePath = Path.Combine(cacheDir, fileName);
                    
                    if (!File.Exists(filePath)) {
                        var bytes = await _httpClient.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(filePath, bytes);
                    }
                    
                    var texture = _textureProvider.GetFromFile(new FileInfo(filePath));
                    _imageCache[url] = texture;
                } catch (Exception ex) {
                    _log.Error(ex, $"Failed to load image from URL: {url}");
                }
            });
            
            return null;
        }

        private (string name, string world) GetPlayerContext()
        {
            var localPlayer = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            if (localPlayer == null) return (null, null);
            return (localPlayer.Name.ToString(), localPlayer.HomeWorld.Value.Name.ExtractText());
        }

        private void FetchData(string name, string world)
        {
            if (_isFetching) return;
            if (name == null) return;
            
            _isFetching = true;
            Task.Run(async () =>
            {
                try 
                {

                    var offTask = _sender.FetchLodestoneEventsAsync();
                    var comTask = _sender.FetchEventsAsync(name, world);
                    await Task.WhenAll(offTask, comTask);
                    
                    _upcomingEvents.Clear();

                    if (offTask.Result != null) {
                        try {
                            var offRes = JObject.Parse(offTask.Result);
                            var offEventsArr = offRes["events"] as JArray;
                            if (offEventsArr != null) {
                                foreach(var ev in offEventsArr) {
                                    ev["type"] = "official";
                                    ev["source"] = "lodestone";
                                    ev["startDate"] = ev["date"];
                                    _upcomingEvents.Add(ev);
                                }
                            }
                        } catch {}
                    }

                    string eventsJson = comTask.Result;
                    if (eventsJson != null)
                    {
                        try {
                            var res = JObject.Parse(eventsJson);
                            var eventsArr = res["events"] as JArray;
                            if (eventsArr != null)
                            {
                                long horizonTicks = DateTime.UtcNow.AddDays(180).Ticks;
                                foreach(var ev in eventsArr) {
                                    _upcomingEvents.Add(ev);
                                    
                                    bool isWeekly = false;
                                    try { isWeekly = (bool)ev["isWeekly"]; } catch {}
                                    
                                    if (isWeekly)
                                    {
                                        DateTime? startDate = null;
                                        try { startDate = (DateTime?)ev["startDate"]; } catch {}
                                        
                                        if (startDate.HasValue)
                                        {
                                            DateTime nextTime = startDate.Value.AddDays(7);
                                            int iterations = 0;
                                            while (nextTime.Ticks < horizonTicks && iterations < 50)
                                            {
                                                var clonedEv = (JObject)((JObject)ev).DeepClone();
                                                clonedEv["startDate"] = nextTime.ToString("O");
                                                _upcomingEvents.Add(clonedEv);
                                                
                                                nextTime = nextTime.AddDays(7);
                                                iterations++;
                                            }
                                        }
                                    }
                                }
                            }
                        } catch { }
                    }

                    string venuesJson = await _sender.GetVenuesAsync(name, world);
                    if (venuesJson != null)
                    {
                        try {
                            var res = JObject.Parse(venuesJson);
                            var venuesArr = res["subscriptions"] as JArray;
                            _venues.Clear();
                            if (venuesArr != null)
                            {
                                foreach(var v in venuesArr) {
                                    _venues.Add(v);
                                }
                            }
                        } catch { }
                    }
                } 
                finally 
                {
                    _isFetching = false;
                }
            });
        }

        public void Draw()
        {
            if (!_initialFetchDone && !_isFetching) {
                var (name, world) = GetPlayerContext();
                if (name != null) {
                    _initialFetchDone = true;
                    FetchData(name, world); // First load
                }
            }

            var startPos = ImGui.GetCursorScreenPos();
            var winSize = ImGui.GetContentRegionAvail();
            
            // Layout: Split left (Sidebar), right (Main) dynamically based on available width
            float sidebarWidth = Math.Max(PluginUI.Scaled(150f), winSize.X * 0.25f);
            
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8) * PluginUI.AppScale);
            UIHelper.BeginSmoothChild("EventsSidebar", new Vector2(sidebarWidth, winSize.Y), true);
            DrawSidebar();
            ImGui.EndChild();
            
            ImGui.SameLine();
            
            UIHelper.BeginSmoothChild("EventsMain", new Vector2(winSize.X - sidebarWidth - 10, winSize.Y), true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            DrawMainContent();
            ImGui.EndChild();
            ImGui.PopStyleVar();
            
            DrawModals(startPos, winSize);
        }

        private void DrawSidebar()
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Actions");
            ImGui.Separator();
            ImGui.Spacing();

            Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            
            if (UIHelper.DrawPremiumButton("btn_add_event", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 35 * PluginUI.AppScale), "Add Custom Event", btnBg, btnHover, btnText, btnHoverText))
            {
                _activeModal = ModalType.AddEvent;
            }

            ImGui.Spacing(); ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Import from Partake.gg");
            UIHelper.DrawPremiumInputText("input_partake", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 25 * PluginUI.AppScale), ref _importUrl, 255);
            ImGui.Spacing();
            
            if (UIHelper.DrawPremiumButton("btn_import_event", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 35 * PluginUI.AppScale), _isImporting ? "Importing..." : "Import Venue/Event", btnBg, btnHover, btnText, btnHoverText))
            {
                if (!string.IsNullOrWhiteSpace(_importUrl) && !_isImporting)
                {
                    var (name, world) = GetPlayerContext();
                    if (name != null)
                    {
                        _isImporting = true;
                        _syncMessage = "Importing...";
                        Task.Run(async () =>
                        {
                            var res = await _sender.ImportPartakeAsync(_importUrl, name, world);
                            if (res != null) {
                                _syncMessage = "Imported successfully!";
                                _importUrl = "";
                                FetchData(name, world);
                            } else {
                                _syncMessage = "Import failed. Check logs.";
                            }
                            _isImporting = false;
                        });
                    }
                }
            }

            ImGui.Spacing(); ImGui.Spacing();
            
            float halfWidth = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;

            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            if (UIHelper.DrawPremiumButton("btn_scan_updates", ImGui.GetCursorScreenPos(), new Vector2(halfWidth, 35 * PluginUI.AppScale), _isSyncing ? "\uf254" : "\uf021", btnBg, btnHover, btnText, btnHoverText))
            {
                if (!_isSyncing)
                {
                    var (name, world) = GetPlayerContext();
                    if (name != null)
                    {
                        _isSyncing = true;
                        _syncMessage = "Scanning...";
                        Task.Run(async () =>
                        {
                            var res = await _sender.SyncVenuesAsync(name, world);
                            if (res != null && res.Contains("Rate limited")) {
                                _syncMessage = "Rate limited. Wait 5 mins.";
                            } else if (res != null) {
                                _syncMessage = "Synced!";
                                FetchData(name, world);
                            } else {
                                _syncMessage = "Sync failed.";
                            }
                            _isSyncing = false;
                            
                            // Clear message after 3 seconds
                            await Task.Delay(3000);
                            if (_syncMessage == "Synced!" || _syncMessage == "Sync failed.") _syncMessage = "";
                        });
                    }
                }
            }
            ImGui.PopFont();
            UIHelper.DrawTooltip("Scan for new Venue Updates");

            ImGui.SameLine();
            
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            if (UIHelper.DrawPremiumButton("btn_open_venues", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 35 * PluginUI.AppScale), "\uf041", btnBg, btnHover, btnText, btnHoverText))
            {
                _activeModal = ModalType.Venues;
            }
            ImGui.PopFont();
            UIHelper.DrawTooltip($"View Saved Venues ({_venues.Count})");

            if (!string.IsNullOrEmpty(_syncMessage))
            {
                ImGui.Spacing();
                ImGui.TextWrapped(_syncMessage);
            }
            
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            DrawUpcomingEvents();
        }

        private void DrawUpcomingEvents()
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Upcoming Events");
            ImGui.Spacing();

            if (_isFetching)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Loading...");
                return;
            }

            // Filter and sort events
            var todayStart = DateTime.Now.Date;
            var upcoming = new List<dynamic>();
            foreach (var ev in _upcomingEvents)
            {
                DateTime? evDate = null;
                try { evDate = (DateTime?)ev["startDate"]; } catch { }
                if (evDate.HasValue && evDate.Value.ToLocalTime().Date >= todayStart)
                {
                    upcoming.Add(ev);
                }
            }

            upcoming.Sort((a, b) => {
                DateTime dateA = (DateTime)a["startDate"];
                DateTime dateB = (DateTime)b["startDate"];
                return dateA.CompareTo(dateB);
            });

            if (upcoming.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No upcoming events.");
                return;
            }

            UIHelper.BeginSmoothChild("UpcomingEventsList", new Vector2(-1, -1) * PluginUI.AppScale, false);

            int displayed = 0;
            var seenIds = new HashSet<string>();
            foreach (var ev in upcoming)
            {
                string id = (string)ev["id"] ?? "";
                if (!string.IsNullOrEmpty(id)) {
                    if (seenIds.Contains(id)) continue;
                    seenIds.Add(id);
                }

                if (displayed >= 10) break;
                
                string title = (string)ev["title"] ?? "Unknown";
                string source = (string)ev["source"] ?? "";
                string img = (string)ev["image"] ?? "";
                DateTime? dt = null;
                try { dt = (DateTime?)ev["startDate"]; } catch { }

                string timeStr = dt.HasValue ? dt.Value.ToLocalTime().ToString("MMM d, yyyy HH:mm") : "";
                
                // Draw Card
                var cursorPos = ImGui.GetCursorScreenPos();
                float width = ImGui.GetContentRegionAvail().X;
                float imgHeight = PluginUI.Scaled(60f);
                float totalHeight = imgHeight + PluginUI.Scaled(65f);
                
                // Dark background
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(width, totalHeight), UIHelper.Vec4ToU32(new Vector4(0, 0, 0, 0.3f)), 8f);
                
                // Outline based on type
                Vector4 borderColor;
                if (source == "lodestone") borderColor = new Vector4(0.3f, 0.6f, 0.4f, 0.3f);
                else if (source == "partake") borderColor = new Vector4(0.2f, 0.5f, 0.8f, 0.3f);
                else borderColor = new Vector4(0.8f, 0.3f, 0.5f, 0.3f);
                
                drawList.AddRect(cursorPos, cursorPos + new Vector2(width, totalHeight), UIHelper.Vec4ToU32(borderColor), 8f);

                string idStr = (string)ev["id"];
                if (string.IsNullOrEmpty(idStr)) idStr = title;
                ImGui.PushID(idStr);
                ImGui.InvisibleButton("##EventCard", new Vector2(width, totalHeight));
                bool hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemClicked())
                {
                    _selectedEvent = ev;
                    _activeModal = ModalType.EventDetails;
                }
                
                if (hovered) {
                    drawList.AddRectFilled(cursorPos, cursorPos + new Vector2(width, totalHeight), UIHelper.Vec4ToU32(new Vector4(1, 1, 1, 0.05f)), 8f);
                }

                // Render image if available
                if (!string.IsNullOrEmpty(img))
                {
                    var tex = GetEventImageWrap(img);
                    if (tex != null)
                    {
                        var wrap = tex.GetWrapOrDefault();
                        if (wrap != null)
                        {
                            float imgAspect = (float)wrap.Width / wrap.Height;
                            float targetAspect = width / imgHeight;
                            Vector2 uv0 = new Vector2(0, 0);
                            Vector2 uv1 = new Vector2(1, 1);
                            if (imgAspect > targetAspect) {
                                float cropWidth = targetAspect / imgAspect;
                                float cropMargin = (1f - cropWidth) / 2f;
                                uv0.X = cropMargin;
                                uv1.X = 1f - cropMargin;
                            } else if (imgAspect < targetAspect) {
                                float cropHeight = imgAspect / targetAspect;
                                float cropMargin = (1f - cropHeight) / 2f;
                                uv0.Y = cropMargin;
                                uv1.Y = 1f - cropMargin;
                            }
                            drawList.AddImageRounded(wrap.Handle, cursorPos, cursorPos + new Vector2(width, imgHeight), uv0, uv1, 0xFFFFFFFF, 8f);
                        }
                    }
                }

                ImGui.SetCursorScreenPos(cursorPos + new Vector2(PluginUI.Scaled(10f), imgHeight + PluginUI.Scaled(5f)));
                Vector4 labelColor;
                string labelText;
                if (source == "lodestone") { labelColor = new Vector4(0.5f, 0.9f, 0.5f, 1f); labelText = "OFFICIAL"; }
                else if (source == "partake") { labelColor = new Vector4(0.4f, 0.7f, 1f, 1f); labelText = "COMMUNITY"; }
                else { labelColor = new Vector4(1f, 0.5f, 0.7f, 1f); labelText = "COMMUNITY"; }
                
                ImGui.TextColored(labelColor, labelText);
                
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(PluginUI.Scaled(10f), imgHeight + PluginUI.Scaled(24f)));
                string displayTitle = title.Length > 28 ? title.Substring(0, 25) + "..." : title;
                ImGui.Text(displayTitle);
                
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(PluginUI.Scaled(10f), imgHeight + PluginUI.Scaled(43f)));
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), timeStr);
                
                ImGui.SetCursorScreenPos(cursorPos + new Vector2(0, totalHeight + 10));
                ImGui.PopID();
                displayed++;
            }

            ImGui.EndChild();
        }

        private void DrawMainContent()
        {
            var winSize = ImGui.GetContentRegionAvail();
            
            // Header: Month Year | Today | < | >
            string monthName = new DateTime(_currentYear, _currentMonth, 1).ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), $"\uf073 {monthName}");
            
            float rightOffset = ImGui.GetWindowContentRegionMax().X - 120f;
            ImGui.SameLine(rightOffset);
            
            Vector4 navBtnBg = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
            Vector4 navBtnHover = new Vector4(0.0f, 0.65f, 1.0f, 1.0f);
            Vector4 navBtnText = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            
            if (UIHelper.DrawPremiumButton("btn_today", ImGui.GetCursorScreenPos(), new Vector2(50, 25) * PluginUI.AppScale, "Today", navBtnBg, navBtnHover, navBtnText, navBtnText))
            {
                _currentMonth = DateTime.Now.Month;
                _currentYear = DateTime.Now.Year;
            }
            ImGui.SameLine();
            if (UIHelper.DrawPremiumButton("btn_prev_mo", ImGui.GetCursorScreenPos(), new Vector2(25, 25) * PluginUI.AppScale, "<", navBtnBg, navBtnHover, navBtnText, navBtnText))
            {
                _currentMonth--;
                if (_currentMonth < 1) { _currentMonth = 12; _currentYear--; }
            }
            ImGui.SameLine();
            if (UIHelper.DrawPremiumButton("btn_next_mo", ImGui.GetCursorScreenPos(), new Vector2(25, 25) * PluginUI.AppScale, ">", navBtnBg, navBtnHover, navBtnText, navBtnText))
            {
                _currentMonth++;
                if (_currentMonth > 12) { _currentMonth = 1; _currentYear++; }
            }

            ImGui.Separator();
            ImGui.Spacing();

            if (_isFetching)
            {
                ImGui.Text("Loading events...");
                return;
            }

            if (ImGui.BeginTable("CalendarGrid", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
            {
                string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
                foreach (var d in days)
                {
                    ImGui.TableSetupColumn(d, ImGuiTableColumnFlags.WidthStretch);
                }
                ImGui.TableHeadersRow();

                var firstDayOfMonth = new DateTime(_currentYear, _currentMonth, 1);
                int daysInMonth = DateTime.DaysInMonth(_currentYear, _currentMonth);
                
                // DayOfWeek in C#: Sunday = 0, Monday = 1... Saturday = 6
                // We want Monday = 0... Sunday = 6
                int startOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;
                
                int totalCells = startOffset + daysInMonth;
                int rows = (int)Math.Ceiling(totalCells / 7.0f);
                if (rows < 5) rows = 5; // Usually calendars show at least 5 rows
                
                float cellHeight = ImGui.GetContentRegionAvail().Y / rows;
                if (cellHeight < PluginUI.Scaled(80f)) cellHeight = PluginUI.Scaled(80f);

                int currentDay = 1;
                for (int r = 0; r < rows; r++)
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, cellHeight);
                    for (int c = 0; c < 7; c++)
                    {
                        ImGui.TableNextColumn();
                        
                        int cellIndex = r * 7 + c;
                        if (cellIndex >= startOffset && currentDay <= daysInMonth)
                        {
                            var cellDate = new DateTime(_currentYear, _currentMonth, currentDay);
                            
                            // Highlight today
                            bool isToday = cellDate.Date == DateTime.Now.Date;
                            if (isToday)
                            {
                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.85f, 0.4f, 1f));
                            }
                            
                            float startY = ImGui.GetCursorPosY();
                            string dayStr = currentDay.ToString();
                            float dayTextWidth = ImGui.CalcTextSize(dayStr).X;
                            float colWidth = ImGui.GetColumnWidth();
                            
                            // Position to top right of the cell, force Y to prevent cascading drift
                            float destX = ImGui.GetCursorPosX() + colWidth - dayTextWidth - 5;
                            if (destX > ImGui.GetCursorPosX()) {
                                ImGui.SetCursorPos(new Vector2(destX, startY));
                            }
                            
                            ImGui.Text(dayStr);
                            if (isToday) ImGui.PopStyleColor();

                            // Force Y for the first event chip to be exactly below the text
                            ImGui.SetCursorPosY(startY + ImGui.GetTextLineHeight() + 5);

                            // Filter events for this day
                            foreach (var ev in _upcomingEvents)
                            {
                                DateTime? evDate = null;
                                try { evDate = (DateTime?)ev["startDate"]; } catch { }
                                
                                if (evDate.HasValue)
                                {
                                    if (evDate.Value.ToLocalTime().Date == cellDate.Date)
                                    {
                                        DrawEventChip(ev);
                                    }
                                }
                            }

                            currentDay++;
                        }
                    }
                }
                ImGui.EndTable();
            }
        }
        
        private void DrawEventChip(dynamic ev)
        {
            string title = (string)ev["title"] ?? "Unknown";
            string location = (string)ev["location"] ?? "";
            string source = (string)ev["source"] ?? "";
            
            string timeStr = "";
            DateTime? dt = null;
            try { dt = (DateTime?)ev["startDate"]; } catch { }
            
            if (dt.HasValue) {
                timeStr = dt.Value.ToLocalTime().ToString("HH:mm");
            }
            
            // Colors based on source
            Vector4 color;
            if (source == "lodestone") color = new Vector4(0.3f, 0.6f, 0.4f, 1f);
            else if (source == "partake") color = new Vector4(0.2f, 0.5f, 0.8f, 1f);
            else color = new Vector4(0.8f, 0.3f, 0.5f, 1f);
            
            Vector4 hoverColor = new Vector4(color.X*1.2f, color.Y*1.2f, color.Z*1.2f, 1f);
            
            // Use a short title for the chip
            string displayTitle = title;
            
            string idStr = (string)ev["id"];
            if (string.IsNullOrEmpty(idStr)) idStr = title;
            
            ImGui.PushID(idStr);
            if (UIHelper.DrawPremiumButton("btn_chip", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetColumnWidth() - 4, 20), $"{timeStr} {displayTitle}", color, hoverColor, new Vector4(1,1,1,1), new Vector4(1,1,1,1), false))
            {
                _selectedEvent = ev;
                _activeModal = ModalType.EventDetails;
            }
            ImGui.PopID();
            
            if (ImGui.IsItemHovered())
            {
                UIHelper.BeginTooltip();
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), title);
                ImGui.Separator();
                ImGui.Text($"Time: {timeStr}");
                ImGui.Text($"Location: {location}");
                if (ev["description"] != null) {
                    ImGui.Spacing();
                    ImGui.PushTextWrapPos(400f);
                    ImGui.TextWrapped((string)ev["description"]);
                    ImGui.PopTextWrapPos();
                }
                UIHelper.EndTooltip();
            }
        }

        private void DrawModals(Vector2 contentPos, Vector2 contentSize)
        {
            bool isEventDetailsOpen = _activeModal == ModalType.EventDetails;
            bool isAddEventOpen = _activeModal == ModalType.AddEvent;
            bool isVenuesOpen = _activeModal == ModalType.Venues;

            if (UIHelper.BeginPremiumModal("Event Details", ref isEventDetailsOpen, contentPos, contentSize, new Vector2(500, 500) * PluginUI.AppScale, out float alphaEv))
            {
                if (_selectedEvent != null)
                {
                    float currentSizeY = Math.Min(500 * PluginUI.AppScale, contentSize.Y - 20f * PluginUI.AppScale) * (0.95f + 0.05f * alphaEv);
                    float contentHeight = currentSizeY - 40f * PluginUI.AppScale;
                    float buttonRowHeight = 55f * PluginUI.AppScale;
                    
                    // Text Area Child (No Scrollbar)
                    UIHelper.BeginSmoothChild("EventDetailsText", new Vector2(0, contentHeight - buttonRowHeight), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
                    
                    string title = (string)_selectedEvent["title"] ?? "Unknown Event";
                    string desc = (string)_selectedEvent["description"] ?? "No description provided.";
                    string loc = (string)_selectedEvent["location"] ?? "Unknown Location";
                    string img = (string)_selectedEvent["image"] ?? "";
                    
                    float availWidth = ImGui.GetContentRegionAvail().X;
                    
                    if (!string.IsNullOrEmpty(img))
                    {
                        var tex = GetEventImageWrap(img);
                        if (tex != null)
                        {
                            var wrap = tex.GetWrapOrDefault();
                            if (wrap != null)
                            {
                                float aspect = (float)wrap.Width / wrap.Height;
                                float w = availWidth;
                                float h = w / aspect;
                                // Limit max height so it doesn't take up the whole modal
                                if (h > 180f * PluginUI.AppScale) {
                                    h = 180f * PluginUI.AppScale;
                                    w = h * aspect;
                                }
                                // Center Image
                                ImGui.SetCursorPosX((availWidth - w) * 0.5f);
                                ImGui.Image(wrap.Handle, new Vector2(w, h));
                                ImGui.Spacing(); ImGui.Spacing();
                            }
                        }
                    }
                    
                    // Center Title
                    ImGui.SetWindowFontScale(1.2f);
                    float titleWidth = ImGui.CalcTextSize(title).X;
                    if (titleWidth < availWidth) ImGui.SetCursorPosX((availWidth - titleWidth) * 0.5f);
                    ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, alphaEv), title);
                    ImGui.SetWindowFontScale(1.0f);
                    
                    ImGui.Separator();
                    
                    // Center Location
                    string locStr = $"Location: {loc}";
                    float locWidth = ImGui.CalcTextSize(locStr).X;
                    if (locWidth < availWidth) ImGui.SetCursorPosX((availWidth - locWidth) * 0.5f);
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, alphaEv), locStr);
                    ImGui.Spacing();
                    
                    ImGui.PushTextWrapPos(availWidth);
                    ImGui.TextColored(new Vector4(1, 1, 1, alphaEv), desc);
                    ImGui.PopTextWrapPos();
                    
                    // Draw gradient over the bottom of the text to fade it out
                    var dl = ImGui.GetWindowDrawList();
                    var childMin = ImGui.GetWindowPos();
                    var childMax = childMin + ImGui.GetWindowSize();
                    Vector4 bgColor = new Vector4(0.04f, 0.05f, 0.08f, 0.98f * alphaEv);
                    dl.AddRectFilledMultiColor(
                        new Vector2(childMin.X, childMax.Y - 60f * PluginUI.AppScale), 
                        childMax,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 0f)),
                        ImGui.ColorConvertFloat4ToU32(new Vector4(bgColor.X, bgColor.Y, bgColor.Z, 0f)),
                        ImGui.ColorConvertFloat4ToU32(bgColor),
                        ImGui.ColorConvertFloat4ToU32(bgColor)
                    );
                    
                    ImGui.EndChild();
                    
                    // Buttons at the bottom
                    ImGui.SetCursorPosY(contentHeight - buttonRowHeight + 5 * PluginUI.AppScale);
                    ImGui.Separator();
                    ImGui.Spacing();
                    
                    string id = (string)_selectedEvent["id"];
                    string source = (string)_selectedEvent["source"] ?? "";
                    string sourceUrl = (string)_selectedEvent["sourceUrl"] ?? (string)_selectedEvent["url"];
                    
                    bool hasDelete = (source == "manual" || source == "partake") && !string.IsNullOrEmpty(id);
                    bool hasUrl = !string.IsNullOrEmpty(sourceUrl);
                    
                    float spacing = ImGui.GetStyle().ItemSpacing.X;
                    float totalButtonWidth = 0;
                    if (hasDelete) totalButtonWidth += 80 * PluginUI.AppScale + spacing;
                    if (hasUrl) totalButtonWidth += 130 * PluginUI.AppScale + spacing;
                    totalButtonWidth += 90 * PluginUI.AppScale; // Close button
                    
                    ImGui.SetCursorPosX((ImGui.GetContentRegionAvail().X - totalButtonWidth) * 0.5f);
                    
                    Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, alphaEv);
                    Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, alphaEv);
                    Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, alphaEv);
                    Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, alphaEv);

                    if (hasDelete) {
                        var (n, w) = GetPlayerContext();
                        Vector4 delBg = new Vector4(0.83f, 0.69f, 0.22f, alphaEv);
                        if (UIHelper.DrawPremiumButton("btn_del_ev", ImGui.GetCursorScreenPos(), new Vector2(80, 25) * PluginUI.AppScale, "Delete", delBg, btnHover, btnText, btnHoverText)) {
                            System.Threading.Tasks.Task.Run(async () => {
                                await _sender.DeleteCustomEventAsync(id);
                                FetchData(n, w);
                            });
                            isEventDetailsOpen = false;
                        }
                        ImGui.SameLine(0, 10 * PluginUI.AppScale);
                    }
                    
                    if (hasUrl) {
                        if (UIHelper.DrawPremiumButton("btn_orig_url", ImGui.GetCursorScreenPos(), new Vector2(130, 25) * PluginUI.AppScale, "Original Page", btnBg, btnHover, btnText, btnHoverText)) {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(sourceUrl) { UseShellExecute = true }); } catch {}
                        }
                        ImGui.SameLine(0, 10 * PluginUI.AppScale);
                    }
                    
                    if (UIHelper.DrawPremiumButton("btn_close_ev", ImGui.GetCursorScreenPos(), new Vector2(90, 25) * PluginUI.AppScale, "Close", btnBg, btnHover, btnText, btnHoverText))
                    {
                        isEventDetailsOpen = false;
                    }
                }
                UIHelper.EndPremiumModal();
            }

            if (!isEventDetailsOpen && _activeModal == ModalType.EventDetails) _activeModal = ModalType.None;

            if (UIHelper.BeginPremiumModal("Add Custom Event", ref isAddEventOpen, contentPos, contentSize, new Vector2(420, 520) * PluginUI.AppScale, out float alphaAdd))
            {
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, alphaAdd), "Add Custom Event");
                ImGui.Separator(); ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Event Title*");
                UIHelper.DrawPremiumInputText("in_title", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 25), ref _customTitle, 100);
                ImGui.Dummy(new Vector2(0, 25) * PluginUI.AppScale); ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Location*");
                UIHelper.DrawPremiumInputText("in_loc", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 25), ref _customLocation, 100);
                ImGui.Dummy(new Vector2(0, 25) * PluginUI.AppScale); ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Description");
                ImGui.InputTextMultiline("##Description", ref _customDescription, 1000, new Vector2(-1, 80) * PluginUI.AppScale);
                ImGui.Spacing();

                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Image URL");
                UIHelper.DrawPremiumInputText("in_img", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 25), ref _customImage, 255);
                ImGui.Dummy(new Vector2(0, 25) * PluginUI.AppScale); ImGui.Spacing();
                
                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Event Link (Discord, Lodestone, etc.)");
                UIHelper.DrawPremiumInputText("in_url", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 25), ref _customUrl, 255);
                ImGui.Dummy(new Vector2(0, 25) * PluginUI.AppScale); ImGui.Spacing();

                ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Date & Time*");
                ImGui.PushItemWidth(40 * PluginUI.AppScale);
                ImGui.InputInt("##Day", ref _customDay, 0, 0); ImGui.SameLine(); ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "."); ImGui.SameLine();
                ImGui.InputInt("##Month", ref _customMonth, 0, 0); ImGui.SameLine(); ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "."); ImGui.SameLine();
                ImGui.PushItemWidth(50 * PluginUI.AppScale);
                ImGui.InputInt("##Year", ref _customYear, 0, 0); ImGui.SameLine(); ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "  "); ImGui.SameLine();
                ImGui.PopItemWidth();
                ImGui.InputInt("##Hour", ref _customHour, 0, 0); ImGui.SameLine(); ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), ":"); ImGui.SameLine();
                ImGui.InputInt("##Minute", ref _customMinute, 0, 0);
                ImGui.PopItemWidth();
                
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80 * PluginUI.AppScale);
                UIHelper.DrawPremiumCheckbox("chk_weekly", ImGui.GetCursorScreenPos(), ref _customIsWeekly);
                ImGui.SameLine(); ImGui.TextColored(new Vector4(1, 1, 1, alphaAdd), "Weekly");
                
                ImGui.Spacing(); ImGui.Spacing();
                
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, alphaAdd);
                Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, alphaAdd);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, alphaAdd);
                Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, alphaAdd);

                if (UIHelper.DrawPremiumButton("btn_save_event", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Save", btnBg, btnHover, btnText, btnHoverText))
                {
                    string isoDate;
                    try {
                        isoDate = new DateTime(_customYear, _customMonth, _customDay, _customHour, _customMinute, 0).ToString("O");
                    } catch {
                        isoDate = DateTime.Now.ToString("O");
                    }

                    var (name, world) = GetPlayerContext();
                    var payload = new {
                        title = _customTitle,
                        location = _customLocation,
                        startDate = isoDate,
                        description = _customDescription,
                        image = _customImage,
                        sourceUrl = _customUrl,
                        isWeekly = _customIsWeekly,
                        name = name,
                        world = world
                    };
                    System.Threading.Tasks.Task.Run(async () => {
                        await _sender.CreateCustomEventAsync(payload);
                        FetchData(name, world);
                    });
                    isAddEventOpen = false;
                }
                ImGui.SameLine(0, 10 * PluginUI.AppScale);
                if (UIHelper.DrawPremiumButton("btn_cancel_event", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Cancel", btnBg, btnHover, btnText, btnHoverText)) { isAddEventOpen = false; }
                UIHelper.EndPremiumModal();
            }

            if (!isAddEventOpen && _activeModal == ModalType.AddEvent) _activeModal = ModalType.None;

            if (UIHelper.BeginPremiumModal("Subscribed Venues", ref isVenuesOpen, contentPos, contentSize, new Vector2(400, 300) * PluginUI.AppScale, out float alphaVenues))
            {
                ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, alphaVenues), "Subscribed Venues");
                ImGui.Separator(); ImGui.Spacing();
                
                if (_venues.Count == 0) ImGui.TextColored(new Vector4(1, 1, 1, alphaVenues), "No venues subscribed.");
                
                Vector4 btnBg = new Vector4(0.12f, 0.12f, 0.14f, alphaVenues);
                Vector4 btnHover = new Vector4(0.0f, 0.65f, 1.0f, alphaVenues);
                Vector4 btnText = new Vector4(0.9f, 0.9f, 0.9f, alphaVenues);
                Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, alphaVenues);
                Vector4 delBg = new Vector4(0.83f, 0.69f, 0.22f, alphaVenues);
                
                foreach (var v in _venues.ToList())
                {
                    string venueName = (string)v["name"] ?? "Unknown";
                    string venueId = (string)v["id"];
                    ImGui.TextColored(new Vector4(1, 1, 1, alphaVenues), venueName);
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60 * PluginUI.AppScale);
                    ImGui.PushID(venueId);
                    if (UIHelper.DrawPremiumButton("btn_del_ven", ImGui.GetCursorScreenPos(), new Vector2(60, 25) * PluginUI.AppScale, "Delete", delBg, btnHover, btnText, btnHoverText))
                    {
                        var (name, world) = GetPlayerContext();
                        System.Threading.Tasks.Task.Run(async () =>
                        {
                            var success = await _sender.DeleteVenueAsync(venueId, name, world);
                            if (success && name != null) {
                                FetchData(name, world);
                            }
                        });
                    }
                    ImGui.PopID();
                    ImGui.Dummy(new Vector2(0, 25) * PluginUI.AppScale);
                    ImGui.Separator();
                }
                ImGui.Spacing();
                if (UIHelper.DrawPremiumButton("btn_close_venues", ImGui.GetCursorScreenPos(), new Vector2(100, 30) * PluginUI.AppScale, "Close", btnBg, btnHover, btnText, btnHoverText)) { isVenuesOpen = false; }

                UIHelper.EndPremiumModal();
            }

            if (!isVenuesOpen && _activeModal == ModalType.Venues) _activeModal = ModalType.None;
        }

        public void Dispose()
        {
        }
    }
}

