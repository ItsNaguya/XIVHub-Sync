using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using XIVHubCompanion.Apps;

namespace XIVHubCompanion.Tablet
{
    /// <summary>Home screen: greeting and clocks, live widgets, the app grid. Layout is editable and synced (prefs.tablet).</summary>
    public sealed class HomeScreen
    {
        private readonly PluginUI _ui;
        private readonly Configuration _config;
        private readonly HubClient _hub;
        private readonly IDataManager _data;
        private readonly IObjectTable _objects;
        private bool _edit;

        public static readonly (string Id, string Title, FontAwesomeIcon Icon, string? App)[] Widgets =
        {
            ("retainers", "Retainers", FontAwesomeIcon.Briefcase, null),
            ("routines", "Routines", FontAwesomeIcon.ClipboardCheck, "Routines"),
            ("gathering", "Favourite nodes", FontAwesomeIcon.Leaf, "Gathering & Fishing"),
            ("market", "Shopping", FontAwesomeIcon.ShoppingCart, "Market"),
            ("crafting", "Crafting queue", FontAwesomeIcon.Hammer, "Crafting"),
            ("events", "Upcoming events", FontAwesomeIcon.CalendarAlt, "Events"),
        };

        public HomeScreen(PluginUI ui, Configuration config, HubClient hub, IDataManager data, IObjectTable objects)
        {
            _ui = ui; _config = config; _hub = hub; _data = data; _objects = objects;
        }

        // ---- background: notifications that don't belong to one app ----------------------------------------------
        private DateTime _nextTick = DateTime.MinValue, _lastDaily = Resets.NextDaily(), _lastWeekly = Resets.NextWeekly();

        public void Tick()
        {
            if (DateTime.Now < _nextTick) return;
            _nextTick = DateTime.Now.AddSeconds(20);
            if (Resets.NextDaily() != _lastDaily) { _lastDaily = Resets.NextDaily(); Notifications.Post("reset:daily", "Routines", "Daily reset", "Your daily routines are fresh.", "Routines"); }
            if (Resets.NextWeekly() != _lastWeekly) { _lastWeekly = Resets.NextWeekly(); Notifications.Post("reset:weekly", "Routines", "Weekly reset", "Weekly routines and lockouts are back.", "Routines"); }
            if (_config.IsVerified && _ui.App<EventsApp>() is { } events)
                foreach (var (title, start) in events.NextEvents(5))
                {
                    var left = start - DateTime.Now;
                    if (left.TotalMinutes is > 0 and <= 15)
                        Notifications.Post($"event:{title}:{start:o}", "Events", title, $"Starts at {start:HH:mm} (in {Ui.Span(left)})", "Events");
                }
        }

