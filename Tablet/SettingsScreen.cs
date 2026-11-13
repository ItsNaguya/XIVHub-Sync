using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Apps;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion
{
    // The Settings app (3.0 rebuild): tabs with cut-corner cards in the tablet look, sized with Ui.S and the larger text.
    // Synced with the website: prefs.tablet (dock, wallpaper, accent; SaveTablet), prefs.theme (the website's dark/light),
    // routines.checklist and gathering.alarms (Data & resets, same actions as the website's settings page).
    // Plugin only: tablet size, the sleep view, startup, scrollbars and the per-app switches.
    public partial class PluginUI
    {
        private static readonly string[] SizeNames = { "Extra small", "Small", "Standard", "Large", "Extra large" };
        private static readonly string[] SizeShort = { "XS", "S", "M", "L", "XL" };
        private static readonly (string Id, string Label, Vector4 Color)[] Accents = { ("crystal", "Crystal", Theme.Crystal), ("gold", "Gold", Theme.Gold), ("violet", "Violet", Theme.Violet) };
        private static readonly (string V, string Label, FontAwesomeIcon Icon)[] SettingsTabs =
        {
            ("look", "Look", FontAwesomeIcon.Palette), ("dock", "Dock", FontAwesomeIcon.GripHorizontal), ("behaviour", "Behaviour", FontAwesomeIcon.SlidersH),
            ("apps", "Apps", FontAwesomeIcon.ThLarge), ("account", "Account & data", FontAwesomeIcon.UserCircle),
        };

        private string _setTab = "look";
        private string _setConfirm = ""; // the action waiting for Yes / No (Account & data)

        // ---- small helpers --------------------------------------------------------------------------------------------
        private static float SLH => Ui.TextSize("Ag").Y;
        private static float SLHs(float scale) => Ui.TextSize("Ag", scale).Y;
        private static float SBtnH => SLH + Ui.S(12);
        private static float SCapsH => Ui.TextSize("A", 0.78f).Y;

        /// <summary>Word-wrapped text; returns the y below it.</summary>
        private static float SWrap(ImDrawListPtr dl, float x, float y, float w, string text, Vector4 col, float scale = 1f)
        {
            float lh = SLHs(scale);
            string line = "";
            foreach (var word in text.Split(' '))
            {
                var t = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Ui.TextSize(t, scale).X > w) { Ui.Text(dl, new Vector2(x, y), line, col, scale); y += lh + Ui.S(2); line = word; }
                else line = t;
            }
            if (line.Length > 0) { Ui.Text(dl, new Vector2(x, y), line, col, scale); y += lh; }
            return y;
        }

        /// <summary>A card: gold caps title, optional note, then the body. The panel is drawn behind afterwards (draw-list channels).</summary>
        private static float SCard(string title, string? note, float x, float y, float w, Func<float, float, float, float> body)
        {
            var dl = Ui.Dl;
            float pad = Ui.S(16);
            dl.ChannelsSplit(2);
            dl.ChannelsSetCurrent(1);
            float cy = y + pad;
            Ui.Caps(dl, new Vector2(x + pad, cy), title);
            cy += SCapsH + Ui.S(8);
            if (note != null) cy = SWrap(dl, x + pad, cy, w - pad * 2, note, Theme.Text3, 0.88f) + Ui.S(8);
            cy += Ui.S(2);
            cy = body(x + pad, cy, w - pad * 2);
            float bottom = cy + pad - Ui.S(4);
            dl.ChannelsSetCurrent(0);
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, bottom), Theme.Night2.With(0.72f), Theme.CrystalLine.With(0.32f), 12f);
            dl.ChannelsMerge();
            return bottom;
        }

        /// <summary>The switch graphic (scaled); the caller owns the hit area.</summary>
        private static Vector2 SSwitchSize => new(Math.Max(Ui.S(22), SLH + Ui.S(4)) * 1.8f, Math.Max(Ui.S(22), SLH + Ui.S(4)));

        private static void SSwitch(ImDrawListPtr dl, string id, Vector2 p, bool on, float hover)
        {
            var sz = SSwitchSize;
            float k = UIHelper.GetHoverState(ImGui.GetID(id + "_knob"), on, 15f);
            float r = sz.Y / 2;
            dl.AddRectFilled(p, p + sz, UIHelper.LerpColor(Theme.Night0.With(0.85f), Ui.Accent.With(0.45f), k).U32(), r);
            dl.AddRect(p, p + sz, UIHelper.LerpColor(Theme.CrystalLine.With(0.35f + 0.3f * hover), Ui.Accent, k * 0.8f).U32(), r, 0, Ui.S(1.2f));
            var c = new Vector2(p.X + r + (sz.X - sz.Y) * k, p.Y + r);
            dl.AddCircleFilled(c, r - Ui.S(3.5f), UIHelper.LerpColor(Theme.Text2, Theme.Text, Math.Max(k, hover)).U32());
        }

        /// <summary>A whole-row switch with an optional hint line; returns the y below it.</summary>
        private float SToggle(string id, string label, string? hint, bool on, float x, float y, float w, Action<bool> set)
        {
            var dl = Ui.Dl;
            var sz = SSwitchSize;
            float tx = x + sz.X + Ui.S(12), tw = w - (tx - x);
            float hintH = 0;
            if (hint != null)
            {
                // measure the wrapped hint without drawing
                string line = ""; int lines = 1;
                foreach (var word in hint.Split(' '))
                {
                    var t = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Ui.TextSize(t, 0.86f).X > tw) { lines++; line = word; } else line = t;
                }
                hintH = Ui.S(2) + lines * SLHs(0.86f) + (lines - 1) * Ui.S(2);
            }
            float labelY = (sz.Y - SLH) / 2; // label centred on the switch, the hint below it
            float inner = Math.Max(sz.Y, labelY + SLH + hintH);
            float rowH = inner + Ui.S(10);
            if (Ui.Hit(id, new Vector2(x, y), new Vector2(w, rowH), out float h)) { set(!on); configuration.Save(); }
            if (h > 0.01f) dl.AddRectFilled(new Vector2(x - Ui.S(6), y), new Vector2(x + w + Ui.S(6), y + rowH), Theme.Text.With(0.04f * h).U32(), Ui.S(6));
            float top = y + Ui.S(5);
            SSwitch(dl, id, new Vector2(x, top), on, h);
            Ui.Text(dl, new Vector2(tx, top + labelY), label, on ? Theme.Text : Theme.Text2);
            if (hint != null) SWrap(dl, tx, top + labelY + SLH + Ui.S(2), tw, hint, Theme.Text3, 0.86f);
            return y + rowH;
        }

        private static float SPillW(string label, FontAwesomeIcon? icon)
            => (label.Length > 0 ? Ui.TextSize(label, 0.92f).X : 0) + (icon.HasValue ? Ui.IconSize(icon.Value, 0.85f).X + (label.Length > 0 ? Ui.S(6) : 0) : 0) + Ui.S(22);

        private static bool SPill(string id, string label, FontAwesomeIcon? icon, Vector2 pos, Vector2 size, Vector4 color, bool on, bool enabled = true, string? tip = null)
        {
            float h = 0;
            bool clicked = false;
            if (enabled) { clicked = Ui.Hit(id, pos, size, out h); if (tip != null) Ui.Tooltip(tip); }
            else if (tip != null) { ImGui.SetCursorScreenPos(pos); ImGui.Dummy(size); Ui.Tooltip(tip); }
            var dl = Ui.Dl;
            float a = enabled ? 1f : 0.45f;
            dl.AddRectFilled(pos, pos + size, (on ? color.With((0.24f + 0.12f * h) * a) : Theme.Night2.With((0.6f + 0.3f * h) * a)).U32(), Ui.S(6));
            dl.AddRect(pos, pos + size, color.With((on ? 0.7f : 0.22f + 0.35f * h) * a).U32(), Ui.S(6), 0, Ui.S(1));
            var tc = (on ? color : UIHelper.LerpColor(Theme.Text2, Theme.Text, h)).With(a);
            float iw = icon.HasValue ? Ui.IconSize(icon.Value, 0.85f).X : 0;
            float lw = label.Length > 0 ? Ui.TextSize(label, 0.92f).X : 0;
            float tw = iw + lw + (iw > 0 && lw > 0 ? Ui.S(6) : 0);
            var start = pos + new Vector2((size.X - tw) / 2, 0);
            if (icon.HasValue) Ui.Icon(dl, new Vector2(start.X, pos.Y + (size.Y - Ui.IconSize(icon.Value, 0.85f).Y) / 2), icon.Value, tc, 0.85f);
            if (lw > 0) Ui.Text(dl, new Vector2(start.X + tw - lw, pos.Y + (size.Y - SLHs(0.92f)) / 2), label, tc, 0.92f);
            return clicked && enabled;
        }

        /// <summary>Segmented choice; shrinks to equal widths (short labels) when it does not fit. Returns the picked value or null.</summary>
        private static string? SSeg(string id, float x, float y, float w, (string V, string Label)[] opts, string cur, string[]? shortLabels = null)
        {
            string? picked = null;
            float gap = Ui.S(4);
            bool fits = opts.Sum(o => SPillW(o.Label, null)) + gap * (opts.Length - 1) <= w;
            float eq = (w - gap * (opts.Length - 1)) / opts.Length;
            float cx = x;
            for (int i = 0; i < opts.Length; i++)
            {
                var (v, label) = opts[i];
                float bw = fits ? SPillW(label, null) : eq;
                string l = fits || shortLabels == null ? label : shortLabels[i];
                if (SPill($"{id}_{v}", l, null, new Vector2(cx, y), new Vector2(bw, SBtnH), Ui.Accent, v == cur, tip: fits ? null : label) && v != cur) picked = v;
                cx += bw + gap;
            }
            return picked;
        }

        /// <summary>Two cards side by side when there is room, else stacked.</summary>
        private static float STwo(float x, float y, float w, Func<float, float, float, float> left, Func<float, float, float, float> right)
        {
            float gap = Ui.S(12);
            if (w < Ui.S(640)) { y = left(x, y, w) + gap; return right(x, y, w); }
            float cw = (w - gap) / 2;
            return Math.Max(left(x, y, cw), right(x + cw + gap, y, cw));
        }

        // ---- the screen -----------------------------------------------------------------------------------------------
        /// <summary>The Settings app: look, dock, behaviour, per-app switches, account and data.</summary>
        private void DrawSettings()
        {
            var o = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float y = o.Y, gap = Ui.S(12);

            // tabs
            bool icons = SettingsTabs.Sum(t => SPillW(t.Label, t.Icon) + Ui.S(4)) <= w;
            float tx = o.X, eq = (w - Ui.S(4) * (SettingsTabs.Length - 1)) / SettingsTabs.Length;
            foreach (var t in SettingsTabs)
            {
                float bw = icons ? SPillW(t.Label, t.Icon) : eq;
                string label = icons || Ui.TextSize(t.Label, 0.92f).X + Ui.S(12) <= bw ? t.Label : t.Label.Split(' ')[0];
                if (SPill("set_tab_" + t.V, label, icons ? t.Icon : null, new Vector2(tx, y), new Vector2(bw, SBtnH), Ui.Accent, _setTab == t.V) && _setTab != t.V)
                { _setTab = t.V; _setConfirm = ""; }
                tx += bw + Ui.S(4);
            }
            y += SBtnH + gap;

            y = _setTab switch
            {
                "dock" => DrawDockSettings(o.X, y, w),
                "behaviour" => DrawBehaviourSettings(o.X, y, w),
                "apps" => DrawAppSettings(o.X, y, w),
                "account" => DrawAccountSettings(o.X, y, w),
                _ => DrawLookSettings(o.X, y, w),
            };
            ImGui.SetCursorScreenPos(new Vector2(o.X, y));
            ImGui.Dummy(new Vector2(Ui.S(1), Ui.S(6)));
        }

        // ---- Look -------------------------------------------------------------------------------------------------------
        private float DrawLookSettings(float x, float y, float w)
        {
            float gap = Ui.S(12);
            y = SCard("Wallpaper", "Shared with your other devices.", x, y, w, (cx, cy, cw) =>
            {
                var dl = Ui.Dl;
                float g = Ui.S(12);
                int n = Wallpapers.Length;
                float tw = Math.Min(Ui.S(200), (cw - g * (n - 1)) / n), th = tw * 0.6f;
                for (int i = 0; i < n; i++)
                {
                    var (id, label) = Wallpapers[i];
                    var p = new Vector2(cx + i * (tw + g), cy);
                    bool sel = configuration.TabletWallpaper == id;
                    bool click = Ui.Hit("wp_" + id, p, new Vector2(tw, th + SLH + Ui.S(8)), out float hv);
                    var saved = configuration.TabletWallpaper;
                    configuration.TabletWallpaper = id;
                    ImGui.PushClipRect(p, p + new Vector2(tw, th), true); // ImGui's clip, never the draw list's (the constellation pops it)
                    DrawWallpaper(dl, p, p + new Vector2(tw, th), preview: true);
                    ImGui.PopClipRect();
                    configuration.TabletWallpaper = saved;
                    dl.AddRect(p, p + new Vector2(tw, th), (sel ? Ui.Accent : Theme.CrystalLine.With(0.22f + 0.4f * hv)).U32(), Ui.S(6), 0, Ui.S(sel ? 2 : 1));
                    if (sel) Ui.Icon(dl, p + new Vector2(tw - Ui.S(8) - Ui.IconSize(FontAwesomeIcon.CheckCircle, 0.9f).X, Ui.S(6)), FontAwesomeIcon.CheckCircle, Ui.Accent, 0.9f);
                    Ui.Text(dl, p + new Vector2(0, th + Ui.S(6)), label, sel ? Ui.Accent : UIHelper.LerpColor(Theme.Text2, Theme.Text, hv));
                    if (click && !sel) { configuration.TabletWallpaper = id; SaveTablet(); }
                }
                cy += th + SLH + Ui.S(16);
                if (configuration.TabletWallpaper == "constellation")
                {
                    cy = SToggle("set_bg", "Still constellation", "No animation; easier on older PCs.", configuration.HideBackgroundAnimation, cx, cy, cw, v => configuration.HideBackgroundAnimation = v);
                    cy = SToggle("set_scan", "Hide the scanline", null, configuration.HideScanline, cx, cy, cw, v => configuration.HideScanline = v);
                }
                return cy;
            }) + gap;

            y = STwo(x, y, w,
                (lx, ly, lw) => SCard("Tablet size", "On this PC only. Larger sizes read better on 4K screens.", lx, ly, lw, (cx, cy, cw) =>
                {
                    var opts = SizeNames.Select((n, i) => (i.ToString(), n)).ToArray();
                    if (SSeg("set_size", cx, cy, cw, opts, Math.Clamp(configuration.TabletSize, 0, 4).ToString(), SizeShort) is { } v)
                    { configuration.TabletSize = int.Parse(v); configuration.Save(); }
                    return cy + SBtnH;
                }),
                (rx, ry, rw) => SCard("Accent", "Buttons, the dock and highlights. Shared with your other devices.", rx, ry, rw, (cx, cy, cw) =>
                {
                    var dl = Ui.Dl;
                    float g = Ui.S(8), bw = Math.Min(Ui.S(150), (cw - g * 2) / 3), bh = SBtnH + Ui.S(4);
                    for (int i = 0; i < Accents.Length; i++)
                    {
                        var (id, label, color) = Accents[i];
                        var p = new Vector2(cx + i * (bw + g), cy);
                        bool sel = configuration.TabletAccent == id;
                        if (Ui.Hit("acc_" + id, p, new Vector2(bw, bh), out float hv) && !sel) { configuration.TabletAccent = id; SaveTablet(); }
                        Ui.Panel(dl, p, p + new Vector2(bw, bh), color.With(sel ? 0.22f : 0.08f + 0.1f * hv), color.With(sel ? 0.9f : 0.35f + 0.2f * hv), 6);
                        Ui.Dot(dl, p + new Vector2(Ui.S(16), bh / 2), 5, color);
                        Ui.Text(dl, p + new Vector2(Ui.S(30), (bh - SLH) / 2), label, sel ? Theme.Text : Theme.Text2);
                    }
                    return cy + bh;
                })) + gap;

            if (configuration.IsVerified)
            {
                y = SCard("Website theme", "Dark or light for the Aetherhold website on every device. The tablet itself stays dark.", x, y, w, (cx, cy, cw) =>
                {
                    string cur = sender.GetState<string>("prefs.theme") == "light" ? "light" : "dark";
                    if (SSeg("set_theme", cx, cy, cw, new[] { ("dark", "Dark"), ("light", "Light") }, cur) is { } v) sender.SetState("prefs.theme", v);
                    return cy + SBtnH;
                });
            }
            return y;
        }

        // ---- Dock -------------------------------------------------------------------------------------------------------
        /// <summary>Dock: which apps sit in it and in what order (empty list = all, in the default order).</summary>
        private float DrawDockSettings(float x, float y, float w)
        {
            return SCard("Dock", "Turn apps on or off and set their order (top = left). Settings always sits at the right end. Shared with your other devices.", x, y, w, (cx, cy, cw) =>
            {
                var dock = configuration.TabletDock.Count == 0 ? _apps.Select(a => a.Name).ToList() : configuration.TabletDock.ToList();
                var rows = dock.Concat(_apps.Select(a => a.Name).Where(n => !dock.Contains(n))).ToList();
                var dl = Ui.Dl;
                var sz = SSwitchSize;
                float rowH = Math.Max(sz.Y, SLH) + Ui.S(14), bs = Math.Max(Ui.S(26), SLH + Ui.S(8));
                List<string>? changed = null;
                for (int i = 0; i < rows.Count; i++)
                {
                    var name = rows[i];
                    var app = AppByName(name);
                    if (app == null) continue;
                    bool on = dock.Contains(name);
                    int di = dock.IndexOf(name);
                    var p = new Vector2(cx, cy);
                    float hitW = cw - (bs * 2 + Ui.S(12));
                    if (i % 2 == 0) dl.AddRectFilled(p - new Vector2(Ui.S(6), 0), p + new Vector2(cw + Ui.S(6), rowH), Theme.Night0.With(0.25f).U32(), Ui.S(6));
                    if (Ui.Hit("dock_" + name, p, new Vector2(hitW, rowH), out float hv))
                    {
                        changed = dock.ToList();
                        if (on) changed.Remove(name); else changed.Add(name);
                    }
                    SSwitch(dl, "dock_" + name, p + new Vector2(0, (rowH - sz.Y) / 2), on, hv);
                    float ix = cx + sz.X + Ui.S(14);
                    Ui.Icon(dl, new Vector2(ix, cy + (rowH - Ui.IconSize(IconOf(app), 0.95f).Y) / 2), IconOf(app), on ? Ui.Accent : Theme.Text3, 0.95f);
                    Ui.Text(dl, new Vector2(ix + Ui.S(30), cy + (rowH - SLH) / 2), name, on ? Theme.Text : Theme.Text3);
                    if (on)
                    {
                        string pos = $"{di + 1}";
                        Ui.Text(dl, new Vector2(cx + hitW - Ui.TextSize(pos, 0.86f).X - Ui.S(8), cy + (rowH - SLHs(0.86f)) / 2), pos, Theme.Text3, 0.86f);
                        float bx = cx + cw - bs * 2 - Ui.S(4), by = cy + (rowH - bs) / 2;
                        if (di > 0 && Ui.IconButton("dock_up_" + name, FontAwesomeIcon.ChevronUp, new Vector2(bx, by), bs, "Move left"))
                        { changed = dock.ToList(); changed.RemoveAt(di); changed.Insert(di - 1, name); }
                        if (di < dock.Count - 1 && Ui.IconButton("dock_dn_" + name, FontAwesomeIcon.ChevronDown, new Vector2(bx + bs + Ui.S(4), by), bs, "Move right"))
                        { changed = dock.ToList(); changed.RemoveAt(di); changed.Insert(di + 1, name); }
                    }
                    cy += rowH;
                }
                cy += Ui.S(10);
                bool isDefault = configuration.TabletDock.Count == 0;
                if (SPill("dock_reset", "Show all, default order", FontAwesomeIcon.Undo, new Vector2(cx, cy), new Vector2(SPillW("Show all, default order", FontAwesomeIcon.Undo), SBtnH), Ui.Accent, false, !isDefault,
                        isDefault ? "Already all apps in the default order" : null))
                    changed = new();
                cy += SBtnH;
                if (changed != null)
                {
                    // all apps in the default order is stored as empty, so new apps show up by themselves
                    configuration.TabletDock = changed.SequenceEqual(_apps.Select(a => a.Name)) ? new() : changed;
                    SaveTablet();
                }
                return cy;
            });
        }

        // ---- Behaviour --------------------------------------------------------------------------------------------------
        private float DrawBehaviourSettings(float x, float y, float w)
        {
            float gap = Ui.S(12), ind = Ui.S(28);
            y = STwo(x, y, w,
                (lx, ly, lw) => SCard("Opening", "On this PC only.", lx, ly, lw, (cx, cy, cw) =>
                {
                    cy = SToggle("set_startup", "Open the tablet when you log in", null, configuration.OpenOnStartup, cx, cy, cw, v => configuration.OpenOnStartup = v);
                    if (configuration.OpenOnStartup)
                        cy = SToggle("set_minimized", "Start asleep", "Opens as the small view instead of the full tablet.", configuration.StartMinimized, cx + ind, cy, cw - ind, v => configuration.StartMinimized = v);
                    cy = SToggle("set_scroll", "Hide scrollbars", "Scroll with the mouse wheel as usual.", configuration.HideScrollbars, cx, cy, cw, v => configuration.HideScrollbars = v);
                    return cy;
                }),
                (rx, ry, rw) => SCard("Sleep view", "The small view you get when you minimise the tablet.", rx, ry, rw, (cx, cy, cw) =>
                {
                    cy = SToggle("set_overlay", "Show alerts in the small view", null, configuration.ShowMinimizedOverlay, cx, cy, cw, v => configuration.ShowMinimizedOverlay = v);
                    if (configuration.ShowMinimizedOverlay)
                    {
                        cy = SToggle("set_ov_gath", "Favourite nodes that are up", "Also the next stop of a running gathering route.", configuration.ShowMinimizedGatheringOverlay, cx + ind, cy, cw - ind, v => configuration.ShowMinimizedGatheringOverlay = v);
                        cy = SToggle("set_ov_ret", "Retainer ventures", null, configuration.ShowMinimizedRetainerOverlay, cx + ind, cy, cw - ind, v => configuration.ShowMinimizedRetainerOverlay = v);
                    }
                    return cy;
                }));
            return y;
        }

        // ---- Apps -------------------------------------------------------------------------------------------------------
        private float DrawAppSettings(float x, float y, float w)
        {
            float gap = Ui.S(12), ind = Ui.S(28);
            return STwo(x, y, w,
                (lx, ly, lw) => SCard("Gathering & Fishing", null, lx, ly, lw, (cx, cy, cw) =>
                {
                    cy = SToggle("set_g_notif", "Notify me when a favourite node spawns", null, configuration.EnableNodeNotifications, cx, cy, cw, v => configuration.EnableNodeNotifications = v);
                    if (configuration.EnableNodeNotifications)
                    {
                        var dl = Ui.Dl;
                        Ui.Text(dl, new Vector2(cx + ind, cy + Ui.S(2)), "Remind me", Theme.Text2, 0.92f);
                        cy += SLHs(0.92f) + Ui.S(8);
                        var opts = new[] { ("0", "On time"), ("1", "1 min early"), ("2", "2 min"), ("3", "3 min") };
                        if (SSeg("set_g_early", cx + ind, cy, cw - ind, opts, Math.Clamp(configuration.EarlyNodeNotificationMinutes, 0, 3).ToString(), new[] { "0", "1", "2", "3" }) is { } v)
                        { configuration.EarlyNodeNotificationMinutes = int.Parse(v); configuration.Save(); }
                        cy += SBtnH + Ui.S(10);
                    }
                    cy = SToggle("set_g_audio", "Play a sound for alarms", "Bells set here or on the website ring in game when the node or fish window opens.", configuration.EnableNodeAudio, cx, cy, cw, v => configuration.EnableNodeAudio = v);
                    cy += Ui.S(4);
                    if (SPill("set_g_test", "Test sound", FontAwesomeIcon.VolumeUp, new Vector2(cx, cy), new Vector2(SPillW("Test sound", FontAwesomeIcon.VolumeUp), SBtnH), Ui.Accent, false))
                        PlayTestSound();
                    cy += SBtnH + Ui.S(8);
                    cy = SToggle("set_g_prices", "Show market prices in the gathering log", null, configuration.EnableGatheringPrices, cx, cy, cw, v => configuration.EnableGatheringPrices = v);
                    return cy;
                }),
                (rx, ry, rw) =>
                {
                    ry = SCard("Market", null, rx, ry, rw, (cx, cy, cw) =>
                        SToggle("set_m_hover", "Open Market when you hover an item", "Hold the mouse over an item in game for a second to see its prices.", configuration.EnableHoverItemFetching, cx, cy, cw, v => configuration.EnableHoverItemFetching = v)) + gap;
                    return SCard("Routines", "Retainer ventures.", rx, ry, rw, (cx, cy, cw) =>
                    {
                        cy = SToggle("set_r_audio", "Play a sound when all ventures are back", null, configuration.RetainerAudioEnabled, cx, cy, cw, v => configuration.RetainerAudioEnabled = v);
                        if (configuration.RetainerAudioEnabled)
                            cy = SToggle("set_r_once", "Only once", "Off: every minute until you visit a summoning bell.", configuration.RetainerAudioFireOnce, cx + ind, cy, cw - ind, v => configuration.RetainerAudioFireOnce = v);
                        return cy;
                    });
                });
        }

        private static unsafe void PlayTestSound()
        {
            try { FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.PlayChatSoundEffect(1); } catch { }
        }

        // ---- Account & data ---------------------------------------------------------------------------------------------
        private float DrawAccountSettings(float x, float y, float w)
        {
            float gap = Ui.S(12);
            y = STwo(x, y, w,
                (lx, ly, lw) => SCard("Aetherhold account", null, lx, ly, lw, (cx, cy, cw) =>
                {
                    var dl = Ui.Dl;
                    bool ok = configuration.IsVerified;
                    string role = sender.CurrentUserRole switch { "admin" => " · admin", "friend" => " · friend", _ => "" };
                    Ui.Dot(dl, new Vector2(cx + Ui.S(5), cy + SLH / 2), 4.5f, ok ? Theme.Live : Theme.Warn, ok);
                    Ui.Text(dl, new Vector2(cx + Ui.S(18), cy), ok ? $"Connected{role}" : "Not connected", ok ? Theme.Text : Theme.Warn);
                    cy += SLH + Ui.S(6);
                    Ui.Text(dl, new Vector2(cx, cy), $"Token {TokenHint(configuration.XivHubId)}  ·  {HubClient.BaseUrl.Replace("https://", "")}", Theme.Text3, 0.88f);
                    cy += SLHs(0.88f) + Ui.S(12);
                    if (_setConfirm != "signout")
                    {
                        const string l = "Enter a new token / disconnect";
                        if (SPill("set_unlink", l, FontAwesomeIcon.Unlink, new Vector2(cx, cy), new Vector2(Math.Min(cw, SPillW(l, FontAwesomeIcon.Unlink)), SBtnH), Theme.Text2, false)) _setConfirm = "signout";
                    }
                    else cy = ConfirmRow(cx, cy, cw, "Disconnect this PC? Sync pauses until you enter a token again.", "Disconnect", () => SignOut()) - SBtnH;
                    return cy + SBtnH;
                }),
                (rx, ry, rw) => SCard("LiveSync", null, rx, ry, rw, (cx, cy, cw) =>
                {
                    var dl = Ui.Dl;
                    cy = SToggle("set_sync", "Send my character to Aetherhold live", null, configuration.IsSyncEnabled, cx, cy, cw, v => configuration.IsSyncEnabled = v);
                    cy = SWrap(dl, cx, cy + Ui.S(4), cw, "Gear, stats, bags, retainers and collections go to your Aetherhold page while you play. Cart, routines, venture timers and crafter stats are shared with the website both ways.", Theme.Text2, 0.88f) + Ui.S(10);
                    string last = sender.LastSyncTime == DateTime.MinValue ? "never" : sender.LastSyncTime.ToString("HH:mm:ss");
                    Ui.Text(dl, new Vector2(cx, cy), $"Syncs {sender.TotalSyncs}  ·  failed {sender.FailedSyncs}  ·  last {last}", Theme.Text2, 0.88f);
                    cy += SLHs(0.88f) + Ui.S(4);
                    bool bad = sender.FailedSyncs > 0 && sender.LastSyncStatus != "Success";
                    Ui.Text(dl, new Vector2(cx, cy), $"Status: {sender.LastSyncStatus}", bad ? Theme.Warn : Theme.Text3, 0.88f);
                    return cy + SLHs(0.88f);
                })) + gap;

            return SCard("Data & resets", "These change your saved data on every device at once.", x, y, w, (cx, cy, cw) =>
            {
                var routines = App<RoutinesApp>();
                var gathering = App<GatheringApp>();
                int alarms = gathering?.AlarmCount ?? 0;
                var player = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
                bool signedIn = configuration.IsVerified;
                cy = ActionRow("daily", cx, cy, cw, "Reset daily routines", "Untick every daily routine.", "Reset", FontAwesomeIcon.Redo, false, signedIn && routines != null,
                    () => routines?.ResetChecklist("daily"));
                cy = ActionRow("weekly", cx, cy, cw, "Reset weekly routines", "Untick every weekly routine.", "Reset", FontAwesomeIcon.Redo, false, signedIn && routines != null,
                    () => routines?.ResetChecklist("weekly"));
                cy = ActionRow("alarms", cx, cy, cw, $"Clear gathering alarms ({alarms})", "Turn off every gathering alarm.", "Clear", FontAwesomeIcon.BellSlash, false, signedIn && alarms > 0,
                    () => gathering?.ClearAlarms());
                cy = ActionRow("wipe", cx, cy, cw, "Wipe calendar", "Deletes all your custom events on every device. This cannot be undone.", "Wipe", FontAwesomeIcon.Trash, true, signedIn && player != null,
                    () =>
                    {
                        if (_objectTable[0] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc) return;
                        var (n, wd) = (pc.Name.ToString(), pc.HomeWorld.Value.Name.ToString());
                        Task.Run(() => sender.WipeCalendarAsync(n, wd));
                    });
                return cy - Ui.S(6);
            });
        }

        /// <summary>One data action: title and hint left, button right; the button turns into Yes / Cancel.</summary>
        private float ActionRow(string id, float x, float y, float w, string title, string hint, string button, FontAwesomeIcon icon, bool danger, bool enabled, Action run)
        {
            var dl = Ui.Dl;
            var col = danger ? Theme.Bad : Ui.Accent;
            bool confirm = _setConfirm == id;
            float bw = confirm ? SPillW("Yes, " + button.ToLowerInvariant(), null) + SPillW("Cancel", null) + Ui.S(6) : SPillW(button, icon);
            float tw = w - bw - Ui.S(16);
            float ty = y + Ui.S(6);
            Ui.Text(dl, new Vector2(x, ty), title, enabled ? Theme.Text : Theme.Text3);
            float hy = SWrap(dl, x, ty + SLH + Ui.S(2), tw, confirm ? "Are you sure? " + hint : hint, confirm ? (danger ? Theme.Bad : Theme.Gold) : Theme.Text3, 0.86f);
            float rowH = Math.Max(hy - y, SBtnH) + Ui.S(12);
            float by = y + (rowH - SBtnH) / 2, bx = x + w - bw;
            if (!confirm)
            {
                if (SPill("set_act_" + id, button, icon, new Vector2(bx, by), new Vector2(bw, SBtnH), col, false, enabled)) _setConfirm = id;
            }
            else
            {
                float yw = SPillW("Yes, " + button.ToLowerInvariant(), null);
                if (SPill("set_yes_" + id, "Yes, " + button.ToLowerInvariant(), null, new Vector2(bx, by), new Vector2(yw, SBtnH), col, true, enabled)) { run(); _setConfirm = ""; }
                if (SPill("set_no_" + id, "Cancel", null, new Vector2(bx + yw + Ui.S(6), by), new Vector2(SPillW("Cancel", null), SBtnH), Theme.Text2, false)) _setConfirm = "";
            }
            dl.AddLine(new Vector2(x, y + rowH), new Vector2(x + w, y + rowH), Theme.CrystalLine.With(0.14f).U32(), Ui.S(1));
            return y + rowH + Ui.S(4);
        }

        /// <summary>Inline confirm under a button: question, then Yes / Cancel. Returns the y below the buttons.</summary>
        private float ConfirmRow(float x, float y, float w, string question, string yes, Action run)
        {
            var dl = Ui.Dl;
            y = SWrap(dl, x, y, w, question, Theme.Gold, 0.88f) + Ui.S(8);
            float yw = SPillW(yes, null);
            if (SPill("set_cf_yes", yes, null, new Vector2(x, y), new Vector2(yw, SBtnH), Theme.Bad, true)) { run(); _setConfirm = ""; }
            if (SPill("set_cf_no", "Cancel", null, new Vector2(x + yw + Ui.S(6), y), new Vector2(SPillW("Cancel", null), SBtnH), Theme.Text2, false)) _setConfirm = "";
            return y + SBtnH;
        }

        /// <summary>Never show the whole token on screen (streams, screenshots).</summary>
        private static string TokenHint(string token) => string.IsNullOrEmpty(token) || token.Length < 8 ? "set" : $"{token[..3]}…{token[^4..]}";
    }
}
