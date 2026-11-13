using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // The screen, laid out like the website's events page: actions + feed on the left, month calendar on the right
    // (stacked on narrow tablets), and the event / create / venues dialogs.
    public partial class EventsApp
    {
        private sealed class Form
        {
            public string Title = "", Date = "", Location = "", Url = "", Image = "", Description = "";
            public bool Weekly;
        }

        private string _importUrl = "";
        private DateTime _month = new(DateTime.Now.Year, DateTime.Now.Month, 1);
        private DateTime? _picked;
        private CalEvent? _selected;
        private bool _confirmDelete;
        private Form? _form;
        private bool _focusForm;
        private bool _showSubs;
        private bool _feedAll;
        private int _openedFrame;
        private float _lockScroll;
        private bool _wasModal;
        private float _descScroll;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] WeekDays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float LHs(float scale) => Ui.TextSize("Ag", scale).Y;
        private static float CapsH => Ui.TextSize("A", 0.78f).Y;
        private static float BtnH => LH + S(12);
        private static float FieldH => ImGui.GetFontSize() + S(7) * 2;
        private static float RowH => Math.Max(S(44), LH + LHs(0.8f) + S(14));

        private static Vector4 KindColor(string k) => k == KLodestone ? Theme.Gold : k == KPartake ? Theme.Crystal : Theme.Violet;
        private static string KindLabel(string k) => k == KLodestone ? "Lodestone" : k == KPartake ? "Partake" : "Custom";
        private static string Day(DateTime d) => d.ToString("ddd, MMM d", Inv);
        private static string Time(DateTime d) => d.ToString("HH:mm", Inv);

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || Ui.TextSize(text, scale).X <= width) return text ?? "";
            if (width <= 0) return "";
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }

        private static List<string> Wrap(string text, float width, float scale = 1f)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = "";
                foreach (var word in para.Split(' '))
                {
                    var w = word;
                    var next = line.Length == 0 ? w : line + " " + w;
                    if (Ui.TextSize(next, scale).X <= width) { line = next; continue; }
                    if (line.Length > 0) lines.Add(line);
                    // a word wider than the box is broken up
                    while (w.Length > 1 && Ui.TextSize(w, scale).X > width)
                    {
                        int n = w.Length - 1;
                        while (n > 1 && Ui.TextSize(w[..n], scale).X > width) n--;
                        lines.Add(w[..n]);
                        w = w[n..];
                    }
                    line = w;
                }
                lines.Add(line);
            }
            return lines;
        }

        private static float BtnW(string label, FontAwesomeIcon? icon) => Ui.TextSize(label).X + (icon.HasValue ? Ui.IconSize(icon.Value, 0.9f).X + S(6) : 0) + S(24);

        private static void Gem(ImDrawListPtr dl, Vector2 c, float r, Vector4 col)
            => dl.AddQuadFilled(c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0), col.U32());

        private static float Title(ImDrawListPtr dl, float x, float y, string text)
        {
            Gem(dl, new Vector2(x + S(5), y + CapsH / 2), S(4), Theme.Gold);
            Ui.Caps(dl, new Vector2(x + S(16), y), text);
            return y + CapsH;
        }

        /// <summary>Draws an image cropped to fill a-b (website: background-size cover). False while it loads or without one.</summary>
        private bool Cover(ImDrawListPtr dl, string url, Vector2 a, Vector2 b, float alpha = 1f)
        {
            var wrap = Image(url)?.GetWrapOrDefault();
            if (wrap == null || wrap.Width == 0 || wrap.Height == 0) return false;
            float ia = (float)wrap.Width / wrap.Height, ta = (b.X - a.X) / Math.Max(1f, b.Y - a.Y);
            Vector2 uv0 = Vector2.Zero, uv1 = Vector2.One;
            if (ia > ta) { float m = (1 - ta / ia) / 2; uv0.X = m; uv1.X = 1 - m; }
            else if (ia < ta) { float m = (1 - ia / ta) / 2; uv0.Y = m; uv1.Y = 1 - m; }
            dl.AddImage(wrap.Handle, a, b, uv0, uv1, new Vector4(1, 1, 1, alpha).U32());
            return true;
        }

        private bool Modal => _selected != null || _form != null || _showSubs;

        private void OpenEvent(CalEvent e) { _selected = e; _confirmDelete = false; _descScroll = 0; _openedFrame = ImGui.GetFrameCount(); }

        // =============================================================================================================
        public void Draw()
        {
            EnsureFresh(true);
            if (_msg.Length > 0 && !_msgBad && (DateTime.Now - _msgAt).TotalSeconds > 5) _msg = "";

            bool modal = Modal;
            if (modal && !_wasModal) _lockScroll = ImGui.GetScrollY();
            if (modal) ImGui.SetScrollY(_lockScroll); // the page stays put under a dialog
            _wasModal = modal;

            var o = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X, gap = S(12), end;
            if (modal) ImGui.BeginDisabled();
            if (w >= S(820))
            {
                float sw = S(300);
                float ys = DrawActions(o.X, o.Y, sw) + gap;
                ys = DrawFeed(o.X, ys, sw, false);
                float yc = DrawCalendar(o.X + sw + gap, o.Y, w - sw - gap);
                end = Math.Max(ys, yc);
            }
            else if (w >= S(560))
            {
                float cw = (w - gap) / 2;
                float ya = DrawActions(o.X, o.Y, cw);
                float yf = DrawFeed(o.X + cw + gap, o.Y, cw, true);
                end = DrawCalendar(o.X, Math.Max(ya, yf) + gap, w);
            }
            else
            {
                float y = DrawActions(o.X, o.Y, w) + gap;
                y = DrawFeed(o.X, y, w, true) + gap;
                end = DrawCalendar(o.X, y, w);
            }
            ImGui.SetCursorScreenPos(new Vector2(o.X, end));
            ImGui.Dummy(new Vector2(S(1), S(4)));
            if (modal)
            {
                ImGui.EndDisabled();
                if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { _selected = null; _form = null; _showSubs = false; }
                else if (_selected != null) DrawDetails();
                else if (_form != null) DrawForm();
                else if (_showSubs) DrawSubs();
            }
        }

        // ---- actions panel -------------------------------------------------------------------------------------------
        private float DrawActions(float x, float y, float w)
        {
            float pad = S(14), iw = w - pad * 2, sgap = S(8);
            float bw2 = (iw - sgap) / 2;
            var msgLines = _msg.Length > 0 ? Wrap(_msg, iw, 0.88f) : new List<string>();
            float h = pad + BtnH + S(16) + CapsH + S(8) + FieldH + S(8) + BtnH + (msgLines.Count > 0 ? S(8) + msgLines.Count * LHs(0.88f) : 0) + pad;
            var dl = Ui.Dl;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + pad, cy = y + pad;

            if (Ui.Button("ev_add", "Add custom event", new Vector2(cx, cy), new Vector2(iw, BtnH), Kind.Gold, FontAwesomeIcon.Plus))
            {
                if (!SignedIn) Flash("Sign in to Aetherhold to add events.", true);
                else { _form = new Form { Date = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:00", Inv) }; _focusForm = true; _openedFrame = ImGui.GetFrameCount(); }
            }
            cy += BtnH + S(16);

            Title(dl, cx, cy, "Partake.gg");
            float tw = S(16) + Ui.TextSize("PARTAKE.GG", 0.78f).X + "PARTAKE.GG".Length * S(1.4f) + S(8);
            float hs = CapsH + S(4);
            var hp = new Vector2(cx + tw, cy - S(2));
            ImGui.SetCursorScreenPos(hp);
            ImGui.InvisibleButton("ev_help", new Vector2(hs, hs));
            dl.AddCircle(hp + new Vector2(hs, hs) / 2, hs / 2, Theme.Text3.U32(), 16, S(1));
            Ui.Text(dl, hp + new Vector2((hs - Ui.TextSize("?", 0.75f).X) / 2, (hs - LHs(0.75f)) / 2), "?", Theme.Text2, 0.75f);
            Ui.Tooltip("Paste a Partake venue link (partake.gg/teams/...) to subscribe for automatic updates, or an event link (partake.gg/events/...) to import one event.");
            cy += CapsH + S(8);

            float ib = FieldH;
            ImGui.SetCursorScreenPos(new Vector2(cx, cy));
            PushFrame();
            ImGui.SetNextItemWidth(iw - ib - S(6));
            bool enter = ImGui.InputTextWithHint("##ev_import", "Paste a Partake link...", ref _importUrl, 400, ImGuiInputTextFlags.EnterReturnsTrue);
            PopFrame();
            bool can = _importUrl.Trim().Length > 0 && !_importing;
            if (Ui.IconButton("ev_import_go", _importing ? FontAwesomeIcon.HourglassHalf : FontAwesomeIcon.Download, new Vector2(cx + iw - ib, cy), ib,
                    _importing ? "Importing..." : "Import the event, or subscribe to the venue", can ? Theme.Gold : Theme.Text3) && can) enter = true;
            if (enter && can) Import(_importUrl);
            cy += FieldH + S(8);

            string scan = _scanning ? "Scanning..." : "Scan updates", ven = $"Venues ({_subs.Count})";
            FontAwesomeIcon? si = FontAwesomeIcon.SyncAlt, vi = FontAwesomeIcon.Users;
            if (BtnW(scan, si) > bw2) si = null;
            if (BtnW(ven, vi) > bw2) vi = null;
            if (BtnW(scan, null) > bw2) scan = _scanning ? "..." : "Scan";
            if (Ui.Button("ev_scan", scan, new Vector2(cx, cy), new Vector2(bw2, BtnH), Kind.Ghost, si)) Scan();
            Ui.Tooltip(SignedIn ? "Pull the latest events from all subscribed venues now. Once every 5 minutes; they also update by themselves every hour." : "Sign in to Aetherhold first");
            if (Ui.Button("ev_venues", ven, new Vector2(cx + bw2 + sgap, cy), new Vector2(bw2, BtnH), Kind.Ghost, vi)) { _showSubs = true; _openedFrame = ImGui.GetFrameCount(); }
            Ui.Tooltip("Your subscribed venues");
            cy += BtnH;

            if (msgLines.Count > 0)
            {
                cy += S(8);
                foreach (var l in msgLines) { Ui.Text(dl, new Vector2(cx, cy), l, _msgBad ? Theme.Bad : Theme.Live, 0.88f); cy += LHs(0.88f); }
            }
            return y + h;
        }

        // ---- feed panel: Upcoming / Maintenance / News ---------------------------------------------------------------
        private List<CalEvent> Visible => _all.Where(e => !_hidden.Contains(e.Kind)).ToList();

        private List<CalEvent> Upcoming()
        {
            var start = DateTime.Now.Date;
            var seen = new HashSet<string>();
            var list = new List<CalEvent>();
            foreach (var e in _all)
            {
                if (_hidden.Contains(e.Kind) || e.Kind == KLodestone || e.Local < start || !seen.Add(e.Id)) continue;
                list.Add(e);
                if (list.Count >= 12) break;
            }
            return list;
        }

        private float DrawFeed(float x, float y, float w, bool capped)
        {
            float pad = S(14), iw = w - pad * 2, rgap = S(6);
            var dl = Ui.Dl;

            // rows first, so the panel height is known
            var rows = new List<Func<float, float, bool>>();
            string empty = "";
            if (_loading) empty = "Loading...";
            else if (_side == "upcoming")
            {
                if (!SignedIn) empty = "Sign in to Aetherhold to see your community events.";
                else
                {
                    foreach (var e in Upcoming()) { var ev = e; rows.Add((rx, ry) => Row("up" + ev.Id, rx, ry, iw, ev, true)); }
                    if (rows.Count == 0) empty = "No upcoming community events. Add one, or import from Partake.";
                }
            }
            else if (_side == "maintenance")
            {
                foreach (var m in _maintenance) { var mm = m; rows.Add((rx, ry) => MaintRow(rx, ry, iw, mm)); }
                if (rows.Count == 0) empty = _loadError.Length > 0 ? _loadError : "No maintenance notices.";
            }
            else
            {
                foreach (var e in Enumerable.Reverse(_official)) { var ev = e; rows.Add((rx, ry) => Row("nw" + ev.Id + ev.Utc.Ticks, rx, ry, iw, ev, true)); }
                if (rows.Count == 0) empty = _loadError.Length > 0 ? _loadError : "No Lodestone news.";
            }
            int max = capped && !_feedAll ? 5 : int.MaxValue;
            int shown = Math.Min(rows.Count, max);
            bool more = capped && rows.Count > 5;
            var emptyLines = empty.Length > 0 ? Wrap(empty, iw, 0.9f) : new List<string>();
            float tabH = LH + S(14);
            float h = pad + tabH + S(10) + shown * (RowH + rgap) + emptyLines.Count * LHs(0.9f) + (more ? BtnH : 0) + pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + pad, cy = y + pad;

            // tabs
            var tabs = new[] { ("upcoming", "Upcoming", FontAwesomeIcon.CalendarAlt), ("maintenance", "Maintenance", FontAwesomeIcon.Wrench), ("news", "News", FontAwesomeIcon.Newspaper) };
            float tw = iw / 3;
            bool icons = tabs.All(t => Ui.TextSize(t.Item2, 0.9f).X + Ui.IconSize(t.Item3, 0.8f).X + S(16) <= tw);
            for (int i = 0; i < 3; i++)
            {
                var (key, label, icon) = tabs[i];
                var tp = new Vector2(cx + i * tw, cy);
                bool on = _side == key;
                if (Ui.Hit("ev_tab_" + key, tp, new Vector2(tw, tabH), out float hov)) SetSide(key);
                var col = on ? Theme.Text : UIHelper.LerpColor(Theme.Text3, Theme.Text2, hov);
                string lbl = Fit(label, tw - S(8) - (icons ? Ui.IconSize(icon, 0.8f).X + S(6) : 0), 0.9f);
                float lw = Ui.TextSize(lbl, 0.9f).X + (icons ? Ui.IconSize(icon, 0.8f).X + S(6) : 0);
                float lx = tp.X + (tw - lw) / 2, ly = tp.Y + (tabH - LHs(0.9f)) / 2;
                if (icons) { Ui.Icon(dl, new Vector2(lx, ly + S(2)), icon, on ? Ui.Accent : col, 0.8f); lx += Ui.IconSize(icon, 0.8f).X + S(6); }
                Ui.Text(dl, new Vector2(lx, ly), lbl, col, 0.9f);
                dl.AddRectFilled(new Vector2(tp.X, tp.Y + tabH - S(2)), new Vector2(tp.X + tw, tp.Y + tabH), (on ? Ui.Accent : Theme.CrystalLine.With(0.12f)).U32());
            }
            cy += tabH + S(10);

            for (int i = 0; i < shown; i++) { rows[i](cx, cy); cy += RowH + rgap; }
            foreach (var l in emptyLines) { Ui.Text(dl, new Vector2(cx, cy), l, Theme.Text3, 0.9f); cy += LHs(0.9f); }
            if (more)
            {
                string lbl = _feedAll ? "Show less" : $"Show all ({rows.Count})";
                if (Ui.Button("ev_feed_more", lbl, new Vector2(cx, cy), new Vector2(iw, BtnH - S(4)))) _feedAll = !_feedAll;
            }
            return y + h;
        }

        private bool Row(string id, float x, float y, float w, CalEvent e, bool withDate)
        {
            var p = new Vector2(x, y);
            var size = new Vector2(w, RowH);
            bool clicked = Ui.Hit("ev_row_" + id, p, size, out float hov);
            Ui.Tooltip(e.Title);
            var dl = Ui.Dl;
            var kc = KindColor(e.Kind);
            dl.AddRectFilled(p, p + size, Theme.Night1.With(0.55f + 0.25f * hov).U32(), S(6));
            dl.AddRect(p, p + size, kc.With(0.12f + 0.25f * hov).U32(), S(6), 0, S(1));
            dl.AddRectFilled(p + new Vector2(0, S(6)), p + new Vector2(S(3), RowH - S(6)), kc.U32(), S(2));

            float img = RowH - S(12);
            var ia = p + new Vector2(S(10), S(6));
            if (!Cover(dl, e.Image, ia, ia + new Vector2(img, img)))
            {
                dl.AddRectFilled(ia, ia + new Vector2(img, img), kc.With(0.1f).U32(), S(4));
                Gem(dl, ia + new Vector2(img, img) / 2, S(5), kc.With(0.6f));
            }
            float tx = ia.X + img + S(10), tw = p.X + w - tx - S(8);
            float ty = p.Y + (RowH - LH - LHs(0.8f) - S(2)) / 2;
            Ui.Text(dl, new Vector2(tx, ty), Fit(e.Title, tw), Theme.Text);
            ty += LH + S(2);
            string k = KindLabel(e.Kind);
            Ui.Text(dl, new Vector2(tx, ty), k, kc, 0.8f);
            float mx = tx + Ui.TextSize(k, 0.8f).X + S(6);
            string meta = (withDate ? Day(e.Local) : "") + (e.Kind != KLodestone ? (withDate ? " · " : "") + Time(e.Local) : "");
            float rep = e.Weekly ? Ui.IconSize(FontAwesomeIcon.Redo, 0.7f).X + S(6) : 0;
            meta = Fit(meta, p.X + w - mx - S(8) - rep, 0.8f);
            Ui.Text(dl, new Vector2(mx, ty), meta, Theme.Text3, 0.8f);
            if (e.Weekly) Ui.Icon(dl, new Vector2(mx + Ui.TextSize(meta, 0.8f).X + S(6), ty + S(2)), FontAwesomeIcon.Redo, Theme.Text3, 0.7f);
            if (clicked) OpenEvent(e);
            return clicked;
        }

        private bool MaintRow(float x, float y, float w, Maint m)
        {
            var p = new Vector2(x, y);
            var size = new Vector2(w, RowH);
            bool clicked = Ui.Hit("ev_mt_" + m.Id + m.Title, p, size, out float hov);
            Ui.Tooltip(m.Title + (IsWebLink(m.Url) ? "\nOpens the Lodestone" : ""));
            var dl = Ui.Dl;
            dl.AddRectFilled(p, p + size, Theme.Night1.With(0.55f + 0.25f * hov).U32(), S(6));
            dl.AddRect(p, p + size, Theme.Warn.With(0.1f + 0.25f * hov).U32(), S(6), 0, S(1));
            float tx = p.X + S(12), tw = w - S(24);
            float ty = p.Y + (RowH - LH - LHs(0.8f) - S(2)) / 2;
            Ui.Text(dl, new Vector2(tx, ty), Fit(m.Title, tw - S(18)), Theme.Text);
            Ui.Icon(dl, new Vector2(p.X + w - S(22), ty + S(2)), FontAwesomeIcon.ExternalLinkAlt, Theme.Text3.With(0.6f + 0.4f * hov), 0.75f);
            ty += LH + S(2);
            float mx = tx;
            if (m.Tag.Length > 0) { Ui.Text(dl, new Vector2(mx, ty), m.Tag, Theme.Warn, 0.8f); mx += Ui.TextSize(m.Tag, 0.8f).X + S(8); }
            if (m.Utc != default) Ui.Text(dl, new Vector2(mx, ty), Day(m.Utc.ToLocalTime()), Theme.Text3, 0.8f);
            if (clicked) OpenLink(m.Url);
            return clicked;
        }

        // ---- calendar ------------------------------------------------------------------------------------------------
        private float DrawCalendar(float x, float y, float w)
        {
            float pad = S(14), iw = w - pad * 2, cgap = S(4);
            var dl = Ui.Dl;
            var vis = Visible;
            int year = _month.Year, month = _month.Month;
            int days = DateTime.DaysInMonth(year, month);
            int first = ((int)_month.DayOfWeek + 6) % 7; // Monday first
            int weeks = (int)Math.Ceiling((first + days) / 7.0);
            var byDay = new Dictionary<int, List<CalEvent>>();
            foreach (var e in vis)
            {
                var l = e.Local;
                if (l.Year != year || l.Month != month) continue;
                if (!byDay.TryGetValue(l.Day, out var list)) byDay[l.Day] = list = new();
                list.Add(e);
            }

            // header: month, kind filters, navigation
            string title = _month.ToString("MMMM yyyy", Inv);
            float titleS = 1.2f, titleW = Ui.IconSize(FontAwesomeIcon.CalendarAlt, 1f).X + S(8) + Ui.TextSize(title, titleS).X;
            var counts = Kinds.ToDictionary(k => k, k => _all.Where(e => e.Kind == k).Select(e => e.Id).Distinct().Count());
            float chipH = LHs(0.85f) + S(10);
            var chipW = Kinds.ToDictionary(k => k, k => S(26) + Ui.TextSize(KindLabel(k), 0.85f).X + S(6) + Ui.TextSize(counts[k].ToString(), 0.85f).X + S(10));
            float filtersW = chipW.Values.Sum() + S(6) * 2;
            float navW = BtnW("Today", null) + BtnH * 2 + S(6) * 2;
            bool filterLine = titleW + filtersW + navW + S(32) > iw;
            float headH = Math.Max(BtnH, LHs(titleS)) + (filterLine ? S(8) + chipH : 0);

            float wdH = LHs(0.8f) + S(6);
            float cellW = (iw - cgap * 6) / 7;
            bool compact = cellW < S(78);
            float thumbH = LHs(0.78f) + S(6);
            float dayH = LHs(0.85f) + S(8);
            float cellH = compact ? Math.Max(S(44), dayH + S(14)) : dayH + 3 * (thumbH + S(3)) + LHs(0.75f) + S(8);
            var picked = _picked.HasValue && _picked.Value.Year == year && _picked.Value.Month == month ? _picked : null;
            var pickedList = picked.HasValue ? vis.Where(e => e.Local.Date == picked.Value.Date).ToList() : new();
            float dayViewH = picked.HasValue ? S(16) + CapsH + S(10) + Math.Max(1, pickedList.Count) * (RowH + S(6)) : 0;
            float h = pad + headH + S(12) + wdH + weeks * (cellH + cgap) + dayViewH + pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + pad, cy = y + pad;

            float lineH = Math.Max(BtnH, LHs(titleS));
            Ui.Icon(dl, new Vector2(cx, cy + (lineH - Ui.IconSize(FontAwesomeIcon.CalendarAlt).Y) / 2), FontAwesomeIcon.CalendarAlt, Theme.Gold);
            Ui.Text(dl, new Vector2(cx + Ui.IconSize(FontAwesomeIcon.CalendarAlt).X + S(8), cy + (lineH - LHs(titleS)) / 2), title, Theme.Text, titleS);

            float nx = cx + iw - navW;
            if (Ui.Button("ev_today", "Today", new Vector2(nx, cy + (lineH - BtnH) / 2), new Vector2(BtnW("Today", null), BtnH))) { _month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1); _picked = DateTime.Now.Date; }
            nx += BtnW("Today", null) + S(6);
            if (Ui.IconButton("ev_prev", FontAwesomeIcon.ChevronLeft, new Vector2(nx, cy + (lineH - BtnH) / 2), BtnH, "Previous month")) { _month = _month.AddMonths(-1); _picked = null; }
            nx += BtnH + S(6);
            if (Ui.IconButton("ev_next", FontAwesomeIcon.ChevronRight, new Vector2(nx, cy + (lineH - BtnH) / 2), BtnH, "Next month")) { _month = _month.AddMonths(1); _picked = null; }

            float fx = filterLine ? cx : cx + iw - navW - S(16) - filtersW;
            float fy = filterLine ? cy + lineH + S(8) : cy + (lineH - chipH) / 2;
            foreach (var k in Kinds)
            {
                bool off = _hidden.Contains(k);
                var cp = new Vector2(fx, fy);
                var cs = new Vector2(chipW[k], chipH);
                string tip = k == KLodestone ? "Official topics from the Lodestone" : k == KPartake ? "Community events imported from Partake.gg" : "Events you added yourself";
                if (Ui.Hit("ev_filter_" + k, cp, cs, out float hov)) ToggleKind(k);
                Ui.Tooltip($"{tip}. Click to {(off ? "show" : "hide")}.");
                var kc = KindColor(k);
                float a = off ? 0.4f : 1f;
                dl.AddRectFilled(cp, cp + cs, (off ? Theme.Night1 : kc.With(0.1f + 0.08f * hov)).U32(), cs.Y / 2);
                dl.AddRect(cp, cp + cs, kc.With((off ? 0.15f : 0.4f) + 0.2f * hov).U32(), cs.Y / 2, 0, S(1));
                dl.AddCircleFilled(cp + new Vector2(S(13), cs.Y / 2), S(4), kc.With(a).U32());
                float tx = cp.X + S(26), ty = cp.Y + (cs.Y - LHs(0.85f)) / 2;
                Ui.Text(dl, new Vector2(tx, ty), KindLabel(k), (off ? Theme.Text3 : Theme.Text), 0.85f);
                Ui.Text(dl, new Vector2(tx + Ui.TextSize(KindLabel(k), 0.85f).X + S(6), ty), counts[k].ToString(), kc.With(a), 0.85f);
                fx += cs.X + S(6);
            }
            cy += headH + S(12);

            for (int i = 0; i < 7; i++)
            {
                var lbl = compact ? WeekDays[i][..1] : WeekDays[i];
                float lx = cx + i * (cellW + cgap) + (cellW - Ui.TextSize(lbl, 0.8f).X) / 2;
                Ui.Text(dl, new Vector2(lx, cy), lbl, Theme.Text3, 0.8f);
            }
            cy += wdH;

            var today = DateTime.Now.Date;
            for (int d = 1; d <= days; d++)
            {
                int idx = first + d - 1;
                var cp = new Vector2(cx + (idx % 7) * (cellW + cgap), cy + (idx / 7) * (cellH + cgap));
                var date = new DateTime(year, month, d);
                var list = byDay.TryGetValue(d, out var l) ? l : new List<CalEvent>();
                bool isToday = date == today, isPicked = picked.HasValue && picked.Value.Date == date;
                var hitSize = compact ? new Vector2(cellW, cellH) : new Vector2(cellW, dayH);
                if (Ui.Hit($"ev_day_{d}", cp, hitSize, out float hov)) _picked = isPicked ? null : date;
                if (ImGui.IsItemHovered()) UIHelper.DrawTooltip($"{Day(date)}: {list.Count} event{(list.Count == 1 ? "" : "s")}");

                var fill = isPicked ? Theme.Crystal.With(0.07f) : Theme.Night1.With(list.Count > 0 ? 0.6f : 0.4f);
                var line = isPicked ? Theme.Crystal.With(0.8f) : isToday ? Theme.Gold.With(0.55f) : Theme.CrystalLine.With(0.1f + 0.2f * hov);
                dl.AddRectFilled(cp, cp + new Vector2(cellW, cellH), fill.U32(), S(5));
                dl.AddRect(cp, cp + new Vector2(cellW, cellH), line.U32(), S(5), 0, S(1));

                string num = d.ToString();
                var np = cp + new Vector2(S(6), S(4));
                Ui.Text(dl, np, num, isToday ? Theme.Gold : list.Count > 0 ? Theme.Text : Theme.Text2, 0.85f);
                float dx = np.X + Ui.TextSize(num, 0.85f).X + S(6);
                if (compact) { dx = cp.X + S(6); }
                float dotY = compact ? cp.Y + cellH - S(9) : np.Y + LHs(0.85f) / 2;
                for (int j = 0; j < Math.Min(4, list.Count); j++)
                    dl.AddCircleFilled(new Vector2(dx + S(3) + j * S(8), dotY), S(2.5f), KindColor(list[j].Kind).U32());

                if (compact) continue;
                float ty = cp.Y + dayH;
                float tw = cellW - S(8);
                for (int j = 0; j < Math.Min(3, list.Count); j++)
                {
                    var e = list[j];
                    var tp = new Vector2(cp.X + S(4), ty);
                    var ts = new Vector2(tw, thumbH);
                    bool clicked = Ui.Hit($"ev_th_{d}_{j}", tp, ts, out float th);
                    Ui.Tooltip(e.Title);
                    var kc = KindColor(e.Kind);
                    if (Cover(dl, e.Image, tp, tp + ts, 0.55f + 0.3f * th))
                        dl.AddRectFilled(tp, tp + ts, Theme.Night0.With(0.45f - 0.2f * th).U32());
                    else dl.AddRectFilled(tp, tp + ts, kc.With(0.14f + 0.12f * th).U32(), S(3));
                    dl.AddRectFilled(tp, tp + new Vector2(S(3), ts.Y), kc.U32());
                    Ui.Text(dl, tp + new Vector2(S(7), (ts.Y - LHs(0.78f)) / 2), Fit(e.Title, tw - S(10), 0.78f), Theme.Text, 0.78f);
                    if (clicked) OpenEvent(e);
                    ty += thumbH + S(3);
                }
                if (list.Count > 3)
                {
                    string more = $"+{list.Count - 3} more";
                    var mp = new Vector2(cp.X + S(5), ty);
                    if (Ui.Hit($"ev_more_{d}", mp, Ui.TextSize(more, 0.75f), out float mh)) _picked = date;
                    Ui.Text(dl, mp, more, UIHelper.LerpColor(Theme.Text3, Theme.Crystal, mh), 0.75f);
                }
            }
            cy += weeks * (cellH + cgap);

            if (picked.HasValue)
            {
                cy += S(16);
                Title(dl, cx, cy, picked.Value.ToString("dddd, MMMM d", Inv));
                if (Ui.IconButton("ev_day_x", FontAwesomeIcon.Times, new Vector2(cx + iw - S(24), cy - S(5)), S(24), "Close day")) _picked = null;
                cy += CapsH + S(10);
                if (pickedList.Count == 0) Ui.Text(dl, new Vector2(cx, cy), "Nothing on this day.", Theme.Text3, 0.9f);
                for (int i = 0; i < pickedList.Count; i++) { Row($"pk{i}", cx, cy, iw, pickedList[i], false); cy += RowH + S(6); }
            }
            return y + h;
        }

        // ---- dialogs -------------------------------------------------------------------------------------------------
        private (Vector2 Wp, Vector2 Ws) Scrim()
        {
            var wp = ImGui.GetWindowPos();
            var ws = ImGui.GetWindowSize();
            Ui.Dl.AddRectFilled(wp, wp + ws, Theme.Night0.With(0.6f).U32());
            return (wp, ws);
        }

        /// <summary>A click on the scrim (outside the dialog) closes it, like the website; not on the frame that opened it.</summary>
        private bool ClickedOutside(Vector2 a, Vector2 b)
            => ImGui.GetFrameCount() > _openedFrame + 1 && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
               && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !ImGui.IsMouseHoveringRect(a, b, false);

        private void DrawDetails()
        {
            var e = _selected!;
            var (wp, ws) = Scrim();
            var dl = Ui.Dl;
            float pad = S(18), dw = Math.Min(S(580), ws.X - S(32)), iw = dw - pad * 2;
            bool hasImg = Image(e.Image)?.GetWrapOrDefault() != null;
            float heroH = hasImg ? Math.Min(S(190), dw * 0.42f) : 0;

            var titleLines = Wrap(e.Title, iw - (hasImg ? 0 : S(30)), 1.25f);
            string when = e.Local.ToString(e.Kind == KLodestone ? "dddd, MMMM d, yyyy" : "dddd, MMMM d, yyyy, HH:mm", Inv);
            var descLines = Wrap(e.Description.Length > 0 ? e.Description : "No description provided.", iw - S(10), 0.95f);
            float chipH = LHs(0.82f) + S(6);
            float head = (hasImg ? heroH : 0) + pad + chipH + S(10) + titleLines.Count * LHs(1.25f) + S(10) + LH + (e.Location.Length > 0 ? LH + S(4) : 0)
                         + (e.Categories.Count > 0 ? S(10) + chipH : 0) + S(14);
            float foot = S(16) + BtnH + pad;
            float descFull = descLines.Count * LHs(0.95f);
            float maxH = ws.Y - S(32);
            float descH = Math.Max(Math.Min(descFull, maxH - head - foot), LHs(0.95f) * 2);
            float dh = head + descH + foot;
            var p = wp + new Vector2((ws.X - dw) / 2, Math.Max(S(16), (ws.Y - dh) / 2));
            var pe = p + new Vector2(dw, dh);
            Ui.Panel(dl, p, pe, Theme.Night1.With(0.98f), KindColor(e.Kind).With(0.45f), 12f);

            float x = p.X + pad, y = p.Y;
            if (hasImg)
            {
                Cover(dl, e.Image, p + S(1, 1), new Vector2(pe.X - S(1), p.Y + heroH));
                dl.AddRectFilledMultiColor(new Vector2(p.X, p.Y + heroH * 0.5f), new Vector2(pe.X, p.Y + heroH),
                    Theme.Night1.With(0f).U32(), Theme.Night1.With(0f).U32(), Theme.Night1.With(0.95f).U32(), Theme.Night1.With(0.95f).U32());
                y += heroH;
            }
            bool close = Ui.IconButton("ev_dt_x", FontAwesomeIcon.Times, new Vector2(pe.X - S(12) - S(28), p.Y + S(10)), S(28), "Close");
            y += pad;

            var kc = KindColor(e.Kind);
            Ui.Chip(dl, new Vector2(x, y), e.Kind == KLodestone ? "Lodestone topic" : e.Kind == KPartake ? "Partake event" : "Custom event", kc, out var cs);
            if (e.Weekly) Ui.Chip(dl, new Vector2(x + cs.X + S(6), y), "Weekly", Theme.Text2, out _);
            y += chipH + S(10);
            foreach (var l in titleLines) { Ui.Text(dl, new Vector2(x, y), l, Theme.Text, 1.25f); y += LHs(1.25f); }
            y += S(10);
            Ui.Icon(dl, new Vector2(x, y + S(2)), FontAwesomeIcon.CalendarAlt, Theme.Text3, 0.85f);
            Ui.Text(dl, new Vector2(x + S(22), y), Fit(when, iw - S(22)), Theme.Text2);
            y += LH;
            if (e.Location.Length > 0)
            {
                y += S(4);
                Ui.Icon(dl, new Vector2(x + S(1), y + S(2)), FontAwesomeIcon.MapMarkerAlt, Theme.Text3, 0.85f);
                Ui.Text(dl, new Vector2(x + S(22), y), Fit(e.Location, iw - S(22)), Theme.Text2);
                y += LH;
            }
            if (e.Categories.Count > 0)
            {
                y += S(10);
                float tx = x;
                foreach (var c in e.Categories)
                {
                    if (tx + Ui.TextSize(c, 0.82f).X + S(14) > x + iw) break;
                    Ui.Chip(dl, new Vector2(tx, y), c, Theme.Crystal, out var s2);
                    tx += s2.X + S(6);
                }
                y += chipH;
            }
            y += S(14);

            // description, scrolled with the wheel when it is longer than the box
            var da = new Vector2(x, y);
            var db = new Vector2(x + iw, y + descH);
            float maxScroll = Math.Max(0, descFull - descH);
            if (maxScroll > 0 && ImGui.IsMouseHoveringRect(da, db, false))
                _descScroll = Math.Clamp(_descScroll - ImGui.GetIO().MouseWheel * LHs(0.95f) * 3, 0, maxScroll);
            _descScroll = Math.Clamp(_descScroll, 0, maxScroll);
            ImGui.PushClipRect(da, db, true);
            float ly = y - _descScroll;
            foreach (var l in descLines)
            {
                if (ly + LHs(0.95f) >= da.Y && ly <= db.Y) Ui.Text(dl, new Vector2(x, ly), l, e.Description.Length > 0 ? Theme.Text : Theme.Text3, 0.95f);
                ly += LHs(0.95f);
            }
            ImGui.PopClipRect();
            if (maxScroll > 0)
            {
                float bh = descH * descH / descFull, by = y + (descH - bh) * (_descScroll / maxScroll);
                dl.AddRectFilled(new Vector2(db.X - S(3), by), new Vector2(db.X, by + bh), Theme.Text3.With(0.5f).U32(), S(2));
            }
            y += descH + S(16);

            // foot: delete (with confirm) left, open original right
            float bx = x;
            if (e.Kind != KLodestone)
            {
                if (_confirmDelete)
                {
                    string q = e.Weekly ? "Delete every week?" : "Delete this event?";
                    Ui.Text(dl, new Vector2(bx, y + (BtnH - LH) / 2), q, Theme.Text);
                    bx += Ui.TextSize(q).X + S(10);
                    if (Ui.Button("ev_del_yes", "Delete", new Vector2(bx, y), new Vector2(BtnW("Delete", null), BtnH), Kind.Danger)) { DeleteEvent(e); _confirmDelete = false; }
                    bx += BtnW("Delete", null) + S(6);
                    if (Ui.Button("ev_del_no", "Keep", new Vector2(bx, y), new Vector2(BtnW("Keep", null), BtnH))) _confirmDelete = false;
                }
                else if (Ui.Button("ev_del", "Delete", new Vector2(bx, y), new Vector2(BtnW("Delete", FontAwesomeIcon.TrashAlt), BtnH), Kind.Danger, FontAwesomeIcon.TrashAlt)) _confirmDelete = true;
            }
            if (IsWebLink(e.Url) && !_confirmDelete)
            {
                float ow = BtnW("Open original", FontAwesomeIcon.ExternalLinkAlt);
                if (Ui.Button("ev_open", "Open original", new Vector2(x + iw - ow, y), new Vector2(ow, BtnH), Kind.Gold, FontAwesomeIcon.ExternalLinkAlt)) OpenLink(e.Url);
                Ui.Tooltip(e.Url);
            }

            if (close || ClickedOutside(p, pe)) _selected = null;
        }

        private static readonly string[] DateFormats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "d.M.yyyy HH:mm", "yyyy-MM-dd" };

        private void DrawForm()
        {
            var f = _form!;
            var (wp, ws) = Scrim();
            var dl = Ui.Dl;
            float pad = S(18), dw = Math.Min(S(560), ws.X - S(32)), iw = dw - pad * 2;
            bool two = iw >= S(420);
            float lab = LHs(0.9f) + S(6), fgap = S(12), areaH = Math.Max(S(80), LH * 4 + S(14));
            float fields = (lab + FieldH + fgap) * (two ? 4 : 5) + LHs(0.8f) + S(4) + lab + areaH + fgap + BtnH;
            float dh = pad + LHs(1.1f) + S(16) + fields + S(16) + BtnH + pad;
            var p = wp + new Vector2((ws.X - dw) / 2, Math.Max(S(16), (ws.Y - dh) / 2));
            var pe = p + new Vector2(dw, dh);
            Ui.Panel(dl, p, pe, Theme.Night1.With(0.98f), Theme.Gold.With(0.45f), 12f);

            float x = p.X + pad, y = p.Y + pad;
            Gem(dl, new Vector2(x + S(5), y + LHs(1.1f) / 2), S(5), Theme.Gold);
            Ui.Text(dl, new Vector2(x + S(18), y), "Create custom event", Theme.Text, 1.1f);
            bool close = Ui.IconButton("ev_fm_x", FontAwesomeIcon.Times, new Vector2(pe.X - pad - S(26), y - S(2)), S(26), "Close");
            y += LHs(1.1f) + S(16);

            bool parsed = DateTime.TryParseExact(f.Date.Trim(), DateFormats, Inv, DateTimeStyles.AssumeLocal, out var start);
            bool ok = f.Title.Trim().Length > 0 && f.Location.Trim().Length > 0 && parsed;
            bool submit = false;

            void Field(string id, string label, string hint, ref string value, int max, float fx, float fw, bool focus = false)
            {
                Ui.Text(dl, new Vector2(fx, y), label, Theme.Text2, 0.9f);
                ImGui.SetCursorScreenPos(new Vector2(fx, y + lab));
                PushFrame();
                ImGui.SetNextItemWidth(fw);
                if (focus) ImGui.SetKeyboardFocusHere();
                if (ImGui.InputTextWithHint(id, hint, ref value, max, ImGuiInputTextFlags.EnterReturnsTrue)) submit = true;
                PopFrame();
            }

            Field("##ev_f_title", "Title*", "e.g. Free Company meeting", ref f.Title, 120, x, iw, _focusForm);
            _focusForm = false;
            y += lab + FieldH + fgap;

            float half = (iw - S(12)) / 2;
            Field("##ev_f_date", "Date and time*", "YYYY-MM-DD HH:mm", ref f.Date, 40, x, two ? half : iw);
            float hintY = y + lab + FieldH + S(4);
            if (two)
            {
                Field("##ev_f_loc", "Location*", "e.g. Shirogane W12 P34", ref f.Location, 120, x + half + S(12), half);
                y += lab + FieldH;
            }
            else y += lab + FieldH;
            Ui.Text(dl, new Vector2(x, hintY), parsed ? start.ToString("dddd, MMMM d, HH:mm", Inv) + " (your local time)" : "Use YYYY-MM-DD HH:mm, e.g. 2026-11-20 20:00",
                parsed ? Theme.Text3 : Theme.Bad, 0.8f);
            y += LHs(0.8f) + S(4) + fgap;
            if (!two)
            {
                Field("##ev_f_loc", "Location*", "e.g. Shirogane W12 P34", ref f.Location, 120, x, iw);
                y += lab + FieldH + fgap;
            }
            Field("##ev_f_url", "Event link", "https://...", ref f.Url, 400, x, iw);
            y += lab + FieldH + fgap;
            Field("##ev_f_img", "Cover image link", "https://...", ref f.Image, 400, x, iw);
            y += lab + FieldH + fgap;

            Ui.Text(dl, new Vector2(x, y), "Description", Theme.Text2, 0.9f);
            ImGui.SetCursorScreenPos(new Vector2(x, y + lab));
            PushFrame();
            ImGui.InputTextMultiline("##ev_f_desc", ref f.Description, 4000, new Vector2(iw, areaH));
            PopFrame();
            y += lab + areaH + fgap;

            float rw = BtnW("Repeat weekly", FontAwesomeIcon.Redo);
            if (Ui.Button("ev_f_weekly", "Repeat weekly", new Vector2(x, y), new Vector2(rw, BtnH), f.Weekly ? Kind.Gold : Kind.Ghost, f.Weekly ? FontAwesomeIcon.Check : FontAwesomeIcon.Redo)) f.Weekly = !f.Weekly;
            Ui.Tooltip("The event shows every week for half a year ahead");
            y += BtnH + S(16);

            string main = _saving ? "Saving..." : "Create event";
            float mw = BtnW(main, null), cw = BtnW("Cancel", null);
            float bx = pe.X - pad - mw;
            if (Ui.Button("ev_f_ok", main, new Vector2(bx, y), new Vector2(mw, BtnH), ok && !_saving ? Kind.Gold : Kind.Ghost)) submit = true;
            if (!ok) Ui.Tooltip("Title, date and location are needed");
            if (Ui.Button("ev_f_cancel", "Cancel", new Vector2(bx - S(8) - cw, y), new Vector2(cw, BtnH))) close = true;
            if (_msgBad && _msg.Length > 0) Ui.Text(dl, new Vector2(x, y + (BtnH - LHs(0.85f)) / 2), Fit(_msg, bx - S(16) - cw - x, 0.85f), Theme.Bad, 0.85f);

            if (submit && ok && !_saving) Create(f, start);
            if (close) _form = null;
        }

        private void DrawSubs()
        {
            var (wp, ws) = Scrim();
            var dl = Ui.Dl;
            float pad = S(18), dw = Math.Min(S(520), ws.X - S(32)), iw = dw - pad * 2;
            var note = Wrap("New events from these venues are pulled in by themselves every hour. Removing a venue also removes its events from your calendar.", iw, 0.88f);
            float rowH = LH + LHs(0.8f) + S(16);
            int n = Math.Max(1, _subs.Count);
            float dh = pad + LHs(1.1f) + S(14) + note.Count * LHs(0.88f) + S(14) + n * (rowH + S(6)) + pad;
            dh = Math.Min(dh, ws.Y - S(32));
            var p = wp + new Vector2((ws.X - dw) / 2, Math.Max(S(16), (ws.Y - dh) / 2));
            var pe = p + new Vector2(dw, dh);
            Ui.Panel(dl, p, pe, Theme.Night1.With(0.98f), Theme.Crystal.With(0.4f), 12f);

            float x = p.X + pad, y = p.Y + pad;
            Gem(dl, new Vector2(x + S(5), y + LHs(1.1f) / 2), S(5), Theme.Gold);
            Ui.Text(dl, new Vector2(x + S(18), y), "Subscribed venues", Theme.Text, 1.1f);
            bool close = Ui.IconButton("ev_sb_x", FontAwesomeIcon.Times, new Vector2(pe.X - pad - S(26), y - S(2)), S(26), "Close");
            y += LHs(1.1f) + S(14);
            foreach (var l in note) { Ui.Text(dl, new Vector2(x, y), l, Theme.Text3, 0.88f); y += LHs(0.88f); }
            y += S(14);

            ImGui.PushClipRect(new Vector2(p.X, y), new Vector2(pe.X, pe.Y - S(6)), true);
            if (_subs.Count == 0) Ui.Text(dl, new Vector2(x, y), Fit("No venues yet. Paste a partake.gg/teams/... link to subscribe.", iw, 0.9f), Theme.Text3, 0.9f);
            foreach (var s in _subs.ToList())
            {
                var rp = new Vector2(x, y);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(0.6f).U32(), S(6));
                float tw = iw - S(50);
                Ui.Text(dl, rp + S(10, 7), Fit(s.Name, tw), Theme.Text);
                string link = s.Url.Replace("https://www.", "").Replace("https://", "");
                var lp = rp + new Vector2(S(10), S(7) + LH + S(2));
                var ls = Vector2.Max(Ui.TextSize(Fit(link, tw, 0.8f), 0.8f), S(4, 4)); // InvisibleButton asserts on size 0
                bool linkClick = Ui.Hit("ev_sb_link_" + s.Id, lp, ls, out float lh);
                Ui.Tooltip("Open on Partake");
                Ui.Text(dl, lp, Fit(link, tw, 0.8f), UIHelper.LerpColor(Theme.Crystal.With(0.8f), Theme.Crystal, lh), 0.8f);
                if (linkClick) OpenLink(s.Url);
                float bs = S(30);
                if (Ui.IconButton("ev_sb_del_" + s.Id, FontAwesomeIcon.TrashAlt, rp + new Vector2(iw - bs - S(8), (rowH - bs) / 2), bs, "Remove venue", Theme.Bad)) RemoveSub(s.Id);
                y += rowH + S(6);
            }
            ImGui.PopClipRect();

            if (close || ClickedOutside(p, pe)) _showSubs = false;
        }

        private static void PushFrame()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(9, 7));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Night2.With(0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Night3);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Night3);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.CrystalLine);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.Text3);
        }

        private static void PopFrame()
        {
            ImGui.PopStyleColor(6);
            ImGui.PopStyleVar(3);
        }
    }
}