        // ---- drawing ----------------------------------------------------------------------------------------------
        public void Draw(Vector2 min, Vector2 max)
        {
            ImGui.SetCursorScreenPos(min);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
            ImGui.BeginChild("Home", max - min, false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            ImGui.SetWindowFontScale(PluginUI.AppScale);
            var dl = ImGui.GetWindowDrawList();
            float pad = Ui.S(22), gap = Ui.S(14);
            float x0 = min.X + pad, w = max.X - min.X - pad * 2;
            float y = min.Y + Ui.S(16);

            y = DrawHero(dl, x0, y, w) + gap;

            // widgets: 3 columns
            var shown = _config.TabletWidgets.Select(id => Widgets.FirstOrDefault(x => x.Id == id)).Where(x => x.Id != null).ToList();
            Ui.Caps(dl, new Vector2(x0, y), "At a glance");
            if (Ui.Button("home_edit", _edit ? "Done" : "Edit", new Vector2(x0 + w - Ui.S(70), y - Ui.S(5)), Ui.S(70, 24), _edit ? Kind.Primary : Kind.Ghost, _edit ? FontAwesomeIcon.Check : FontAwesomeIcon.Pen)) _edit = !_edit;
            y += Ui.S(24);
            int cols = 3;
            float cw = (w - gap * (cols - 1)) / cols, ch = Ui.S(150);
            for (int i = 0; i < shown.Count; i++)
            {
                var p = new Vector2(x0 + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap));
                DrawWidget(dl, shown[i], p, new Vector2(cw, ch), i, shown.Count);
            }
            int rows = Math.Max(1, (shown.Count + cols - 1) / cols);
            if (shown.Count == 0) Ui.Text(dl, new Vector2(x0, y + Ui.S(6)), "No widgets. Press Edit to add some.", Theme.Text3, 0.9f);
            y += rows * (ch + gap);

            if (_edit)
            {
                var hidden = Widgets.Where(x => !_config.TabletWidgets.Contains(x.Id)).ToList();
                float x = x0;
                if (hidden.Count > 0) { Ui.Text(dl, new Vector2(x, y + Ui.S(4)), "Add:", Theme.Text3, 0.85f); x += Ui.S(40); }
                foreach (var hw in hidden)
                {
                    float bw = Ui.TextSize(hw.Title).X + Ui.S(42);
                    if (Ui.Button("add_" + hw.Id, hw.Title, new Vector2(x, y), new Vector2(bw, Ui.S(26)), Kind.Ghost, FontAwesomeIcon.Plus))
                    { _config.TabletWidgets.Add(hw.Id); _ui.SaveTablet(); }
                    x += bw + Ui.S(8);
                }
                y += Ui.S(36);
            }

            // app grid
            Ui.Caps(dl, new Vector2(x0, y), "Apps");
            y += Ui.S(24);
            DrawAppGrid(dl, x0, y, w);

            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        private float DrawHero(ImDrawListPtr dl, float x, float y, float w)
        {
            var player = PlayerName();
            int hour = DateTime.Now.Hour;
            string greet = hour < 5 ? "Good night" : hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            Ui.Text(dl, new Vector2(x, y), player.Name == null ? greet : $"{greet}, {player.Name.Split(' ')[0]}", Theme.Text, 1.6f);
            float lh = Ui.TextSize("A", 1.6f).Y;
            string sub = player.Name == null ? "Log in with a character to start." : $"{player.Name}  ·  {player.World}  ·  {player.Job}";
            Ui.Text(dl, new Vector2(x, y + lh + Ui.S(2)), sub, Theme.Text2, 0.9f);

            var et = EorzeaTimeHelper.GetEorzeaTime();
            string big = $"{et:HH:mm}";
            var bs = Ui.TextSize(big, 2.2f);
            Ui.Text(dl, new Vector2(x + w - bs.X, y - Ui.S(4)), big, Theme.Text, 2.2f);
            Ui.Caps(dl, new Vector2(x + w - bs.X - Ui.S(74), y + bs.Y * 0.42f), "Eorzea", Theme.Gold, 0.72f);
            string local = $"Local {DateTime.Now:HH:mm}";
            Ui.Text(dl, new Vector2(x + w - Ui.TextSize(local, 0.82f).X, y + bs.Y - Ui.S(2)), local, Theme.Text3, 0.82f);
            return y + Math.Max(lh + Ui.S(26), bs.Y + Ui.S(18));
        }

        private (string? Name, string World, string Job) PlayerName()
        {
            try
            {
                if (_objects[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter p)
                    return (p.Name.TextValue, p.HomeWorld.Value.Name.ToString(), $"{p.ClassJob.Value.Abbreviation} {p.Level}");
            }
            catch { }
            return (null, "", "");
        }

        // ---- widgets ----------------------------------------------------------------------------------------------
        private void DrawWidget(ImDrawListPtr dl, (string Id, string Title, FontAwesomeIcon Icon, string? App) wd, Vector2 p, Vector2 size, int index, int count)
        {
            bool click = Ui.Hit("w_" + wd.Id, p, size, out float hv) && !_edit;
            Ui.Panel(dl, p, p + size, Theme.Night1.With(0.78f + 0.12f * hv), wd.App != null ? Ui.Accent.With(0.22f + 0.3f * hv) : Theme.CrystalLine, 12);
            float pad = Ui.S(14);
            Ui.Icon(dl, p + new Vector2(pad, pad + Ui.S(1)), wd.Icon, Ui.Accent, 0.8f);
            Ui.Caps(dl, p + new Vector2(pad + Ui.S(22), pad), wd.Title, Theme.Gold, 0.72f);
            var body = p + new Vector2(pad, pad + Ui.S(28));
            float bw = size.X - pad * 2;

            switch (wd.Id)
            {
                case "retainers": Retainers(dl, body, bw); break;
                case "routines": Routines(dl, body, bw); break;
                case "gathering": Gathering(dl, body, bw); break;
                case "market": Market(dl, body, bw); break;
                case "crafting": Crafting(dl, body, bw); break;
                case "events": Events(dl, body, bw); break;
            }

            if (_edit)
            {
                float bs = Ui.S(24), ex = p.X + size.X - pad - bs, ey = p.Y + Ui.S(10);
                if (Ui.IconButton("w_rm_" + wd.Id, FontAwesomeIcon.EyeSlash, new Vector2(ex, ey), bs, "Hide")) { _config.TabletWidgets.Remove(wd.Id); _ui.SaveTablet(); }
                if (index < count - 1 && Ui.IconButton("w_r_" + wd.Id, FontAwesomeIcon.ArrowRight, new Vector2(ex - bs - Ui.S(2), ey), bs, "Move right")) Move(wd.Id, +1);
                if (index > 0 && Ui.IconButton("w_l_" + wd.Id, FontAwesomeIcon.ArrowLeft, new Vector2(ex - (bs + Ui.S(2)) * 2, ey), bs, "Move left")) Move(wd.Id, -1);
            }
            else if (click && _ui.AppByName(wd.App) is { } app) _ui.Open(app);
        }

        private void Move(string id, int dir)
        {
            var l = _config.TabletWidgets;
            int i = l.IndexOf(id), j = i + dir;
            if (i < 0 || j < 0 || j >= l.Count) return;
            (l[i], l[j]) = (l[j], l[i]);
            _ui.SaveTablet();
        }

        private static float Big(ImDrawListPtr dl, Vector2 p, string value, string label, Vector4 color)
        {
            Ui.Text(dl, p, value, color, 1.7f);
            var vs = Ui.TextSize(value, 1.7f);
            Ui.Text(dl, p + new Vector2(vs.X + Ui.S(8), vs.Y - Ui.TextSize(label, 0.85f).Y - Ui.S(3)), label, Theme.Text2, 0.85f);
            return vs.Y;
        }

        private static void Muted(ImDrawListPtr dl, Vector2 p, string text) => Ui.Text(dl, p, text, Theme.Text3, 0.85f);

        private void Retainers(ImDrawListPtr dl, Vector2 p, float w)
        {
            if (!PluginUI.RetainersKnown || PluginUI.ActiveRetainersCount == 0) { Muted(dl, p, "Visit a summoning bell once"); Muted(dl, p + Ui.S(0, 20), "to see your ventures here."); return; }
            int act = PluginUI.ActiveRetainersCount, ret = PluginUI.ReturnedRetainersCount;
            bool all = ret == act;
            float h = Big(dl, p, $"{ret}/{act}", "back", all ? Theme.Gold : Theme.Text);
            Ui.Progress(dl, p + new Vector2(0, h + Ui.S(10)), w, act == 0 ? 0 : (float)ret / act, all ? Theme.Gold : Ui.Accent);
            Ui.Text(dl, p + new Vector2(0, h + Ui.S(22)), all ? "All ventures are done." : $"Next back in {Ui.Span(TimeSpan.FromSeconds(PluginUI.ShortestRetainerVentureTime))}", all ? Theme.Gold : Theme.Text2, 0.88f);
        }

        private void Routines(ImDrawListPtr dl, Vector2 p, float w)
        {
            if (_ui.App<RoutinesApp>() is not { } r) return;
            float y = p.Y;
            foreach (var (type, label, reset) in new[] { ("daily", "Daily", Resets.NextDaily()), ("weekly", "Weekly", Resets.NextWeekly()) })
            {
                var (done, total) = r.Progress(type);
                Ui.Text(dl, new Vector2(p.X, y), $"{label}  {done}/{total}", done == total && total > 0 ? Theme.Live : Theme.Text, 0.95f);
                string rs = $"reset {Ui.Span(reset - DateTime.UtcNow)}";
                Ui.Text(dl, new Vector2(p.X + w - Ui.TextSize(rs, 0.8f).X, y + Ui.S(2)), rs, Theme.Text3, 0.8f);
                Ui.Progress(dl, new Vector2(p.X, y + Ui.S(22)), w, total == 0 ? 0 : (float)done / total, done == total && total > 0 ? Theme.Live : Ui.Accent);
                y += Ui.S(38);
            }
        }

        private void Gathering(ImDrawListPtr dl, Vector2 p, float w)
        {
            var list = _ui.App<GatheringApp>()?.FavoriteTimers() ?? new();
            if (list.Count == 0) { Muted(dl, p, "Star nodes in Gathering"); Muted(dl, p + Ui.S(0, 20), "to see their timers here."); return; }
            float y = p.Y;
            foreach (var (node, up, secs) in list.Take(3))
            {
                Ui.Dot(dl, new Vector2(p.X + Ui.S(4), y + Ui.S(9)), 3.4f, up ? Theme.Live : Theme.Text3, up);
                string t = up ? $"up · {Ui.Span(TimeSpan.FromSeconds(secs))} left" : $"in {Ui.Span(TimeSpan.FromSeconds(secs))}";
                float tw = Ui.TextSize(t, 0.82f).X;
                string name = Fit(node.name ?? "", w - tw - Ui.S(24), 0.9f);
                Ui.Text(dl, new Vector2(p.X + Ui.S(14), y), name, Theme.Text, 0.9f);
                Ui.Text(dl, new Vector2(p.X + w - tw, y + Ui.S(1)), t, up ? Theme.Live : Theme.Text2, 0.82f);
                Ui.Text(dl, new Vector2(p.X + Ui.S(14), y + Ui.S(16)), Fit(node.zone ?? "", w - Ui.S(14), 0.74f), Theme.Text3, 0.74f);
                y += Ui.S(32);
            }
        }

        private void Market(ImDrawListPtr dl, Vector2 p, float w)
        {
            if (_ui.App<MarketApp>() is not { } m) return;
            if (m.CartLines == 0 && m.RouteStops == 0) { Muted(dl, p, "Your cart is empty."); Muted(dl, p + Ui.S(0, 20), "Add items in Market or on the website."); return; }
            float h = Big(dl, p, $"{m.CartLines}", m.CartLines == 1 ? "item in the cart" : "items in the cart", Theme.Text);
            if (m.RouteStops > 0)
            {
                Ui.Progress(dl, p + new Vector2(0, h + Ui.S(10)), w, (float)m.RouteStopsDone / m.RouteStops, Ui.Accent);
                Ui.Text(dl, p + new Vector2(0, h + Ui.S(22)), $"Route {m.RouteStopsDone}/{m.RouteStops} stops  ·  {Ui.Gil(m.RouteCost)} gil", Theme.Text2, 0.88f);
            }
            else Ui.Text(dl, p + new Vector2(0, h + Ui.S(14)), $"{m.CartQuantity} pieces · no route yet", Theme.Text2, 0.88f);
        }

        private void Crafting(ImDrawListPtr dl, Vector2 p, float w)
        {
            var q = _config.CraftingActivePipeline;
            if (q.Count == 0) { Muted(dl, p, "Nothing queued."); Muted(dl, p + Ui.S(0, 20), "Plan crafts in Crafting."); return; }
            float h = Big(dl, p, $"{q.Count}", q.Count == 1 ? "recipe queued" : "recipes queued", Theme.Text);
            var recipes = _data.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
            float y = p.Y + h + Ui.S(8);
            foreach (var item in q.Take(2))
            {
                string name = "Recipe " + item.RecipeId;
                try { if (recipes?.GetRow(item.RecipeId) is { } r) name = r.ItemResult.Value.Name.ToString(); } catch { }
                Ui.Text(dl, new Vector2(p.X, y), Fit($"{item.Amount}×  {name}", w, 0.86f), Theme.Text2, 0.86f);
                y += Ui.S(18);
            }
        }

        private void Events(ImDrawListPtr dl, Vector2 p, float w)
        {
            var list = _ui.App<EventsApp>()?.NextEvents(3) ?? new();
            if (list.Count == 0) { Muted(dl, p, "No upcoming events."); return; }
            float y = p.Y;
            foreach (var (title, start) in list)
            {
                var left = start - DateTime.Now;
                string when = left.TotalMinutes < 0 ? "now" : left.TotalHours < 24 ? $"in {Ui.Span(left)}" : start.ToString("ddd d MMM, HH:mm");
                float tw = Ui.TextSize(when, 0.8f).X;
                Ui.Text(dl, new Vector2(p.X, y), Fit(title, w - tw - Ui.S(10), 0.88f), Theme.Text, 0.88f);
                Ui.Text(dl, new Vector2(p.X + w - tw, y + Ui.S(1)), when, left.TotalHours < 1 ? Theme.Gold : Theme.Text2, 0.8f);
                y += Ui.S(26);
            }
        }

        private static string Fit(string text, float width, float scale)
        {
            if (Ui.TextSize(text, scale).X <= width) return text;
            while (text.Length > 1 && Ui.TextSize(text + "…", scale).X > width) text = text[..^1];
            return text + "…";
        }

        // ---- app grid ---------------------------------------------------------------------------------------------
        private void DrawAppGrid(ImDrawListPtr dl, float x0, float y, float w)
        {
            var apps = _ui.Apps;
            float tile = Ui.S(88), gap = Ui.S(14);
            int perRow = Math.Max(1, (int)((w + gap) / (tile + gap)));
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                var p = new Vector2(x0 + (i % perRow) * (tile + gap), y + (i / perRow) * (tile + Ui.S(30)));
                bool click = Ui.Hit("app_" + app.Name, p, new Vector2(tile, tile + Ui.S(22)), out float hv);
                bool docked = _config.TabletDock.Count == 0 || _config.TabletDock.Contains(app.Name);
                if (ImGui.BeginPopupContextItem("app_ctx_" + app.Name))
                {
                    if (ImGui.MenuItem(docked ? "Remove from dock" : "Add to dock")) ToggleDock(app.Name);
                    ImGui.EndPopup();
                }
                Ui.Tooltip(docked ? "Right-click: remove from dock" : "Right-click: add to dock");
                Ui.Panel(dl, p, p + new Vector2(tile, tile), Theme.Night2.With(0.75f + 0.2f * hv), Ui.Accent.With(0.2f + 0.45f * hv), 12);
                Ui.IconCentered(dl, p + new Vector2(tile, tile) / 2, app.Icon, UIHelper.LerpColor(Ui.Accent, Theme.Text, hv), 1.9f + 0.15f * hv);
                string label = Fit(app.Name, tile + gap, 0.82f);
                Ui.Text(dl, new Vector2(p.X + tile / 2 - Ui.TextSize(label, 0.82f).X / 2, p.Y + tile + Ui.S(5)), label, Theme.Text2, 0.82f);
                if (click) _ui.Open(app);
            }
        }

        private void ToggleDock(string name)
        {
            var dock = _config.TabletDock.Count == 0 ? _ui.Apps.Select(a => a.Name).ToList() : _config.TabletDock;
            if (dock.Contains(name)) dock.Remove(name); else dock.Add(name);
            _config.TabletDock = dock;
            _ui.SaveTablet();
        }
    }
}
