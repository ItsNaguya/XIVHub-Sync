using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;
using System.Collections.Generic;
using XIVHubCompanion.Apps;
using Dalamud.Plugin;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace XIVHubCompanion
{
    public class PluginUI : IDisposable
    {
        private Configuration configuration;
        private HubClient sender;
        private IDalamudPluginInterface pluginInterface;
        private bool settingsVisible = false;
        private Dalamud.Plugin.Services.IObjectTable _objectTable;

        private bool _isVerified = true;
        private bool _isVerifying = false;
        private bool _verificationChecked = false;
        private bool _isWipeModalOpen = false;

        private string _inputToken = "";
        private Dalamud.Plugin.Services.IUnlockState _unlockState;

        private List<IApp> _apps = new List<IApp>();
        private IApp _selectedApp;
        
        private ConstellationBackground _constellation = new ConstellationBackground();

        public bool SettingsVisible
        {
            get { return settingsVisible; }
            set { settingsVisible = value; }
        }
        
        private readonly Dalamud.Plugin.Services.IPluginLog _log;
        private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;
        private readonly Dalamud.Plugin.Services.IGameGui _gameGui;
        private readonly Dalamud.Plugin.Services.IDataManager _dataManager;
        private readonly Dalamud.Plugin.Services.IClientState _clientState;

        private Dictionary<string, ISharedImmediateTexture> _sidebarIconCache = new Dictionary<string, ISharedImmediateTexture>();

        private ulong _lastHoveredItem = 0;
        private DateTime _hoverStartTime = DateTime.MinValue;

        private string _errorMessage = "";

        public static int ActiveRetainersCount { get; private set; } = 0;
        public static int ReturnedRetainersCount { get; private set; } = 0;
        public static long ShortestRetainerVentureTime { get; private set; } = 0;
        private static bool _lastRetainerAlarmFired = false;

        public PluginUI(Configuration configuration, HubClient sender, Dalamud.Plugin.Services.IGameGui gameGui, Dalamud.Plugin.Services.IChatGui chatGui, Dalamud.Plugin.Services.IAddonLifecycle addonLifecycle, Dalamud.Plugin.Services.ITextureProvider textureProvider, IDalamudPluginInterface pluginInterface, Dalamud.Plugin.Services.IPluginLog log, Dalamud.Plugin.Services.IObjectTable objectTable, Dalamud.Plugin.Services.IDataManager dataManager, Dalamud.Plugin.Services.IClientState clientState, Dalamud.Plugin.Services.ICommandManager commandManager, Dalamud.Plugin.Services.ICondition condition, Dalamud.Plugin.Services.IUnlockState unlockState)
        {
            this.configuration = configuration;
            this.sender = sender;
            this.pluginInterface = pluginInterface;
            this._inputToken = configuration.XivHubId ?? "";
            this._log = log;
            this._objectTable = objectTable;
            this._textureProvider = textureProvider;
            this._gameGui = gameGui;
            this._dataManager = dataManager;
            this._clientState = clientState;
            this._unlockState = unlockState;

            var collectionService = new Collections.CollectionService(_dataManager, _unlockState);

            _apps.Add(new MarketApp(gameGui, addonLifecycle, textureProvider, sender, _log, objectTable, pluginInterface, configuration));
            _apps.Add(new GatheringApp(sender, _log, configuration, gameGui, chatGui, clientState, textureProvider, dataManager, pluginInterface, commandManager, objectTable, condition));
            _apps.Add(new CraftingApp(sender, configuration, dataManager, _log, textureProvider));
            _apps.Add(new Apps.CollectionApp(sender, collectionService, textureProvider));
            _apps.Add(new RoutinesApp(sender, configuration));
            _apps.Add(new EventsApp(sender, objectTable, _log, textureProvider, pluginInterface));
            _apps.Add(new RaidPlannerApp());
            
            _selectedApp = _apps[0];
        }

        public void Dispose()
        {
            foreach (var app in _apps)
            {
                app.Dispose();
            }
        }

        public void OpenMarketAppWithItem(int trueItemId, string name, string icon, bool canBeHq)
        {
            var marketApp = _apps[0] as MarketApp;
            if (marketApp != null)
            {
                this.SettingsVisible = true;
                _selectedApp = marketApp;
                marketApp.SelectMarketItem(new MarketSearchItem { id = trueItemId, name = name, icon = icon, canBeHq = canBeHq });
            }
        }

        private void DrawPremiumFrame(ImDrawListPtr dl, Vector2 pos, Vector2 size)
        {
            float r = Scaled(14f);
            dl.AddRectFilled(pos, pos + size, Theme.Night1.With(0.97f).U32(), r);
            dl.AddRect(pos, pos + size, Theme.CrystalLine.U32(), r, 0, Scaled(1.2f));
            dl.AddLine(pos + new Vector2(Scaled(48), 0), pos + new Vector2(size.X - Scaled(48), 0), Theme.Gold.With(0.75f).U32(), Scaled(1.5f));
        }

        private ISharedImmediateTexture? _mark;

        /// <summary>The Aetherhold crystal mark (embedded images/mark.png).</summary>
        private void DrawMark(ImDrawListPtr dl, Vector2 min, float size)
        {
            _mark ??= _textureProvider.GetFromManifestResource(typeof(PluginUI).Assembly, "Aetherhold.mark.png");
            var wrap = _mark.GetWrapOrDefault();
            if (wrap != null) dl.AddImage(wrap.Handle, min, min + new Vector2(size, size));
        }

        public void Draw()
        {
            foreach (var app in _apps)
            {
                app.Update();
            }

            if (SettingsVisible && this.configuration.EnableHoverItemFetching)
            {
                ulong currentHover = _gameGui.HoveredItem;
                if (currentHover != 0)
                {
                    if (currentHover != _lastHoveredItem)
                    {
                        _lastHoveredItem = currentHover;
                        _hoverStartTime = DateTime.Now;
                    }
                    else if ((DateTime.Now - _hoverStartTime).TotalSeconds > 1.0)
                    {
                        var trueItemId = (int)(currentHover % 500000);
                        var marketApp = _apps[0] as MarketApp;
                        if (marketApp != null && marketApp.GetSelectedItemId() != trueItemId)
                        {
                            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow((uint)trueItemId);
                            if (row.HasValue)
                            {
                                OpenMarketAppWithItem(trueItemId, row.Value.Name.ToString(), row.Value.Icon.ToString(), row.Value.CanBeHq);
                            }
                        }
                    }
                }
                else
                {
                    _lastHoveredItem = 0;
                }
            }

            UpdateRetainersGlobal();

            if (SettingsVisible)
            {
                if (this.configuration.IsMinimized)
                    DrawWidgetWindow();
                else
                    DrawSettingsWindow();
            }
        }
        
        private void DrawWidgetWindow()
        {
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

            ImGui.SetNextWindowSize(new Vector2(200, 100), ImGuiCond.Always);
            
            if (ImGui.Begin("Aetherhold Widget###NAGU-PAD Widget", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                var winPos = ImGui.GetWindowPos();
                var winSize = ImGui.GetWindowSize();
                var drawList = ImGui.GetWindowDrawList();

                uint darkIron = Theme.Night1.With(0.97f).U32();
                uint edgeHighlight = UIHelper.Vec4ToU32(Theme.Crystal.With(0.23f));
                uint ceruleumBlue = UIHelper.Vec4ToU32(Theme.Crystal.With(0.5f));
                
                float rounding = 12f;
                drawList.AddRectFilled(winPos, winPos + winSize, darkIron, rounding);
                drawList.AddRect(winPos, winPos + winSize, edgeHighlight, rounding, 0, 1.5f);
                drawList.AddLine(winPos + new Vector2(20, 0), winPos + new Vector2(winSize.X - 20, 0), ceruleumBlue, 2f);

                var gatheringApp = _apps.OfType<GatheringApp>().FirstOrDefault();
                
                if (this.configuration.ShowMinimizedOverlay && this.configuration.ShowMinimizedGatheringOverlay)
                {
                    if (gatheringApp != null && gatheringApp.ActiveFavoriteCount > 0)
                {
                    string text = $"{gatheringApp.ActiveFavoriteCount}";
                    
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    var starSize = ImGui.CalcTextSize(((char)Dalamud.Interface.FontAwesomeIcon.Star).ToString());
                    ImGui.PopFont();
                    
                    var numSize = ImGui.CalcTextSize(text);
                    
                    float totalWidth = starSize.X + 5f + numSize.X;
                    Vector2 startPos = winPos + new Vector2(10f, 10f);
                    
                    float alpha = 0.5f + 0.5f * (float)((Math.Sin(ImGui.GetTime() * 4.0) + 1.0) / 2.0);
                    ImGui.SetCursorScreenPos(startPos);
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    ImGui.TextColored(Theme.Gold.With(alpha), ((char)Dalamud.Interface.FontAwesomeIcon.Star).ToString());
                    ImGui.PopFont();
                    
                    ImGui.SameLine(0, 5f);
                    ImGui.SetCursorScreenPos(new Vector2(startPos.X + starSize.X + 5f, startPos.Y));
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), text);
                    }

                    gatheringApp?.DrawWidgetOverlay(winPos, winSize);
                }

                if (this.configuration.ShowMinimizedOverlay && this.configuration.ShowMinimizedRetainerOverlay && ActiveRetainersCount > 0)
                {
                    float retAlpha = 0.8f;
                    if (ReturnedRetainersCount > 0 && ReturnedRetainersCount == ActiveRetainersCount)
                    {
                        retAlpha = 0.5f + 0.5f * (float)((Math.Sin(ImGui.GetTime() * 6.0) + 1.0) / 2.0); // Pulsing
                    }
                    else if (ReturnedRetainersCount > 0)
                    {
                        retAlpha = 1.0f; // Solid bright if some are returned
                    }
                    
                    Vector2 retPos = winPos + new Vector2(135, 42); // Center right area
                    ImGui.SetCursorScreenPos(retPos);
                    ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                    ImGui.SetWindowFontScale(1.1f);
                    ImGui.TextColored(Theme.Gold.With(retAlpha), ((char)Dalamud.Interface.FontAwesomeIcon.Briefcase).ToString());
                    ImGui.PopFont();
                    ImGui.SetWindowFontScale(1.0f);
                    
                    ImGui.SetCursorScreenPos(retPos + new Vector2(25, -3));
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text.With(retAlpha));
                    ImGui.Text($"{ReturnedRetainersCount}/{ActiveRetainersCount}");
                    ImGui.PopStyleColor();
                    
                    string timeStr = "Ready!";
                    if (ShortestRetainerVentureTime > 0)
                    {
                        var ts = TimeSpan.FromSeconds(ShortestRetainerVentureTime);
                        timeStr = ts.Hours > 0 ? $"{ts.Hours}h {ts.Minutes}m" : $"{ts.Minutes}m {ts.Seconds}s";
                    }
                    
                    ImGui.SetCursorScreenPos(retPos + new Vector2(25, 12));
                    if (timeStr == "Ready!")
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Crystal.With(retAlpha)); // Green for Ready
                    }
                    else
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text2.With(retAlpha));
                    }
                    ImGui.SetWindowFontScale(0.85f);
                    ImGui.Text(timeStr);
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.PopStyleColor();
                }

                ImGui.SetCursorScreenPos(winPos + new Vector2(80, 30));
                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Crystal);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0,0,0,0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Crystal.With(0.2f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Crystal.With(0.4f));
                ImGui.SetWindowFontScale(1.8f);
                if (ImGui.Button("\uf065", new Vector2(40, 40)))
                {
                    this.configuration.IsMinimized = false;
                    this.configuration.Save();
                }
                ImGui.SetWindowFontScale(1.0f);
                ImGui.PopStyleColor(4);
                ImGui.PopFont();
                
                ImGui.SetCursorScreenPos(winPos);
                ImGui.InvisibleButton("WidgetDrag", new Vector2(160, 100));
                
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                }
                
                ImGui.SetCursorScreenPos(winPos + new Vector2(175, 8));
                ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text2);
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0,0,0,0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1,1,1,0.1f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1,1,1,0.2f));
                if (ImGui.Button("\uf00d", new Vector2(18, 18)))
                {
                    SettingsVisible = false;
                }
                ImGui.PopStyleColor(4);
                ImGui.PopFont();
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        public static float AppScale { get; private set; } = 1.0f;
        public static bool HideScrollbars { get; private set; } = false;

        public static float Scaled(float value) => value * AppScale;
        public static Vector2 Scaled(Vector2 value) => new Vector2(value.X * AppScale, value.Y * AppScale);

        public void DrawSettingsWindow()
        {
            if (!SettingsVisible) return;

            HideScrollbars = this.configuration.HideScrollbars;

            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0)); 
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

            switch (this.configuration.TabletSize)
            {
                case 0: AppScale = 0.80f; break;
                case 1: AppScale = 0.90f; break;
                case 2: AppScale = 1.0f;  break;
                case 3: AppScale = 1.10f; break;
                case 4: AppScale = 1.20f; break;
                default: AppScale = 1.0f; break;
            }

            Vector2 windowSize = new Vector2(1010 * AppScale, 770 * AppScale);
            ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);
            
            if (ImGui.Begin("Aetherhold###NAGU-PAD Premium Tablet", ref settingsVisible, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize))
            {
                Theme.Push();
                bool isVerifiedFrame = _isVerified;
                ImGui.SetWindowFontScale(AppScale);

                if (!_verificationChecked && !_isVerifying) {
                    _isVerifying = true;
                    var localPlayer = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
                    if (localPlayer != null) {
                        string name = localPlayer.Name.ToString();
                        string world = localPlayer.HomeWorld.Value.Name.ExtractText();
                        Task.Run(async () => {


                            if (!string.IsNullOrEmpty(this.configuration.XivHubId)) {
                                var res = await sender.VerifyUserAsync(this.configuration.XivHubId, name);
                                if (res == "Success") {
                                    this.configuration.IsVerified = true;
                                    _isVerified = true;
                                } else if (res == "Invalid token" || res.StartsWith("Character mismatch")) {
                                    this.configuration.IsVerified = false;
                                    this.configuration.XivHubId = "";
                                    this.configuration.Save();
                                    sender.MarkAuthRejected();
                                    _isVerified = false;
                                    _errorMessage = res;
                                } else {
                                    // Server or network error, assume still verified
                                    _isVerified = true;
                                }
                            } else {
                                this.configuration.IsVerified = false;
                                _isVerified = false;
                            }
                            _verificationChecked = true;
                            _isVerifying = false;
                        });
                    } else {
                        _isVerifying = false; 
                    }
                }

                var winPos = ImGui.GetWindowPos();
                var winSize = ImGui.GetWindowSize();
                var drawList = ImGui.GetWindowDrawList();

                Vector2 contentPos = winPos + Scaled(new Vector2(21, 21));
                Vector2 contentSize = winSize - Scaled(new Vector2(42, 42));

                DrawPremiumFrame(drawList, winPos, winSize);

                drawList.AddRectFilled(contentPos, contentPos + contentSize, UIHelper.Vec4ToU32(Theme.Night0), 4f);
                drawList.AddRectFilled(contentPos, contentPos + contentSize, UIHelper.Vec4ToU32(Theme.Night1.With(0.95f)), 0f);

                _constellation.Draw(contentPos, contentSize, this.configuration.HideBackgroundAnimation, this.configuration.HideScanline);
                drawList.AddRect(contentPos, contentPos + contentSize, UIHelper.Vec4ToU32(Theme.Crystal.With(0.1f)), 0f, 0, 1f);

                ImGui.SetCursorPos(new Vector2(0, 0));
                ImGui.InvisibleButton("TitleBarDrag", new Vector2(winSize.X, Scaled(25f)));

                string role = sender.CurrentUserRole switch { "admin" => "  ·  ADMIN", "friend" => "  ·  FRIEND", _ => "" };
                ImGui.SetWindowFontScale(0.85f * AppScale);
                var headSize = ImGui.CalcTextSize("AETHERHOLD" + role);
                float markSize = Scaled(16f);
                var titlePos = winPos + new Vector2(winSize.X / 2 - (headSize.X + markSize + Scaled(6f)) / 2, Scaled(12f) - headSize.Y / 2);
                DrawMark(drawList, titlePos + new Vector2(0, (headSize.Y - markSize) / 2), markSize);
                ImGui.SetCursorScreenPos(titlePos + new Vector2(markSize + Scaled(6f), 0));
                ImGui.TextColored(Theme.Gold, "AETHERHOLD");
                if (role != "") { ImGui.SameLine(0, 0); ImGui.TextColored(Theme.Text3, role); }
                ImGui.SetWindowFontScale(AppScale);

                Vector4 baseIconColor = Theme.Text2;
                Vector4 closeHoverColor = Theme.Gold;
                Vector4 minimizeHoverColor = Theme.Crystal;
                
                if (DrawHardwareButton(drawList, "CloseBtn", winPos + new Vector2(winSize.X - Scaled(23f), Scaled(75f)), Scaled(new Vector2(21, 35)), "\uf00d", baseIconColor, closeHoverColor))
                {
                    settingsVisible = false;
                }
                
                if (DrawHardwareButton(drawList, "MinimizeBtn", winPos + new Vector2(winSize.X - Scaled(23f), Scaled(115f)), Scaled(new Vector2(21, 40)), "\uf068", baseIconColor, minimizeHoverColor))
                {
                    this.configuration.IsMinimized = true;
                    this.configuration.Save();
                }

                float sidebarWidth = Scaled(80f);
                
                if (!isVerifiedFrame) {
                    ImGui.BeginDisabled(true);
                }

                drawList.AddLine(contentPos + new Vector2(sidebarWidth, 0), contentPos + new Vector2(sidebarWidth, contentSize.Y), Theme.CrystalLine.U32());
                
                ImGui.SetCursorPos(Scaled(new Vector2(25, 30)));
                float availableHeight = contentSize.Y - Scaled(40f);
                UIHelper.BeginSmoothChild("Sidebar", new Vector2(sidebarWidth, availableHeight), false, ImGuiWindowFlags.NoScrollbar);
                
                float slotHeight = availableHeight / 8f;
                float currentY = 0f;

                float[] appHoverStates = new float[_apps.Count];
                int appIdx = 0;
                foreach (var app in _apps)
                {
                    ImGui.SetCursorPosY(currentY + (slotHeight / 2f) - Scaled(25f));
                    appHoverStates[appIdx] = RenderSidebarIcon(app.Name, app.Icon, app == _selectedApp, () => _selectedApp = app);
                    currentY += slotHeight;
                    appIdx++;
                }

                Vector2 divStart = ImGui.GetCursorScreenPos() + new Vector2(0, (slotHeight / 2f) - Scaled(25f));
                Vector2 divEnd = divStart + new Vector2(sidebarWidth - Scaled(10f), 0);
                drawList.AddLine(divStart, divEnd, UIHelper.Vec4ToU32(new Vector4(1, 1, 1, 0.1f)));

                ImGui.SetCursorPosY(currentY + (slotHeight / 2f) - Scaled(25f));
                RenderSidebarIcon("Core Settings", ((char)Dalamud.Interface.FontAwesomeIcon.Cog).ToString(), _selectedApp == null, () => _selectedApp = null);

                ImGui.EndChild();

                ImGuiWindowFlags mainFlags = ImGuiWindowFlags.None;
                if (this.configuration.HideScrollbars) mainFlags |= ImGuiWindowFlags.NoScrollbar;

                Vector2 rightPanePos = contentPos + new Vector2(sidebarWidth, 0);
                Vector2 rightPaneSize = new Vector2(contentSize.X - sidebarWidth, contentSize.Y);
                drawList.AddRectFilled(rightPanePos, rightPanePos + rightPaneSize, Theme.Night2.With(0.35f).U32(), Scaled(4f), ImDrawFlags.RoundCornersRight);
                drawList.AddRect(rightPanePos, rightPanePos + rightPaneSize, Theme.CrystalLine.U32(), Scaled(4f), ImDrawFlags.RoundCornersRight, 1.2f);

                ImGui.SetCursorPos(new Vector2(Scaled(25f) + sidebarWidth + Scaled(20f), Scaled(25f))); 
                UIHelper.BeginSmoothChild("MainContent", new Vector2(contentSize.X - sidebarWidth - Scaled(40f), contentSize.Y - Scaled(35f)), false, mainFlags);
                
                if (_selectedApp != null)
                {
                    ImGui.BeginGroup();
                    ImGui.SetWindowFontScale(1.1f);
                    _selectedApp.Draw();
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.EndGroup();
                }
                else
                {
                    DrawCoreSettings();
                }

                ImGui.EndChild();

                if (!isVerifiedFrame) {
                    ImGui.EndDisabled();
                }
                

                if (_verificationChecked && !isVerifiedFrame)
                {
                    ImGui.SetCursorScreenPos(contentPos);
                    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, Scaled(16f));
                    ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Night0.With(0.92f));
                    ImGui.BeginChild("UnverifiedOverlay", contentSize, false, ImGuiWindowFlags.NoScrollbar);
                    
                    float blink = (float)(Math.Sin(ImGui.GetTime() * 6.0) * 0.5 + 0.5);
                    Vector4 redColor = Theme.Gold.With(0.5f + blink * 0.5f);
                    
                    string alertTitle = "UNVERIFIED USER";
                    var titleSize = ImGui.CalcTextSize(alertTitle);
                    
                    ImGui.SetCursorScreenPos(contentPos + new Vector2(contentSize.X / 2 - (titleSize.X * 2.5f) / 2, contentSize.Y / 2 - Scaled(120f)));
                    ImGui.SetWindowFontScale(2.5f);
                    ImGui.TextColored(redColor, alertTitle);
                    ImGui.SetWindowFontScale(1.0f);
                    
                    string alertMsg1 = "To connect this plugin to Aetherhold, you need your character's plugin token.";
                    string alertMsg2 = "Click the button below to get your token from the website settings page.";
                    
                    var msg1Size = ImGui.CalcTextSize(alertMsg1);
                    var msg2Size = ImGui.CalcTextSize(alertMsg2);
                    
                    ImGui.SetCursorScreenPos(contentPos + new Vector2(contentSize.X / 2 - msg1Size.X / 2, contentSize.Y / 2 - Scaled(50f)));
                    ImGui.TextColored(Theme.Text2, alertMsg1);
                    
                    ImGui.SetCursorScreenPos(contentPos + new Vector2(contentSize.X / 2 - msg2Size.X / 2, contentSize.Y / 2 - Scaled(30f)));
                    ImGui.TextColored(Theme.Text2, alertMsg2);

                    Vector4 webBtnBg = Theme.CrystalDeep;
                    Vector4 webBtnHover = Theme.CrystalDeep;
                    Vector4 webBtnText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    Vector4 webBtnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

                    if (UIHelper.DrawPremiumButton("btn_open_website", contentPos + new Vector2(contentSize.X / 2 - Scaled(125f), contentSize.Y / 2), new Vector2(Scaled(250f), Scaled(40f)), "Get Token from Website", webBtnBg, webBtnHover, webBtnText, webBtnHoverText))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HubClient.BaseUrl + "/settings") { UseShellExecute = true });
                    }

                    if (!string.IsNullOrEmpty(_errorMessage)) {
                        var errSize = ImGui.CalcTextSize(_errorMessage);
                        ImGui.SetCursorScreenPos(contentPos + new Vector2(contentSize.X / 2 - errSize.X / 2, contentSize.Y / 2 + Scaled(55f)));
                        ImGui.TextColored(Theme.Gold, _errorMessage);
                    }

                    ImGui.SetCursorScreenPos(contentPos + new Vector2(contentSize.X / 2 - Scaled(150f), contentSize.Y / 2 + Scaled(85f)));
                    ImGui.SetNextItemWidth(Scaled(300f));
                    ImGui.InputText("##token_input", ref _inputToken, 255);
                    
                    if (ImGui.IsItemHovered()) {
                        UIHelper.DrawTooltip("Paste your unique character token from the website here.");
                    }

                    Vector4 btnBg = Theme.Night2;
                    Vector4 btnHover = Theme.Crystal;
                    Vector4 btnText = Theme.Text;
                    Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

                    if (UIHelper.DrawPremiumButton("btn_submit_token", contentPos + new Vector2(contentSize.X / 2 - Scaled(75f), contentSize.Y / 2 + Scaled(125f)), new Vector2(Scaled(150f), Scaled(40f)), "Verify Token", btnBg, btnHover, btnText, btnHoverText))
                    {
                        var localPlayer = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
                        if (localPlayer != null) {
                            string name = localPlayer.Name.ToString();
                            string world = localPlayer.HomeWorld.Value.Name.ExtractText();
                            _isVerifying = true;
                            _errorMessage = "Verifying...";
                            Task.Run(async () => {
                                var res = await sender.VerifyUserAsync(_inputToken.Trim(), name);
                                if (res == "Success") {
                                    this.configuration.XivHubId = _inputToken.Trim();
                                    this.configuration.IsVerified = true;
                                    this.configuration.Save();
                                    sender.AttachAuthHeader(this.configuration);
                                    _isVerified = true;
                                    _errorMessage = "";
                                } else {
                                    _errorMessage = res;
                                }
                                _isVerifying = false;
                            });
                        } else {
                            _errorMessage = "Could not find local player character.";
                        }
                    }
                    
                    ImGui.EndChild();
                    ImGui.PopStyleColor();
                    ImGui.PopStyleVar();
                }


                Theme.Pop();
            }
            ImGui.End();

            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private bool DrawHardwareButton(ImDrawListPtr drawList, string id, Vector2 pos, Vector2 size, string icon, Vector4 baseColor, Vector4 hoverColor)
        {
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, size);
            bool isHovered = ImGui.IsItemHovered();
            bool isActive = ImGui.IsItemActive();
            bool isClicked = ImGui.IsItemClicked();

            uint baseId = ImGui.GetID(id);
            float hoverState = UIHelper.GetHoverState(baseId, isHovered, 10.0f);
            float activeState = UIHelper.GetHoverState(baseId ^ 0x12345678, isActive, 20.0f);

            float pressOffset = activeState * 1.5f;
            Vector2 btnStart = pos + new Vector2(pressOffset, 0);
            Vector2 btnEnd = pos + size + new Vector2(pressOffset, 0);

            Vector4 gunmetal = Theme.Night2;
            Vector4 currentBg = UIHelper.LerpColor(gunmetal, hoverColor, hoverState * 0.25f);
            
            drawList.AddRectFilled(btnStart, btnEnd, UIHelper.Vec4ToU32(currentBg), 2f);
            
            drawList.AddLine(btnStart + new Vector2(1, 1), new Vector2(btnEnd.X - 1, btnStart.Y + 1), UIHelper.Vec4ToU32(new Vector4(1, 1, 1, 0.15f + hoverState * 0.2f)), 1f);
            drawList.AddLine(btnStart + new Vector2(1, 1), new Vector2(btnStart.X + 1, btnEnd.Y - 1), UIHelper.Vec4ToU32(new Vector4(1, 1, 1, 0.1f + hoverState * 0.1f)), 1f);

            Vector4 currentText = UIHelper.LerpColor(baseColor, hoverColor, hoverState);
            
            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            var textSize = ImGui.CalcTextSize(icon);
            ImGui.SetCursorScreenPos(btnStart + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2));
            ImGui.TextColored(currentText, icon);
            ImGui.PopFont();

            return isClicked;
        }

        /// <summary>Never show the whole token on screen (streams, screenshots).</summary>
        private static string TokenHint(string token) => string.IsNullOrEmpty(token) || token.Length < 8 ? "set" : $"{token[..3]}…{token[^4..]}";

        private float RenderSidebarIcon(string name, string icon, bool isSelected, Action onClick)
        {
            ImGui.SetCursorPosX(Scaled(15f));
            var cursorPos = ImGui.GetCursorScreenPos();
            var size = new Vector2(Scaled(50f), Scaled(50f));
            
            ImGui.InvisibleButton(name, size);
            bool isHovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked()) onClick();

            uint id = ImGui.GetID(name);
            float hoverState = UIHelper.GetHoverState(id, isHovered, 12.0f);
            var drawList = ImGui.GetWindowDrawList();

            Vector4 baseBg = isSelected ? Theme.Crystal.With(0.15f) : new Vector4(1, 1, 1, 0.0f);
            Vector4 hoverBg = isSelected ? Theme.Crystal.With(0.25f) : new Vector4(1, 1, 1, 0.05f);
            Vector4 currentBg = UIHelper.LerpColor(baseBg, hoverBg, hoverState);
            
            drawList.AddRectFilled(cursorPos, cursorPos + size, UIHelper.Vec4ToU32(currentBg), Scaled(12f));

            if (isSelected)
            {
                drawList.AddRectFilled(cursorPos + new Vector2(-Scaled(10f), Scaled(10f)), cursorPos + new Vector2(-Scaled(7f), size.Y - Scaled(10f)), UIHelper.Vec4ToU32(Theme.Crystal), Scaled(2f));
            }

            Vector4 baseText = isSelected ? Theme.Crystal : Theme.Text2;
            Vector4 hoverText = isSelected ? Theme.Crystal : Theme.Text;
            Vector4 currentText = UIHelper.LerpColor(baseText, hoverText, hoverState);

            float baseScale = isSelected ? 1.4f : 1.2f;
            float targetScale = 1.6f;
            float currentScale = baseScale + (targetScale - baseScale) * hoverState;

            if (isSelected)
            {
                float pulse = (float)(Math.Sin(ImGui.GetTime() * 4.0) * 0.15f + 0.85f);
                currentText.W = pulse;
            }

            ImGui.PushFont(Dalamud.Interface.UiBuilder.IconFont);
            
            var textSize = ImGui.CalcTextSize(icon);
            Vector2 iconPos = cursorPos + new Vector2(size.X / 2 - textSize.X / 2, size.Y / 2 - textSize.Y / 2);
            
            drawList.AddText(iconPos, UIHelper.Vec4ToU32(currentText), icon);
            
            ImGui.PopFont();

            if (isHovered)
            {
                UIHelper.DrawTooltip(name);
            }
            
            return hoverState;
        }

        private void DrawCoreSettings()
        {
            if (ImGui.BeginTabBar("SettingsTabs"))
            {
                if (ImGui.BeginTabItem("Core System"))
                {
                    ImGui.BeginGroup();
                    ImGui.SetWindowFontScale(1.1f);
                    
                    ImGui.Dummy(new Vector2(0, 10));
                    ImGui.TextColored(Theme.Crystal, "Tablet Size");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    int currentSize = this.configuration.TabletSize;
                    string[] sizes = new string[] { "Extra Small (640x480) [Unsupported]", "Small (800x600) [Unsupported]", "Standard (960x720)", "Large (1120x840) [Unsupported]", "Extra Large (1280x960) [Unsupported]" };
                    if (ImGui.Combo("Size Option", ref currentSize, sizes, sizes.Length))
                    {
                        this.configuration.TabletSize = currentSize;
                        this.configuration.Save();
                    }
                    
                    ImGui.Dummy(new Vector2(0, 15));
                    ImGui.TextColored(Theme.Crystal, "Appearance");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    bool hideAnim = this.configuration.HideBackgroundAnimation;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_bg", "Hide Background Animation", ref hideAnim))
                    {
                        this.configuration.HideBackgroundAnimation = hideAnim;
                        this.configuration.Save();
                    }
                    
                    bool hideScan = this.configuration.HideScanline;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_scan", "Hide Scanline", ref hideScan))
                    {
                        this.configuration.HideScanline = hideScan;
                        this.configuration.Save();
                    }
        
                    bool hideScroll = this.configuration.HideScrollbars;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_scroll", "Hide Scrollbars (Dynamic Fade)", ref hideScroll))
                    {
                        this.configuration.HideScrollbars = hideScroll;
                        this.configuration.Save();
                    }

                    ImGui.Dummy(new Vector2(0, 10));
                    ImGui.TextColored(Theme.Crystal, "Widget Overlays");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    bool showOverlay = this.configuration.ShowMinimizedOverlay;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_overlay", "Enable Minimized Overlays", ref showOverlay))
                    {
                        this.configuration.ShowMinimizedOverlay = showOverlay;
                        this.configuration.Save();
                    }

                    if (showOverlay)
                    {
                        ImGui.Indent(20);
                        bool showGathering = this.configuration.ShowMinimizedGatheringOverlay;
                        if (UIHelper.DrawPremiumSwitchWithText("chk_gathering", "Show Gathering Overlay", ref showGathering))
                        {
                            this.configuration.ShowMinimizedGatheringOverlay = showGathering;
                            this.configuration.Save();
                        }

                        bool showRetainer = this.configuration.ShowMinimizedRetainerOverlay;
                        if (UIHelper.DrawPremiumSwitchWithText("chk_retainer", "Show Retainer Overlay", ref showRetainer))
                        {
                            this.configuration.ShowMinimizedRetainerOverlay = showRetainer;
                            this.configuration.Save();
                        }
                        ImGui.Unindent(20);
                    }

                    ImGui.Dummy(new Vector2(0, 5));
                    
                    bool openOnStartup = this.configuration.OpenOnStartup;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_startup", "Open Plugin when Logging In", ref openOnStartup))
                    {
                        this.configuration.OpenOnStartup = openOnStartup;
                        this.configuration.Save();
                    }
                    
                    if (openOnStartup)
                    {
                        ImGui.Dummy(new Vector2(0, 5));
                        ImGui.Indent(20);
                        bool startMinimized = this.configuration.StartMinimized;
                        if (UIHelper.DrawPremiumSwitchWithText("chk_minimized", "Start Minimized (Background Mode)", ref startMinimized))
                        {
                            this.configuration.StartMinimized = startMinimized;
                            this.configuration.Save();
                        }
                        ImGui.Unindent(20);
                    }

                    ImGui.Dummy(new Vector2(0, 15));
                    ImGui.TextColored(Theme.Crystal, "Aetherhold account");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    if (this.configuration.IsVerified)
                    {
                        string roleDisplay = "";
                        if (sender.CurrentUserRole == "admin") roleDisplay = " [Admin]";
                        else if (sender.CurrentUserRole == "friend") roleDisplay = " [Friend]";
                        
                        ImGui.TextColored(Theme.Crystal, $"✓ Verified{roleDisplay} (token {TokenHint(this.configuration.XivHubId)})");
                    }
                    else
                    {
                        ImGui.TextColored(Theme.Gold, "✗ Unverified");
                    }
                    
                    ImGui.Dummy(new Vector2(0, 5));
                    Vector4 btnBg = Theme.Night2;
                    Vector4 btnHover = Theme.Gold;
                    Vector4 btnText = Theme.Text;
                    Vector4 btnHoverText = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                    
                    if (UIHelper.DrawPremiumButton("btn_unlink", ImGui.GetCursorScreenPos(), new Vector2(250, 40), "Re-enter Token / Unlink", btnBg, btnHover, btnText, btnHoverText))
                    {
                        this.configuration.XivHubId = "";
                        this.configuration.IsVerified = false;
                        this.configuration.Save();
                        _isVerified = false;
                        _errorMessage = "";
                        sender.AttachAuthHeader(this.configuration);
                    }
                    ImGui.Dummy(new Vector2(0, 30));
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.EndGroup();
                    ImGui.EndTabItem();
                }
                
                if (ImGui.BeginTabItem("Background Sync"))
                {
                    ImGui.BeginGroup();
                    ImGui.SetWindowFontScale(1.1f);
                    
                    ImGui.Dummy(new Vector2(0, 10));
                    ImGui.TextColored(Theme.Crystal, "Synchronization Engine");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    var enabled = this.configuration.IsSyncEnabled;
                    if (UIHelper.DrawPremiumSwitchWithText("chk_sync", "Enable Live Background Sync", ref enabled))
                    {
                        this.configuration.IsSyncEnabled = enabled;
                        this.configuration.Save();
                    }
                    
                    ImGui.Dummy(new Vector2(0, 5));
                    ImGui.TextWrapped("When enabled, this plugin acts as a silent companion, automatically pushing your character data, inventory, and active gear to Aetherhold in real time.");
                    
                    ImGui.Dummy(new Vector2(0, 20));
                    ImGui.TextColored(Theme.Crystal, "Engine Telemetry");
                    ImGui.Dummy(new Vector2(0, 5));
                    
                    ImGui.Text($"Total Sync Attempts: {sender.TotalSyncs}");
                    ImGui.TextColored(sender.FailedSyncs > 0 ? Theme.Bad : Theme.Text2, $"Failed Syncs: {sender.FailedSyncs}");
                    ImGui.Text($"Last Sync Time: {(sender.LastSyncTime == DateTime.MinValue ? "Never" : sender.LastSyncTime.ToString("HH:mm:ss"))}");
                    ImGui.TextWrapped($"Connection Status: {sender.LastSyncStatus}");
                    
                    ImGui.Dummy(new Vector2(0, 20));
                    ImGui.TextColored(Theme.Gold, "Data Reset");
                    ImGui.Dummy(new Vector2(0, 5));
                    ImGui.TextWrapped("Warning: Wiping the calendar will delete all custom events across all devices for this character. Proceed with caution.");
                    
                    Vector4 btnBg = Theme.Night2;
                    Vector4 btnHover = Theme.Crystal;
                    Vector4 btnText = Theme.Text;
                    Vector4 btnHoverText = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
                    Vector4 redBtnBg = new Vector4(0.3f, 0.1f, 0.1f, 1.0f);
                    Vector4 redBtnHover = Theme.Gold;
                    
                    if (UIHelper.DrawPremiumButton("btn_wipe", ImGui.GetCursorScreenPos(), new Vector2(150, 30), "Wipe Calendar", redBtnBg, redBtnHover, btnText, btnHoverText))
                    {
                        _isWipeModalOpen = true;
                    }
                    ImGui.Dummy(new Vector2(0, 30));
                    
                    if (UIHelper.BeginPremiumModal("Confirm Wipe Calendar", ref _isWipeModalOpen, ImGui.GetWindowPos(), ImGui.GetWindowSize(), new Vector2(400, 200) * AppScale, out float alpha))
                    {
                        ImGui.TextColored(Theme.Bad, "Are you absolutely sure?");
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0, 10 * AppScale));
                        ImGui.TextWrapped("This will permanently delete all events for this character on Aetherhold.");
                        ImGui.Dummy(new Vector2(0, 20 * AppScale));
                        
                        if (UIHelper.DrawPremiumButton("btn_confirm_wipe", ImGui.GetCursorScreenPos(), new Vector2(120, 30) * AppScale, "Yes, wipe it!", redBtnBg, redBtnHover, btnText, btnHoverText))
                        {
                            var player = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
                            if (player != null)
                            {
                                string name = player.Name.ToString();
                                string world = player.HomeWorld.Value.Name.ToString();
                                
                                Task.Run(async () => {
                                    await sender.WipeCalendarAsync(name, world);
                                });
                            }
                            _isWipeModalOpen = false;
                        }
                        ImGui.SameLine(0, 10);
                        
                        if (UIHelper.DrawPremiumButton("btn_cancel_wipe", ImGui.GetCursorScreenPos(), new Vector2(120, 30) * AppScale, "Cancel", btnBg, btnHover, btnText, btnHoverText))
                        {
                            _isWipeModalOpen = false;
                        }
                        UIHelper.EndPremiumModal();
                    }
                    
                    ImGui.SetWindowFontScale(1.0f);
                    ImGui.EndGroup();
                    ImGui.EndTabItem();
                }
                
                foreach (var app in _apps)
                {
                    if (app.HasSettings)
                    {
                        if (ImGui.BeginTabItem(app.Name))
                        {
                            app.DrawSettings();
                            ImGui.EndTabItem();
                        }
                    }
                }
                
                ImGui.EndTabBar();
            }
        }

        private long? _lastVentureEnd = -1;

        /// <summary>Venture timer from the game (open problem 8): the website's retainer clock reads prefs.retainerVentureEnd (ms).</summary>
        private void PushVentureEnd(long latestEndUnix)
        {
            long? ms = latestEndUnix > 0 ? latestEndUnix * 1000 : null;
            if (ms == _lastVentureEnd || !sender.StateLoaded) return;
            _lastVentureEnd = ms;
            if (sender.GetState<long?>("prefs.retainerVentureEnd") == ms) return;
            sender.SetState("prefs.retainerVentureEnd", ms);
        }

        private unsafe void UpdateRetainersGlobal()
        {
            var rm = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            if (rm == null) return;
            
            int active = 0;
            int returned = 0;
            long shortest = long.MaxValue;
            long latestEnd = 0;
            
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            for (uint i = 0; i < 10; i++)
            {
                var ret = rm->GetRetainerBySortedIndex(i);
                if (ret == null || ret->RetainerId == 0) continue;
                if (string.IsNullOrEmpty(ret->NameString)) continue;
                
                active++;
                
                if (ret->VentureId != 0)
                {
                    latestEnd = Math.Max(latestEnd, ret->VentureComplete);
                    long diff = ret->VentureComplete - now;
                    if (diff <= 0)
                    {
                        returned++;
                    }
                    else if (diff > 0 && diff < shortest)
                    {
                        shortest = diff;
                    }
                }
            }
            
            ActiveRetainersCount = active;
            if (rm->IsReady) PushVentureEnd(latestEnd);
            ReturnedRetainersCount = returned;
            ShortestRetainerVentureTime = shortest == long.MaxValue ? 0 : shortest;
            
            if (active > 0 && returned > 0 && returned == active)
            {
                if (!_lastRetainerAlarmFired && this.configuration.RetainerAudioEnabled)
                {
                    try
                    {
                        FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(1);
                    }
                    catch { }
                    _lastRetainerAlarmFired = true;
                }
                
                if (!this.configuration.RetainerAudioFireOnce)
                {
                    _lastRetainerAlarmFired = false;
                }
            }
            else
            {
                _lastRetainerAlarmFired = false;
            }
        }
    }
}
