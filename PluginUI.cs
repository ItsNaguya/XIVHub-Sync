using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;
using XIVHubCompanion.Apps;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion
{
    /// <summary>
    /// The Aetherhold tablet: bezel, status bar, home screen, apps, dock, notification shade, sleep view and sign-in.
    /// </summary>
    public partial class PluginUI : IDisposable
    {
        private readonly Configuration configuration;
        private readonly HubClient sender;
        private readonly IObjectTable _objectTable;
        private readonly IPluginLog _log;
        private readonly ITextureProvider _textureProvider;
        private readonly IGameGui _gameGui;
        private readonly IDataManager _dataManager;

        private readonly List<IApp> _apps = new();
        private readonly SettingsApp _settingsApp;
        private readonly HomeScreen _home;
        private readonly ConstellationBackground _constellation = new();
        private readonly ConstellationBackground _constellationPreview = new(16, 4, 45f); // own state: one instance at two sizes re-seeds every frame
        private IApp? _openApp; // null = home screen

        private bool settingsVisible;
        public bool SettingsVisible { get => settingsVisible; set => settingsVisible = value; }

        // sign-in
        private bool _isVerified = true, _isVerifying, _verificationChecked;
        private string _inputToken, _errorMessage = "";

        private bool _shadeOpen;
        private ulong _lastHoveredItem;
        private DateTime _hoverStartTime = DateTime.MinValue;

        public static float AppScale { get; private set; } = 1.0f;
        /// <summary>Window font scale inside the tablet: 1 when the baked body font is pushed, else stretch to the same size.</summary>
        public static float FontScale { get; private set; } = 1.0f;
        /// <summary>Apps whose layout fits the tablet's larger text (Fonts.TabletText); the others draw at the old size.</summary>
        private static readonly HashSet<string> LargeTextApps = new();
        public static bool HideScrollbars { get; private set; }
        public static float Scaled(float value) => value * AppScale;
        public static Vector2 Scaled(Vector2 value) => value * AppScale;

        public static int ActiveRetainersCount { get; private set; }
        public static int ReturnedRetainersCount { get; private set; }
        public static long ShortestRetainerVentureTime { get; private set; }
        public static bool RetainersKnown { get; private set; }

        private static readonly float[] Sizes = { 0.8f, 0.9f, 1.0f, 1.1f, 1.2f };
        private static readonly Vector2 BaseSize = new(1040, 760);

        public PluginUI(Configuration configuration, HubClient sender, IGameGui gameGui, IChatGui chatGui, IAddonLifecycle addonLifecycle, ITextureProvider textureProvider, IDalamudPluginInterface pluginInterface, IPluginLog log, IObjectTable objectTable, IDataManager dataManager, IClientState clientState, ICommandManager commandManager, ICondition condition, IUnlockState unlockState)
        {
            this.configuration = configuration;
            this.sender = sender;
            _inputToken = configuration.XivHubId ?? "";
            _log = log;
            _objectTable = objectTable;
            _textureProvider = textureProvider;
            _gameGui = gameGui;
            _dataManager = dataManager;

            var collectionService = new Collections.CollectionService(dataManager, unlockState);
            _apps.Add(new MarketApp(gameGui, addonLifecycle, textureProvider, sender, log, objectTable, pluginInterface, configuration));
            _apps.Add(new GatheringApp(sender, log, configuration, gameGui, chatGui, clientState, textureProvider, dataManager, pluginInterface, commandManager, objectTable, condition));
            _apps.Add(new CraftingApp(sender, configuration, dataManager, log, textureProvider));
            _apps.Add(new CollectionApp(sender, collectionService, textureProvider));
            _apps.Add(new RoutinesApp(sender, configuration));
            _apps.Add(new EventsApp(sender, objectTable, log, textureProvider, pluginInterface));
            _apps.Add(new RaidPlannerApp());
            _settingsApp = new SettingsApp(DrawSettings);
            _home = new HomeScreen(this, configuration, sender, dataManager, objectTable);

            sender.StateChanged += OnStateChanged;
            sender.OnAuthRejected += () => Notifications.Post("auth", "Account", "Plugin token rejected", "Sync is paused. Enter your token again in Settings.", "Settings");
            ApplyLook();
        }

        public IReadOnlyList<IApp> Apps => _apps;
        public T? App<T>() where T : class, IApp => _apps.OfType<T>().FirstOrDefault();
        public IApp? AppByName(string? name) => name == "Settings" ? _settingsApp : _apps.FirstOrDefault(a => a.Name == name);
        public void Open(IApp? app) { _openApp = app; _shadeOpen = false; }

        public void Dispose()
        {
            sender.StateChanged -= OnStateChanged;
            foreach (var app in _apps) app.Dispose();
        }

        public void OpenMarketAppWithItem(int trueItemId, string name, string icon, bool canBeHq)
        {
            if (App<MarketApp>() is not { } market) return;
            SettingsVisible = true;
            configuration.IsMinimized = false;
            Open(market);
            market.SelectMarketItem(new MarketSearchItem { id = trueItemId, name = name, icon = icon, canBeHq = canBeHq });
        }

        // ---- look and synced home layout (prefs.tablet) -----------------------------------------------------------
        private void ApplyLook() => Ui.Accent = configuration.TabletAccent switch { "gold" => Theme.Gold, "violet" => Theme.Violet, _ => Theme.Crystal };

        public void SaveTablet()
        {
            configuration.Save();
            ApplyLook();
            sender.SetState("prefs.tablet", new { widgets = configuration.TabletWidgets, dock = configuration.TabletDock, wallpaper = configuration.TabletWallpaper, accent = configuration.TabletAccent });
        }

        private void OnStateChanged(string section, JToken? value)
        {
            if (section != "prefs.tablet" || value is not JObject o) return;
            if (o["widgets"] is JArray w) configuration.TabletWidgets = w.Select(x => x.ToString()).Distinct().ToList();
            if (o["dock"] is JArray d) configuration.TabletDock = d.Select(x => x.ToString()).Distinct().ToList();
            if (o["wallpaper"]?.ToString() is { Length: > 0 } wp) configuration.TabletWallpaper = wp;
            if (o["accent"]?.ToString() is { Length: > 0 } ac) configuration.TabletAccent = ac;
            configuration.Save();
            ApplyLook();
        }

        // ---- frame loop -------------------------------------------------------------------------------------------
        public void Draw()
        {
            foreach (var app in _apps) app.Update();
            HoverToMarket();
            UpdateRetainersGlobal();
            _home.Tick();

            if (!SettingsVisible) return;
            HideScrollbars = configuration.HideScrollbars;
            if (configuration.IsMinimized) DrawSleep();
            else DrawTablet();
        }

        private void HoverToMarket()
        {
            if (!SettingsVisible || !configuration.EnableHoverItemFetching) return;
            ulong hover = _gameGui.HoveredItem;
            if (hover == 0) { _lastHoveredItem = 0; return; }
            if (hover != _lastHoveredItem) { _lastHoveredItem = hover; _hoverStartTime = DateTime.Now; return; }
            if ((DateTime.Now - _hoverStartTime).TotalSeconds <= 1.0) return;
            var id = (int)(hover % 500000);
            if (App<MarketApp>() is not { } market || market.GetSelectedItemId() == id) return;
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>()?.GetRow((uint)id);
            if (row.HasValue) OpenMarketAppWithItem(id, row.Value.Name.ToString(), row.Value.Icon.ToString(), row.Value.CanBeHq);
        }

        private void CheckVerification()
        {
            if (_verificationChecked || _isVerifying) return;
            if (_objectTable[0] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player) return;
            if (string.IsNullOrEmpty(configuration.XivHubId))
            {
                configuration.IsVerified = false; _isVerified = false; _verificationChecked = true;
                return;
            }
            _isVerifying = true;
            var name = player.Name.ToString();
            Task.Run(async () =>
            {
                var res = await sender.VerifyUserAsync(configuration.XivHubId, name);
                if (res == "Success") { configuration.IsVerified = true; _isVerified = true; }
                else if (res == "Invalid token" || res.StartsWith("Character mismatch"))
                {
                    configuration.IsVerified = false; configuration.XivHubId = ""; configuration.Save();
                    sender.MarkAuthRejected();
                    _isVerified = false; _errorMessage = res;
                }
                else _isVerified = true; // server or network trouble: keep working with the saved token
                _verificationChecked = true;
                _isVerifying = false;
            });
        }

        // ---- the tablet -------------------------------------------------------------------------------------------
        private const float Bezel = 16f, StatusH = 30f, DockH = 56f;

        private void DrawTablet()
        {
            AppScale = Sizes[Math.Clamp(configuration.TabletSize, 0, Sizes.Length - 1)];
            var size = BaseSize * AppScale;
            ImGui.SetNextWindowSize(size, ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);

            if (ImGui.Begin("Aetherhold###NAGU-PAD Premium Tablet", ref settingsVisible, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                Theme.Push();
                bool body = Fonts.PushBody(AppScale);
                FontScale = body ? 1f : AppScale * Fonts.TabletText;
                ImGui.SetWindowFontScale(FontScale);
                CheckVerification();

                var win = ImGui.GetWindowPos();
                var dl = ImGui.GetWindowDrawList();
                DrawBezel(dl, win, size);

                var sMin = win + Ui.S(Bezel, Bezel);
                var sMax = win + size - Ui.S(Bezel, Bezel);
                ImGui.PushClipRect(sMin, sMax, true); // ImGui's own clip: the constellation pops it, a draw-list clip would leak
                DrawWallpaper(dl, sMin, sMax);
                ImGui.PopClipRect();

                var cMin = sMin + new Vector2(0, Ui.S(StatusH));
                var cMax = sMax - new Vector2(0, Ui.S(DockH));
                bool locked = _verificationChecked && !_isVerified;

                if (locked) DrawSignIn(cMin, cMax);
                else if (_openApp == null) _home.Draw(cMin, cMax);
                else DrawAppScreen(_openApp, cMin, cMax);

                DrawDock(sMin, sMax, locked);
                if (_shadeOpen && !locked) DrawShade(cMin, cMax);
                DrawStatusBar(sMin, sMax);
                if (body) Fonts.PopBody();
                Theme.Pop();
            }
            ImGui.End();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        private ISharedImmediateTexture? _mark;
        public void DrawMark(ImDrawListPtr dl, Vector2 min, float size)
        {
            _mark ??= _textureProvider.GetFromManifestResource(typeof(PluginUI).Assembly, "Aetherhold.mark.png");
            if (_mark.GetWrapOrDefault() is { } wrap) dl.AddImage(wrap.Handle, min, min + new Vector2(size, size));
        }

        private void DrawBezel(ImDrawListPtr dl, Vector2 p, Vector2 size)
        {
            float r = Ui.S(30);
            dl.AddRectFilled(p, p + size, Theme.Night0.With(0.98f).U32(), r);
            dl.AddRect(p, p + size, Theme.Text3.With(0.45f).U32(), r, 0, Ui.S(2.2f));
            dl.AddRect(p + Ui.S(3, 3), p + size - Ui.S(3, 3), Theme.CrystalLine.U32(), r - Ui.S(3), 0, Ui.S(1));
            dl.AddCircleFilled(new Vector2(p.X + size.X / 2, p.Y + Ui.S(8)), Ui.S(2.6f), Theme.Night3.U32());       // camera
            dl.AddRectFilled(new Vector2(p.X + size.X / 2 - Ui.S(40), p.Y + size.Y - Ui.S(7)), new Vector2(p.X + size.X / 2 + Ui.S(40), p.Y + size.Y - Ui.S(5)), Theme.Text3.With(0.35f).U32(), Ui.S(2)); // grip
            dl.AddRect(p + Ui.S(Bezel, Bezel) - Ui.S(1, 1), p + size - Ui.S(Bezel, Bezel) + Ui.S(1, 1), Theme.Night3.U32(), Ui.S(14), 0, Ui.S(1.5f));

            // the bezel is the handle: drag it to move the tablet
            DragArea("bezel_t", p, new Vector2(size.X, Ui.S(Bezel)));
            DragArea("bezel_b", new Vector2(p.X, p.Y + size.Y - Ui.S(Bezel)), new Vector2(size.X, Ui.S(Bezel)));
            DragArea("bezel_l", new Vector2(p.X, p.Y + Ui.S(Bezel)), new Vector2(Ui.S(Bezel), size.Y - Ui.S(Bezel * 2)));
            DragArea("bezel_r", new Vector2(p.X + size.X - Ui.S(Bezel), p.Y + Ui.S(Bezel)), new Vector2(Ui.S(Bezel), size.Y - Ui.S(Bezel * 2)));
        }

        private static void DragArea(string id, Vector2 pos, Vector2 size)
        {
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, size);
            ImGui.SetItemAllowOverlap(); // buttons drawn on top of a drag area (sleep view) must still get the click
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
        }

        public static readonly (string Id, string Label)[] Wallpapers = { ("constellation", "Constellation"), ("crystal", "Crystal"), ("night", "Night"), ("dusk", "Dusk") };

        public void DrawWallpaper(ImDrawListPtr dl, Vector2 a, Vector2 b, bool preview = false)
        {
            uint top, bottom;
            switch (configuration.TabletWallpaper)
            {
                case "night": top = Theme.Night1.U32(); bottom = Theme.Night0.U32(); break;
                case "dusk": top = Theme.Night1.U32(); bottom = new Vector4(0.16f, 0.12f, 0.10f, 1f).U32(); break;
                default: top = Theme.Night2.U32(); bottom = Theme.Night0.U32(); break;
            }
            dl.AddRectFilledMultiColor(a, b, top, top, bottom, bottom);
            var size = b - a;
            switch (configuration.TabletWallpaper)
            {
                case "crystal":
                    Glow(dl, a + new Vector2(size.X * 0.82f, size.Y * 0.12f), size.Y * 0.7f, Theme.Crystal, 0.07f);
                    Glow(dl, a + new Vector2(size.X * 0.12f, size.Y * 0.95f), size.Y * 0.6f, Theme.Violet, 0.06f);
                    break;
                case "dusk":
                    Glow(dl, a + new Vector2(size.X * 0.5f, size.Y * 1.05f), size.Y * 0.8f, Theme.Gold, 0.08f);
                    break;
                case "constellation":
                    (preview ? _constellationPreview : _constellation).Draw(a, size, configuration.HideBackgroundAnimation, preview || configuration.HideScanline);
                    break;
            }
        }

        private static void Glow(ImDrawListPtr dl, Vector2 c, float radius, Vector4 color, float alpha)
        {
            for (int i = 12; i >= 1; i--) dl.AddCircleFilled(c, radius * i / 12f, color.With(alpha / 12f).U32(), 64);
        }

        // ---- status bar ---------------------------------------------------------------------------------------------
        private unsafe void DrawStatusBar(Vector2 sMin, Vector2 sMax)
        {
            var dl = ImGui.GetWindowDrawList();
            var a = sMin; var b = new Vector2(sMax.X, sMin.Y + Ui.S(StatusH));
            dl.AddRectFilled(a, b, Theme.Night0.With(0.62f).U32(), Ui.S(14), ImDrawFlags.RoundCornersTop);
            dl.AddLine(new Vector2(a.X, b.Y), b, Theme.CrystalLine.U32(), Ui.S(1));
            float midY = a.Y + Ui.S(StatusH) / 2;
            float textH = ImGui.GetTextLineHeight();

            // left: Eorzea time, weather, local time
            float x = a.X + Ui.S(14);
            var et = EorzeaTimeHelper.GetEorzeaTime();
            Ui.Text(dl, new Vector2(x, midY - textH / 2), $"ET {et:HH:mm}", Theme.Text);
            x += Ui.TextSize($"ET {et:HH:mm}").X + Ui.S(10);
            if (CurrentWeather() is { } weather)
            {
                if (weather.Icon > 0 && _textureProvider.GetFromGameIcon(new GameIconLookup((uint)weather.Icon)).GetWrapOrDefault() is { } tex)
                {
                    float s = Ui.S(18);
                    dl.AddImage(tex.Handle, new Vector2(x, midY - s / 2), new Vector2(x + s, midY + s / 2));
                    x += s + Ui.S(4);
                }
                Ui.Text(dl, new Vector2(x, midY - textH * 0.82f / 2), weather.Name, Theme.Text2, 0.82f);
                x += Ui.TextSize(weather.Name, 0.82f).X + Ui.S(10);
            }
            Ui.Text(dl, new Vector2(x, midY - textH * 0.82f / 2), $"LT {DateTime.Now:HH:mm}", Theme.Text3, 0.82f);
            float leftEnd = x + Ui.TextSize("LT 00:00", 0.82f).X + Ui.S(12);

            // centre: the mark and the name
            string role = sender.CurrentUserRole switch { "admin" => "ADMIN", "friend" => "FRIEND", _ => "" };
            float nameW = Ui.TextSize("AETHERHOLD", 0.78f).X + Ui.S(1.4f) * 10;
            float cx = (a.X + b.X) / 2 - (nameW + Ui.S(22)) / 2;
            DrawMark(dl, new Vector2(cx, midY - Ui.S(8)), Ui.S(16));
            Ui.Caps(dl, new Vector2(cx + Ui.S(22), midY - textH * 0.78f / 2), "Aetherhold");
            if (role != "") Ui.Caps(dl, new Vector2(cx + Ui.S(22) + nameW + Ui.S(8), midY - textH * 0.7f / 2), role, Theme.Text3, 0.7f);

            // right, from the edge inwards: close, sleep, notifications, sync, ventures, resets
            float bs = Ui.S(24), rx = b.X - Ui.S(10) - bs, by = midY - bs / 2;
            if (Ui.IconButton("sb_close", FontAwesomeIcon.Times, new Vector2(rx, by), bs, "Close")) settingsVisible = false;
            rx -= bs + Ui.S(2);
            if (Ui.IconButton("sb_sleep", FontAwesomeIcon.Moon, new Vector2(rx, by), bs, "Sleep (small view)")) { configuration.IsMinimized = true; configuration.Save(); }
            rx -= bs + Ui.S(6);
            int unseen = Notifications.Unseen;
            if (Ui.IconButton("sb_bell", FontAwesomeIcon.Bell, new Vector2(rx, by), bs, "Notifications", unseen > 0 ? Theme.Gold : null, _shadeOpen))
            {
                _shadeOpen = !_shadeOpen;
                if (_shadeOpen) Notifications.MarkAllSeen();
            }
            if (unseen > 0) Ui.Dot(ImGui.GetWindowDrawList(), new Vector2(rx + bs - Ui.S(4), by + Ui.S(5)), 3.2f, Theme.Gold);
            rx -= Ui.S(14);

            var (syncColor, syncText, pulse) = SyncState();
            Ui.Dot(dl, new Vector2(rx, midY), 3.6f, syncColor, pulse);
            ImGui.SetCursorScreenPos(new Vector2(rx - Ui.S(6), midY - Ui.S(8)));
            ImGui.InvisibleButton("sb_sync", Ui.S(12, 16));
            Ui.Tooltip(syncText);
            rx -= Ui.S(14);

            string vent = !RetainersKnown || ActiveRetainersCount == 0 ? "" : ReturnedRetainersCount == ActiveRetainersCount ? $"{ActiveRetainersCount}/{ActiveRetainersCount} back"
                : $"{ReturnedRetainersCount}/{ActiveRetainersCount} · {Ui.Span(TimeSpan.FromSeconds(ShortestRetainerVentureTime))}";
            if (vent != "")
            {
                float w = Ui.TextSize(vent, 0.82f).X;
                rx -= w;
                var col = ReturnedRetainersCount > 0 ? Theme.Gold : Theme.Text2;
                Ui.Text(dl, new Vector2(rx, midY - textH * 0.82f / 2), vent, col, 0.82f);
                float iw = Ui.IconSize(FontAwesomeIcon.Briefcase, 0.75f).X;
                rx -= iw + Ui.S(5);
                Ui.Icon(dl, new Vector2(rx, midY - textH * 0.75f / 2), FontAwesomeIcon.Briefcase, col, 0.75f);
                rx -= Ui.S(14);
            }

            string resets = $"Daily {Ui.Span(Resets.NextDaily() - DateTime.UtcNow)}  ·  Weekly {Ui.Span(Resets.NextWeekly() - DateTime.UtcNow)}";
            float rw = Ui.TextSize(resets, 0.82f).X;
            if (rx - rw > (a.X + b.X) / 2 + nameW / 2 + Ui.S(40))
            {
                rx -= rw;
                Ui.Text(dl, new Vector2(rx, midY - textH * 0.82f / 2), resets, Theme.Text3, 0.82f);
            }

            // empty status-bar space moves the tablet, like the bezel
            DragArea("sb_drag_l", new Vector2(leftEnd, a.Y), new Vector2(Math.Max(1, cx - leftEnd - Ui.S(4)), Ui.S(StatusH)));
            DragArea("sb_drag_c", new Vector2(cx, a.Y), new Vector2(nameW + Ui.S(22), Ui.S(StatusH)));
        }

        private (Vector4, string, bool) SyncState()
        {
            if (!configuration.IsSyncEnabled) return (Theme.Text3, "LiveSync is off (Settings)", false);
            if (!sender.HasToken) return (Theme.Text3, "Not connected: enter your plugin token", false);
            if (sender.AuthRejected) return (Theme.Bad, "Token rejected: sync paused", false);
            if (sender.LastSyncStatus == "Success" && (DateTime.Now - sender.LastSyncTime).TotalSeconds < 30) return (Theme.Live, $"Live · synced {Ui.Ago(sender.LastSyncTime)}", true);
            return (Theme.Warn, $"Connecting… ({sender.LastSyncStatus})", false);
        }

        private unsafe (string Name, int Icon)? CurrentWeather()
        {
            try
            {
                var wm = FFXIVClientStructs.FFXIV.Client.Game.WeatherManager.Instance();
                if (wm == null) return null;
                var id = wm->GetCurrentWeather();
                if (id == 0) return null;
                var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Weather>()?.GetRow(id);
                return row.HasValue ? (row.Value.Name.ToString(), row.Value.Icon) : null;
            }
            catch { return null; }
        }

        // ---- dock -------------------------------------------------------------------------------------------------
        public IEnumerable<IApp> DockApps() => configuration.TabletDock.Count == 0 ? _apps : configuration.TabletDock.Select(n => AppByName(n)).Where(a => a != null && a != _settingsApp)!;

        public static FontAwesomeIcon IconOf(IApp app) => app.Icon.Length > 0 ? (FontAwesomeIcon)app.Icon[0] : FontAwesomeIcon.Square;

        private void DrawDock(Vector2 sMin, Vector2 sMax, bool locked)
        {
            var dl = ImGui.GetWindowDrawList();
            var a = new Vector2(sMin.X, sMax.Y - Ui.S(DockH));
            dl.AddRectFilled(a, sMax, Theme.Night0.With(0.66f).U32(), Ui.S(14), ImDrawFlags.RoundCornersBottom);
            dl.AddLine(a, new Vector2(sMax.X, a.Y), Theme.CrystalLine.U32(), Ui.S(1));
            if (locked) return;

            var items = new List<(string Id, FontAwesomeIcon Icon, string Name, IApp? App, bool Active)>
            {
                ("home", FontAwesomeIcon.Home, "Home", null, _openApp == null),
            };
            foreach (var app in DockApps()) items.Add((app.Name, IconOf(app), app.Name, app, _openApp == app));
            items.Add(("settings", FontAwesomeIcon.Cog, "Settings", _settingsApp, _openApp == _settingsApp));

            float bs = Ui.S(40), gap = Ui.S(8), sep = Ui.S(14);
            float total = items.Count * bs + (items.Count - 1) * gap + sep * 2;
            float x = (sMin.X + sMax.X) / 2 - total / 2, y = a.Y + (Ui.S(DockH) - bs) / 2;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (i == 1 || i == items.Count - 1)
                {
                    dl.AddLine(new Vector2(x + sep / 2 - gap / 2, y + Ui.S(8)), new Vector2(x + sep / 2 - gap / 2, y + bs - Ui.S(8)), Theme.CrystalLine.U32(), Ui.S(1));
                    x += sep;
                }
                if (Ui.IconButton("dock_" + it.Id, it.Icon, new Vector2(x, y), bs, it.Name, null, it.Active)) Open(it.App);
                if (it.Active) dl.AddCircleFilled(new Vector2(x + bs / 2, y + bs + Ui.S(3)), Ui.S(2), Ui.Accent.U32());
                x += bs + gap;
            }
        }

        // ---- app screen -------------------------------------------------------------------------------------------
        private void DrawAppScreen(IApp app, Vector2 cMin, Vector2 cMax)
        {
            var dl = ImGui.GetWindowDrawList();
            float headH = Ui.S(34);
            dl.AddRectFilled(cMin, new Vector2(cMax.X, cMin.Y + headH), Theme.Night1.With(0.55f).U32());
            float midY = cMin.Y + headH / 2;
            if (Ui.IconButton("app_back", FontAwesomeIcon.ChevronLeft, new Vector2(cMin.X + Ui.S(8), midY - Ui.S(12)), Ui.S(24), "Home")) { Open(null); return; }
            Ui.Icon(dl, new Vector2(cMin.X + Ui.S(40), midY - ImGui.GetTextLineHeight() * 0.85f / 2), IconOf(app), Ui.Accent, 0.85f);
            Ui.Caps(dl, new Vector2(cMin.X + Ui.S(64), midY - ImGui.GetTextLineHeight() * 0.8f / 2), app.Name, Theme.Gold, 0.8f);

            var pad = Ui.S(14, 10);
            ImGui.SetCursorScreenPos(cMin + new Vector2(pad.X, headH + pad.Y));
            var flags = configuration.HideScrollbars ? ImGuiWindowFlags.NoScrollbar : ImGuiWindowFlags.None;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            if (UIHelper.BeginSmoothChild("AppScreen", cMax - cMin - new Vector2(pad.X * 2, headH + pad.Y * 2), false, flags))
            {
                // apps keep the old text size until their own rebuild fits the larger one (then add them to LargeTextApps)
                float shell = FontScale;
                if (!LargeTextApps.Contains(app.Name)) FontScale = shell / Fonts.TabletText;
                ImGui.SetWindowFontScale(FontScale);
                app.Draw();
                FontScale = shell;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // ---- notification shade ------------------------------------------------------------------------------------
        private void DrawShade(Vector2 cMin, Vector2 cMax)
        {
            ImGui.SetCursorScreenPos(cMin);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Theme.Night0.With(0.45f));
            ImGui.BeginChild("Shade", cMax - cMin, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.SetWindowFontScale(FontScale);
            var dl = ImGui.GetWindowDrawList();
            float w = Ui.S(400), pad = Ui.S(14);
            var pa = new Vector2(cMax.X - w - Ui.S(10), cMin.Y + Ui.S(6));
            var list = Notifications.All();
            float rowH = Ui.S(62);
            float h = Math.Min(cMax.Y - pa.Y - Ui.S(10), Ui.S(118) + Math.Max(1, list.Count) * (rowH + Ui.S(6)));
            var pb = new Vector2(pa.X + w, pa.Y + h);

            // a click outside the panel closes the shade
            ImGui.SetCursorScreenPos(cMin);
            bool bgClick = ImGui.InvisibleButton("shade_bg", cMax - cMin);
            ImGui.SetItemAllowOverlap();
            if (bgClick && !ImGui.IsMouseHoveringRect(pa, pb)) _shadeOpen = false;

            Ui.Panel(dl, pa, pb, Theme.Night1.With(0.97f), Theme.CrystalLine, 12);
            float y = pa.Y + pad;
            Ui.Caps(dl, new Vector2(pa.X + pad, y), "Today");
            y += Ui.S(22);
            var (dd, dt) = App<RoutinesApp>()?.Progress("daily") ?? (0, 0);
            string today = $"Daily {dd}/{dt}   ·   " + (RetainersKnown && ActiveRetainersCount > 0 ? $"Retainers {ReturnedRetainersCount}/{ActiveRetainersCount} back" : "Retainers: visit a bell")
                + $"   ·   Reset in {Ui.Span(Resets.NextDaily() - DateTime.UtcNow)}";
            Ui.Text(dl, new Vector2(pa.X + pad, y), today, Theme.Text2, 0.85f);
            y += Ui.S(30);

            Ui.Caps(dl, new Vector2(pa.X + pad, y), "Notifications");
            if (list.Count > 0 && Ui.Button("shade_clear", "Clear all", new Vector2(pb.X - pad - Ui.S(84), y - Ui.S(4)), Ui.S(84, 24))) Notifications.Clear();
            y += Ui.S(30);

            ImGui.SetCursorScreenPos(new Vector2(pa.X + pad, y));
            ImGui.BeginChild("shade_list", new Vector2(w - pad * 2, pb.Y - y - pad), false, configuration.HideScrollbars ? ImGuiWindowFlags.NoScrollbar : ImGuiWindowFlags.None);
            ImGui.SetWindowFontScale(FontScale);
            var ldl = ImGui.GetWindowDrawList();
            if (list.Count == 0) Ui.Text(ldl, ImGui.GetCursorScreenPos() + Ui.S(0, 6), "All quiet. Alarms, retainers and events show up here.", Theme.Text3, 0.9f);
            foreach (var n in list)
            {
                var ra = ImGui.GetCursorScreenPos();
                var size = new Vector2(ImGui.GetContentRegionAvail().X, rowH);
                bool click = Ui.Hit("note_" + n.Key, ra, size, out float hv);
                Ui.Panel(ldl, ra, ra + size, Theme.Night2.With(0.7f + 0.25f * hv), Theme.CrystalLine, 6);
                Ui.Chip(ldl, ra + Ui.S(10, 8), n.Source, n.Source == "Account" ? Theme.Bad : Ui.Accent, out var cs);
                Ui.Text(ldl, ra + new Vector2(Ui.S(18) + cs.X, Ui.S(9)), n.Title, Theme.Text, 0.95f);
                Ui.Text(ldl, ra + Ui.S(10, 34), n.Body, Theme.Text2, 0.82f);
                var ago = Ui.Ago(n.At);
                Ui.Text(ldl, new Vector2(ra.X + size.X - Ui.TextSize(ago, 0.75f).X - Ui.S(30), ra.Y + Ui.S(10)), ago, Theme.Text3, 0.75f);
                if (Ui.IconButton("note_x_" + n.Key, FontAwesomeIcon.Times, new Vector2(ra.X + size.X - Ui.S(24), ra.Y + Ui.S(6)), Ui.S(18), "Dismiss")) Notifications.Dismiss(n);
                else if (click && AppByName(n.App) is { } app) { Notifications.Dismiss(n); Open(app); }
                ImGui.SetCursorScreenPos(ra + new Vector2(0, rowH + Ui.S(6)));
                ImGui.Dummy(new Vector2(1, 0));
            }
            ImGui.EndChild();
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // ---- sign-in (lock screen) ---------------------------------------------------------------------------------
        private void DrawSignIn(Vector2 cMin, Vector2 cMax)
        {
            ImGui.SetCursorScreenPos(cMin);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.BeginChild("SignIn", cMax - cMin, false, ImGuiWindowFlags.NoScrollbar);
            ImGui.SetWindowFontScale(FontScale);
            var dl = ImGui.GetWindowDrawList();
            var c = (cMin + cMax) / 2;
            var et = EorzeaTimeHelper.GetEorzeaTime();
            string clock = $"{DateTime.Now:HH:mm}";
            Ui.Text(dl, new Vector2(c.X - Ui.TextSize(clock, 3.2f).X / 2, cMin.Y + Ui.S(40)), clock, Theme.Text, 3.2f);
            string sub = $"Eorzea {et:HH:mm}  ·  {DateTime.Now:dddd, d MMMM}";
            Ui.Text(dl, new Vector2(c.X - Ui.TextSize(sub, 0.9f).X / 2, cMin.Y + Ui.S(40) + Ui.TextSize(clock, 3.2f).Y), sub, Theme.Text2, 0.9f);

            var pa = new Vector2(c.X - Ui.S(250), c.Y - Ui.S(70));
            var pb = new Vector2(c.X + Ui.S(250), c.Y + Ui.S(170));
            Ui.Panel(dl, pa, pb, Theme.Night1.With(0.92f), Theme.CrystalLine, 14);
            DrawMark(dl, new Vector2(c.X - Ui.S(20), pa.Y + Ui.S(18)), Ui.S(40));
            float y = pa.Y + Ui.S(66);
            void Line(string t, Vector4 col, float sc = 0.9f) { Ui.Text(dl, new Vector2(c.X - Ui.TextSize(t, sc).X / 2, y), t, col, sc); y += Ui.TextSize(t, sc).Y + Ui.S(4); }
            Line("Connect this character to Aetherhold", Theme.Text, 1.05f);
            Line("Copy your plugin token from Settings on the website and paste it here.", Theme.Text2, 0.82f);
            y += Ui.S(10);

            ImGui.SetCursorScreenPos(new Vector2(pa.X + Ui.S(30), y));
            ImGui.SetNextItemWidth(Ui.S(440));
            ImGui.InputTextWithHint("##token", "Plugin token (xh_…)", ref _inputToken, 128, ImGuiInputTextFlags.Password);
            y += Ui.S(38);
            if (Ui.Button("signin_web", "Get my token", new Vector2(pa.X + Ui.S(30), y), Ui.S(210, 32), Kind.Ghost, FontAwesomeIcon.ExternalLinkAlt))
                Dalamud.Utility.Util.OpenLink(HubClient.BaseUrl + "/settings");
            if (Ui.Button("signin_go", _isVerifying ? "Checking…" : "Connect", new Vector2(pb.X - Ui.S(240), y), Ui.S(210, 32), Kind.Primary, FontAwesomeIcon.Link) && !_isVerifying)
                Connect();
            y += Ui.S(42);
            if (_errorMessage != "") Line(_errorMessage, Theme.Warn, 0.82f);
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private void Connect()
        {
            if (_objectTable[0] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player) { _errorMessage = "Log in with your character first."; return; }
            var name = player.Name.ToString();
            var token = _inputToken.Trim();
            _isVerifying = true;
            _errorMessage = "Checking…";
            Task.Run(async () =>
            {
                var res = await sender.VerifyUserAsync(token, name);
                if (res == "Success")
                {
                    configuration.XivHubId = token; configuration.IsVerified = true; configuration.Save();
                    sender.AttachAuthHeader(configuration);
                    _isVerified = true; _errorMessage = "";
                }
                else _errorMessage = res;
                _isVerifying = false;
            });
        }

        public void SignOut()
        {
            configuration.XivHubId = ""; configuration.IsVerified = false; configuration.Save();
            _isVerified = false; _verificationChecked = true; _errorMessage = ""; _inputToken = "";
            sender.AttachAuthHeader(configuration);
        }

        // ---- sleep view (minimised) --------------------------------------------------------------------------------
        private void DrawSleep()
        {
            var size = new Vector2(300, 64);
            ImGui.SetNextWindowSize(size, ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            float saved = AppScale;
            AppScale = 1f;
            if (ImGui.Begin("Aetherhold Widget###NAGU-PAD Widget", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar))
            {
                var p = ImGui.GetWindowPos();
                var dl = ImGui.GetWindowDrawList();
                dl.AddRectFilled(p, p + size, Theme.Night0.With(0.96f).U32(), 18);
                dl.AddRect(p, p + size, Theme.Text3.With(0.45f).U32(), 18, 0, 1.8f);
                dl.AddRect(p + new Vector2(5, 5), p + size - new Vector2(5, 5), Theme.CrystalLine.U32(), 14, 0, 1);
                DragArea("sleep_drag", p, size);
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { configuration.IsMinimized = false; configuration.Save(); }

                DrawMark(dl, p + new Vector2(14, 18), 28);
                var et = EorzeaTimeHelper.GetEorzeaTime();
                Ui.Text(dl, p + new Vector2(50, 12), $"ET {et:HH:mm}", Theme.Text, 1.1f);
                var (syncColor, _, pulse) = SyncState();
                Ui.Dot(dl, p + new Vector2(124, 20), 3.2f, syncColor, pulse);

                // second line: what needs you (each part follows its overlay switch)
                float x = p.X + 50, y = p.Y + 36;
                if (configuration.ShowMinimizedOverlay && configuration.ShowMinimizedRetainerOverlay && RetainersKnown && ActiveRetainersCount > 0)
                {
                    bool all = ReturnedRetainersCount == ActiveRetainersCount;
                    var col = all ? Theme.Gold.With(0.6f + 0.4f * (float)((Math.Sin(ImGui.GetTime() * 5) + 1) / 2)) : Theme.Text2;
                    Ui.Icon(dl, new Vector2(x, y + 1), FontAwesomeIcon.Briefcase, col, 0.75f);
                    var t = all ? $"{ActiveRetainersCount}/{ActiveRetainersCount} back" : $"{ReturnedRetainersCount}/{ActiveRetainersCount} {Ui.Span(TimeSpan.FromSeconds(ShortestRetainerVentureTime))}";
                    Ui.Text(dl, new Vector2(x + 16, y), t, col, 0.82f);
                    x += 22 + Ui.TextSize(t, 0.82f).X;
                }
                if (configuration.ShowMinimizedOverlay && configuration.ShowMinimizedGatheringOverlay && App<GatheringApp>() is { ActiveFavoriteCount: > 0 } g)
                {
                    Ui.Icon(dl, new Vector2(x, y + 1), FontAwesomeIcon.Star, Theme.Gold, 0.75f);
                    Ui.Text(dl, new Vector2(x + 16, y), $"{g.ActiveFavoriteCount} up", Theme.Gold, 0.82f);
                }

                int unseen = Notifications.Unseen;
                if (Ui.IconButton("sleep_bell", FontAwesomeIcon.Bell, p + new Vector2(178, 18), 28, unseen > 0 ? $"{unseen} new" : "Notifications", unseen > 0 ? Theme.Gold : null))
                { configuration.IsMinimized = false; _shadeOpen = true; Notifications.MarkAllSeen(); configuration.Save(); }
                if (unseen > 0) Ui.Dot(dl, p + new Vector2(202, 22), 3.2f, Theme.Gold);
                if (Ui.IconButton("sleep_wake", FontAwesomeIcon.Expand, p + new Vector2(212, 18), 28, "Wake the tablet"))
                { configuration.IsMinimized = false; configuration.Save(); }
                if (Ui.IconButton("sleep_close", FontAwesomeIcon.Times, p + new Vector2(250, 18), 28, "Close")) settingsVisible = false;
            }
            ImGui.End();
            AppScale = saved;
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);
        }

        // ---- retainers ---------------------------------------------------------------------------------------------
        private long? _lastVentureEnd = -1;
        private bool _retainerAlarmFired;
        private DateTime _retainerAlarmAt = DateTime.MinValue;

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
            int active = 0, returned = 0;
            long shortest = long.MaxValue, latestEnd = 0, now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (uint i = 0; i < 10; i++)
            {
                var ret = rm->GetRetainerBySortedIndex(i);
                if (ret == null || ret->RetainerId == 0 || string.IsNullOrEmpty(ret->NameString)) continue;
                active++;
                if (ret->VentureId == 0) continue;
                latestEnd = Math.Max(latestEnd, ret->VentureComplete);
                long diff = ret->VentureComplete - now;
                if (diff <= 0) returned++;
                else shortest = Math.Min(shortest, diff);
            }
            RetainersKnown = rm->IsReady;
            ActiveRetainersCount = active;
            ReturnedRetainersCount = returned;
            ShortestRetainerVentureTime = shortest == long.MaxValue ? 0 : shortest;
            if (rm->IsReady) PushVentureEnd(latestEnd);

            if (active > 0 && returned == active)
            {
                // once when they are all back; again every minute if "fire once" is off
                bool repeat = !configuration.RetainerAudioFireOnce && (DateTime.Now - _retainerAlarmAt).TotalSeconds > 60;
                if (!_retainerAlarmFired || repeat)
                {
                    if (!_retainerAlarmFired) Notifications.Post("retainers", "Retainers", "All retainers are back", $"{returned} of {active} ventures done. Visit a summoning bell.");
                    if (configuration.RetainerAudioEnabled) try { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(1); } catch { }
                    _retainerAlarmFired = true;
                    _retainerAlarmAt = DateTime.Now;
                }
            }
            else _retainerAlarmFired = false;
        }
    }

    /// <summary>Daily 15:00 UTC, weekly Tuesday 08:00 UTC.</summary>
    public static class Resets
    {
        public static DateTime NextDaily()
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddHours(15);
            return now >= next ? next.AddDays(1) : next;
        }

        public static DateTime NextWeekly()
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddHours(8);
            int days = ((int)DayOfWeek.Tuesday - (int)next.DayOfWeek + 7) % 7;
            if (days == 0 && now >= next) days = 7;
            return next.AddDays(days);
        }
    }

    /// <summary>Settings as an app (dock, right end).</summary>
    public sealed class SettingsApp : IApp
    {
        private readonly Action _draw;
        public SettingsApp(Action draw) => _draw = draw;
        public string Name => "Settings";
        public string Icon => ((char)FontAwesomeIcon.Cog).ToString();
        public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }
        public void Draw() => _draw();
        public void Dispose() { }
    }
}
