using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // The screens, laid out like the website's Raid Planner: your statics + form/join, then one static with the tabs
    // Schedule / Roster / Loot & BiS / Strategy / Notes / Settings. Dialogs draw on the window draw list over a
    // disabled page (no child windows). Schedule and Loot are in their own files.
    public partial class RaidPlannerApp
    {
        private string _newTitle = "", _joinCode = "", _addName = "";
        private bool _confirmLeave;
        private string _confirmKick = "";
        private float _lockScroll;
        private bool _wasModal;
        private readonly HashSet<string> _openNotes = new();

        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float LHs(float scale) => Ui.TextSize("Ag", scale).Y;
        private static float CapsH => Ui.TextSize("A", 0.78f).Y;
        private static float BtnH => LH + S(12);
        private static float FieldH => ImGui.GetFontSize() + S(7) * 2;
        private static float Pad => S(14);

        private static string Day(DateTime d) => d.ToString("ddd, MMM d", Inv);
        private static string Time(DateTime d) => d.ToString("HH:mm", Inv);
        private static string RelDay(DateTime local)
        {
            int days = (local.Date - DateTime.Now.Date).Days;
            if (days == 0) return local < DateTime.Now ? "today, started" : "today";
            if (days == 1) return "tomorrow";
            if (days == -1) return "yesterday";
            return days > 0 ? $"in {days} days" : $"{-days} days ago";
        }

        private static Vector4 RoleColor(string role) => role == "tank" ? Theme.Crystal : role == "healer" ? Theme.Live : Theme.Bad;
        private static FontAwesomeIcon RoleIcon(string role) => role == "tank" ? FontAwesomeIcon.Shield : role == "healer" ? FontAwesomeIcon.Heart : FontAwesomeIcon.Khanda;
        private static Vector4 CatColor(string c) => c switch
        {
            "prog" => Theme.Violet, "reclear" => Theme.Live, "farm" => Theme.Gold, "ultimate" => Theme.Warn, "social" => Theme.Text2, "other" => Theme.Text3, _ => Theme.Crystal,
        };
        private static Vector4 RsvpColor(string? s) => s == "yes" ? Theme.Live : s == "maybe" ? Theme.Warn : s == "no" ? Theme.Bad : Theme.Text3;

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

        /// <summary>Gold gem + small caps header (website ax-panel-title); returns its width.</summary>
        private static float Title(ImDrawListPtr dl, float x, float y, string text, string? aside = null)
        {
            Gem(dl, new Vector2(x + S(5), y + CapsH / 2), S(4), Theme.Gold);
            float w = S(16) + Ui.Caps(dl, new Vector2(x + S(16), y), text);
            if (!string.IsNullOrEmpty(aside))
            {
                Ui.Text(dl, new Vector2(x + w + S(8), y + (CapsH - LHs(0.8f)) / 2), aside, Theme.Text3, 0.8f);
                w += S(8) + Ui.TextSize(aside, 0.8f).X;
            }
            return w;
        }

        /// <summary>A toggle/action pill: tinted by colour, filled when on. Disabled pills draw dim and never click.</summary>
        private static bool Pill(string id, string label, FontAwesomeIcon? icon, Vector2 pos, Vector2 size, Vector4 color, bool on, bool enabled = true, string? tip = null)
        {
            float h = 0;
            bool clicked = false;
            if (enabled) { clicked = Ui.Hit(id, pos, size, out h); if (tip != null) Ui.Tooltip(tip); }
            else if (tip != null) { ImGui.SetCursorScreenPos(pos); ImGui.Dummy(size); Ui.Tooltip(tip); }
            var dl = Ui.Dl;
            float a = enabled ? 1f : 0.45f;
            dl.AddRectFilled(pos, pos + size, (on ? color.With((0.24f + 0.12f * h) * a) : Theme.Night2.With((0.6f + 0.3f * h) * a)).U32(), S(6));
            dl.AddRect(pos, pos + size, color.With((on ? 0.7f : 0.22f + 0.35f * h) * a).U32(), S(6), 0, S(1));
            var tc = (on ? color : UIHelper.LerpColor(Theme.Text2, Theme.Text, h)).With(a);
            float iw = icon.HasValue ? Ui.IconSize(icon.Value, 0.85f).X : 0;
            float lw = label.Length > 0 ? Ui.TextSize(label, 0.92f).X : 0;
            float tw = iw + lw + (iw > 0 && lw > 0 ? S(6) : 0);
            var start = pos + new Vector2((size.X - tw) / 2, 0);
            if (icon.HasValue) Ui.Icon(dl, new Vector2(start.X, pos.Y + (size.Y - Ui.IconSize(icon.Value, 0.85f).Y) / 2), icon.Value, tc, 0.85f);
            if (lw > 0) Ui.Text(dl, new Vector2(start.X + tw - lw, pos.Y + (size.Y - LHs(0.92f)) / 2), label, tc, 0.92f);
            return clicked && enabled;
        }

        private static float PillW(string label, FontAwesomeIcon? icon)
            => (label.Length > 0 ? Ui.TextSize(label, 0.92f).X : 0) + (icon.HasValue ? Ui.IconSize(icon.Value, 0.85f).X + (label.Length > 0 ? S(6) : 0) : 0) + S(20);

        /// <summary>Segmented choice (website Seg). Returns the picked value, or null.</summary>
        private static string? Seg(string id, float x, float y, (string V, string Label)[] opts, string cur, out float width)
        {
            string? picked = null;
            float cx = x;
            foreach (var (v, label) in opts)
            {
                float w = PillW(label, null);
                if (Pill($"{id}_{v}", label, null, new Vector2(cx, y), new Vector2(w, BtnH), Ui.Accent, v == cur) && v != cur) picked = v;
                cx += w + S(4);
            }
            width = cx - S(4) - x;
            return picked;
        }

        private static float SegW((string V, string Label)[] opts) => opts.Sum(o => PillW(o.Label, null) + S(4)) - S(4);

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

        private static bool Input(string id, string hint, ref string value, int max, float x, float y, float w, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None, bool focus = false)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            PushFrame();
            ImGui.SetNextItemWidth(w);
            if (focus) ImGui.SetKeyboardFocusHere();
            bool enter = ImGui.InputTextWithHint(id, hint, ref value, max, flags | ImGuiInputTextFlags.EnterReturnsTrue);
            PopFrame();
            return enter;
        }

        private float MsgLine(ImDrawListPtr dl, float x, float y, float w)
        {
            if (_msg.Length == 0) return y;
            foreach (var l in Wrap(_msg, w, 0.88f)) { Ui.Text(dl, new Vector2(x, y), l, _msgBad ? Theme.Bad : Theme.Live, 0.88f); y += LHs(0.88f); }
            return y;
        }

        private void Copy(string text, string what) { ImGui.SetClipboardText(text); Flash(what); }

        // =============================================================================================================
        public void Draw()
        {
            var o = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            var dl = Ui.Dl;
            if (!SignedIn)
            {
                float h = Pad * 2 + LH * 2 + S(6);
                Ui.Panel(dl, o, o + new Vector2(w, h), null, null, 12f);
                Ui.Text(dl, o + new Vector2(Pad, Pad), "Raid Planner", Theme.Text);
                Ui.Text(dl, o + new Vector2(Pad, Pad + LH + S(6)), Fit("Sign in to Aetherhold (Settings > Account) to plan raids with your static.", w - Pad * 2, 0.92f), Theme.Text3, 0.92f);
                ImGui.SetCursorScreenPos(o + new Vector2(0, h));
                ImGui.Dummy(new Vector2(S(1), S(4)));
                return;
            }
            EnsureFresh();
            if (_msg.Length > 0 && (DateTime.Now - _msgAt).TotalSeconds > (_msgBad ? 6 : 4)) _msg = "";

            var g = _open != null ? _groups.FirstOrDefault(x => x.Id == _open) : null;
            if (g == null) { _dlg = Dlg.None; _picker = null; _confirmLeave = false; }

            bool modal = _dlg != Dlg.None || _picker != null;
            if (modal && !_wasModal) _lockScroll = ImGui.GetScrollY();
            if (modal) ImGui.SetScrollY(_lockScroll); // the page stays put under a dialog
            _wasModal = modal;

            if (modal) ImGui.BeginDisabled();
            float end = g == null ? DrawHome(o, w) : DrawStatic(g, o, w);
            ImGui.SetCursorScreenPos(new Vector2(o.X, end));
            ImGui.Dummy(new Vector2(S(1), S(4)));
            if (!modal) return;
            ImGui.EndDisabled();
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) { if (_picker != null) _picker = null; else _dlg = Dlg.None; return; }
            if (g == null) return;
            if (_dlg != Dlg.None)
            {
                if (_picker != null) ImGui.BeginDisabled();
                DrawDialog(g);
                if (_picker != null) ImGui.EndDisabled();
            }
            if (_picker != null) DrawPicker();
        }

        // ---- home: your statics, form, join --------------------------------------------------------------------------
        private float DrawHome(Vector2 o, float w)
        {
            float gap = S(12);
            bool two = w >= S(760);
            float lw = two ? (w - gap) * 0.58f : w;
            float y1 = DrawStaticList(o.X, o.Y, lw);
            float y2 = DrawForms(two ? o.X + lw + gap : o.X, two ? o.Y : y1 + gap, two ? w - lw - gap : w);
            return Math.Max(y1, y2);
        }

        private float DrawStaticList(float x, float y, float w)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2, rowH = LH + LHs(0.85f) + S(16), rgap = S(6);
            var list = _groups;
            int rows = Math.Max(1, list.Count);
            float h = Pad + CapsH + S(12) + rows * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            Title(dl, cx, cy, "Your statics", list.Count > 0 ? list.Count.ToString() : null);
            cy += CapsH + S(12);
            if (list.Count == 0)
            {
                string t = _loading ? "Loading..." : _loadError.Length > 0 ? _loadError : "You aren't in any static yet. Form one, or join with a code from your leader.";
                foreach (var l in Wrap(t, iw, 0.92f).Take(2)) { Ui.Text(dl, new Vector2(cx, cy), l, _loadError.Length > 0 ? Theme.Bad : Theme.Text3, 0.92f); cy += LHs(0.92f); }
                return y + h;
            }
            var now = DateTime.UtcNow;
            foreach (var g in list)
            {
                var rp = new Vector2(cx, cy);
                bool click = Ui.Hit("rd_st_" + g.Id, rp, new Vector2(iw, rowH), out float hv);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(0.55f + 0.3f * hv).U32(), S(6));
                dl.AddRect(rp, rp + new Vector2(iw, rowH), Theme.Crystal.With(0.12f + 0.3f * hv).U32(), S(6), 0, S(1));

                string role = g.CreatorId == _me ? "Leader" : (g.Leads?.Contains(_me) ?? false) ? "Lead" : "";
                float side = Ui.TextSize(g.Id, 0.9f).X + (role.Length > 0 ? Ui.TextSize(role, 0.82f).X + S(24) : 0) + S(14);
                Ui.Text(dl, rp + S(12, 7), Fit(g.Title, iw - side - S(24)), Theme.Text);
                int filled = g.Roster.Count(s => s.Player != null);
                var next = Occurrences(g, now.AddHours(-3), now.AddDays(30)).FirstOrDefault(oc => !oc.Cancelled);
                string meta = $"{filled}/8" + (g.Bench.Count > 0 ? $" · {g.Bench.Count} bench" : "") + (next != null ? $" · next {Day(next.LocalStart)}, {Time(next.LocalStart)}" : "");
                Ui.Text(dl, rp + new Vector2(S(12), S(7) + LH + S(2)), Fit(meta, iw - side - S(24), 0.85f), Theme.Text3, 0.85f);

                float sx = rp.X + iw - S(12);
                if (role.Length > 0)
                {
                    float cw = Ui.TextSize(role, 0.82f).X + S(14);
                    sx -= cw;
                    Ui.Chip(dl, new Vector2(sx, rp.Y + (rowH - LHs(0.82f) - S(6)) / 2), role, Theme.Gold, out _);
                    sx -= S(10);
                }
                float codeW = Ui.TextSize(g.Id, 0.9f).X;
                Ui.Text(dl, new Vector2(sx - codeW, rp.Y + (rowH - LHs(0.9f)) / 2), g.Id, Theme.Crystal, 0.9f);
                if (click) { SavePrefs(g.Id, true); _confirmLeave = false; }
                cy += rowH + rgap;
            }
            return y + h;
        }

        private float DrawForms(float x, float y, float w)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2;
            var hint = Wrap("The leader shares the six-character code. Joining puts you on the bench; the leader places players on the roster. Friends without a login can use the static's guest link instead.", iw, 0.85f);
            var msg = _msg.Length > 0 ? Wrap(_msg, iw, 0.88f) : new List<string>();
            float h = Pad + (CapsH + S(10) + FieldH + S(16)) * 2 + hint.Count * LHs(0.85f) + (msg.Count > 0 ? S(8) + msg.Count * LHs(0.88f) : 0) + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;

            Title(dl, cx, cy, "Form a static");
            cy += CapsH + S(10);
            float bw = BtnW("Create", FontAwesomeIcon.Plus);
            bool go = Input("##rd_new", "Static name", ref _newTitle, 40, cx, cy, iw - bw - S(8));
            bool can = _newTitle.Trim().Length > 0 && !_busy;
            if (Ui.Button("rd_create", "Create", new Vector2(cx + iw - bw, cy), new Vector2(bw, FieldH), can ? Kind.Gold : Kind.Ghost, FontAwesomeIcon.Plus)) go = true;
            if (go && can) Create(_newTitle);
            cy += FieldH + S(16);

            Title(dl, cx, cy, "Join a static");
            cy += CapsH + S(10);
            bw = BtnW("Join", FontAwesomeIcon.Check);
            bool join = Input("##rd_join", "CODE12", ref _joinCode, 7, cx, cy, iw - bw - S(8), ImGuiInputTextFlags.CharsUppercase | ImGuiInputTextFlags.CharsNoBlank);
            var code = new string(_joinCode.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            bool canJoin = code.Length == 6 && !_busy;
            if (Ui.Button("rd_joinb", "Join", new Vector2(cx + iw - bw, cy), new Vector2(bw, FieldH), canJoin ? Kind.Primary : Kind.Ghost, FontAwesomeIcon.Check)) join = true;
            if (join && canJoin) Join(code);
            cy += FieldH + S(16);
            foreach (var l in hint) { Ui.Text(dl, new Vector2(cx, cy), l, Theme.Text3, 0.85f); cy += LHs(0.85f); }
            if (msg.Count > 0) MsgLine(dl, cx, cy + S(8), iw);
            return y + h;
        }

        // ---- one static: header, tabs, body ------------------------------------------------------------------------
        private float DrawStatic(Group g, Vector2 o, float w)
        {
            var dl = Ui.Dl;
            bool lead = IsManager(g, _me), leader = g.CreatorId == _me;
            string tab = _tab == "settings" && !lead ? "schedule" : _tab;
            float gap = S(10);

            // header
            float hh = Pad * 2 + Math.Max(LHs(1.3f), BtnH);
            Ui.Panel(dl, o, o + new Vector2(w, hh), null, null, 12f);
            float cx = o.X + Pad, cy = o.Y + Pad, mid = o.Y + hh / 2;
            float ib = BtnH;
            if (Ui.IconButton("rd_back", FontAwesomeIcon.ChevronLeft, new Vector2(cx, mid - ib / 2), ib, "Back to your statics")) { SavePrefs(null, true); _confirmLeave = false; }
            cx += ib + S(8);

            // right side first, so the title knows its room
            float rx = o.X + w - Pad;
            if (_confirmLeave)
            {
                string q = leader ? "Delete for everyone?" : "Leave this static?";
                string yes = leader ? "Delete" : "Leave", no = leader ? "Keep" : "Stay";
                float nw = BtnW(no, null), yw = BtnW(yes, null);
                rx -= nw;
                if (Ui.Button("rd_lv_no", no, new Vector2(rx, mid - BtnH / 2), new Vector2(nw, BtnH))) _confirmLeave = false;
                rx -= yw + S(6);
                if (Ui.Button("rd_lv_yes", yes, new Vector2(rx, mid - BtnH / 2), new Vector2(yw, BtnH), Kind.Danger))
                {
                    _confirmLeave = false;
                    if (leader) DeleteStatic(g.Id); else Act(g.Id, B("leave"));
                }
                rx -= Ui.TextSize(q, 0.9f).X + S(10);
                Ui.Text(dl, new Vector2(rx, mid - LHs(0.9f) / 2), q, Theme.Warn, 0.9f);
            }
            else
            {
                rx -= ib;
                if (Ui.IconButton("rd_leave", leader ? FontAwesomeIcon.TrashAlt : FontAwesomeIcon.SignOutAlt, new Vector2(rx, mid - ib / 2), ib,
                        leader ? "Delete this static for everyone" : "Leave this static", Theme.Text3)) _confirmLeave = true;
            }
            rx -= ib + S(4);
            if (Ui.IconButton("rd_web", FontAwesomeIcon.ExternalLinkAlt, new Vector2(rx, mid - ib / 2), ib, "Open the Raid Planner on the website (same static and tab)")) OpenWebsite();
            if (g.GuestOpen)
            {
                rx -= ib + S(4);
                if (Ui.IconButton("rd_guest", FontAwesomeIcon.Link, new Vector2(rx, mid - ib / 2), ib, "Copy the guest link (for players without a login)"))
                    Copy($"{HubClient.BaseUrl}/raid/{g.Id}", "Guest link copied");
            }
            // code chip (click copies)
            var codeTs = Ui.TextSize(g.Id, 0.9f);
            float chipW = codeTs.X + Ui.IconSize(FontAwesomeIcon.Copy, 0.75f).X + S(24), chipH = codeTs.Y + S(8);
            rx -= chipW + S(10);
            var cp = new Vector2(rx, mid - chipH / 2);
            bool copy = Ui.Hit("rd_code", cp, new Vector2(chipW, chipH), out float chv);
            Ui.Tooltip("Join code for players with an Aetherhold login. Click to copy.");
            dl.AddRectFilled(cp, cp + new Vector2(chipW, chipH), Theme.Gold.With(0.12f + 0.12f * chv).U32(), chipH / 2);
            dl.AddRect(cp, cp + new Vector2(chipW, chipH), Theme.Gold.With(0.45f).U32(), chipH / 2, 0, S(1));
            Ui.Text(dl, cp + new Vector2(S(10), S(4)), g.Id, Theme.Gold, 0.9f);
            Ui.Icon(dl, cp + new Vector2(S(14) + codeTs.X, (chipH - Ui.IconSize(FontAwesomeIcon.Copy, 0.75f).Y) / 2), FontAwesomeIcon.Copy, Theme.Gold, 0.75f);
            if (copy) Copy(g.Id, "Code copied");

            Ui.Text(dl, new Vector2(cx, mid - LHs(1.3f) / 2), Fit(g.Title, rx - cx - S(12), 1.3f), Theme.Text, 1.3f);
            float y = o.Y + hh + gap;

            // tabs
            var tabs = new List<(string V, string Label, FontAwesomeIcon Icon)>
            {
                ("schedule", "Schedule", FontAwesomeIcon.CalendarAlt), ("roster", "Roster", FontAwesomeIcon.Users), ("loot", "Loot & BiS", FontAwesomeIcon.Gem),
                ("strategy", "Strategy", FontAwesomeIcon.Map), ("notes", "Notes", FontAwesomeIcon.StickyNote),
            };
            if (lead) tabs.Add(("settings", "Settings", FontAwesomeIcon.Cog));
            bool icons = tabs.Sum(t => PillW(t.Label, t.Icon) + S(4)) <= w;
            float tx = o.X;
            float tw = icons ? 0 : (w - S(4) * (tabs.Count - 1)) / tabs.Count;
            foreach (var t in tabs)
            {
                float bw = icons ? PillW(t.Label, t.Icon) : tw;
                string label = icons || Ui.TextSize(t.Label, 0.92f).X + S(12) <= bw ? t.Label : t.Label.Split(' ')[0];
                if (Pill("rd_tab_" + t.V, label, icons ? t.Icon : null, new Vector2(tx, y), new Vector2(bw, BtnH), Ui.Accent, tab == t.V) && tab != t.V)
                { SavePrefs(tab: t.V); _confirmKick = ""; }
                tx += bw + S(4);
            }
            if (_busy && icons) Ui.Icon(dl, new Vector2(tx + S(6), y + (BtnH - Ui.IconSize(FontAwesomeIcon.HourglassHalf, 0.85f).Y) / 2), FontAwesomeIcon.HourglassHalf, Theme.Text3, 0.85f);
            y += BtnH + gap;
            if (_msg.Length > 0) y = MsgLine(dl, o.X, y, w) + S(8);

            return tab switch
            {
                "roster" => DrawRoster(g, o.X, y, w, lead),
                "loot" => DrawLoot(g, o.X, y, w, lead),
                "strategy" => DrawStrategy(g, o.X, y, w),
                "notes" => DrawNotes(g, o.X, y, w),
                "settings" => DrawSettingsTab(g, o.X, y, w),
                _ => DrawSchedule(g, o.X, y, w, lead),
            };
        }

        // ---- roster ------------------------------------------------------------------------------------------------
        private float DrawRoster(Group g, float x, float y, float w, bool lead)
        {
            float gap = S(12);
            bool two = w >= S(720);
            float sw = two ? (w - gap) * 0.62f : w;
            float y1 = DrawSlots(g, x, y, sw, lead);
            float y2 = DrawBench(g, two ? x + sw + gap : x, two ? y : y1 + gap, two ? w - sw - gap : w, lead);
            return Math.Max(y1, y2);
        }

        private void Badges(ImDrawListPtr dl, Group g, Member m, ref float bx, float by)
        {
            if (m.CharacterId == g.CreatorId)
            {
                Ui.Icon(dl, new Vector2(bx, by), FontAwesomeIcon.Crown, Theme.Gold, 0.75f);
                ImGui.SetCursorScreenPos(new Vector2(bx, by)); ImGui.Dummy(Ui.IconSize(FontAwesomeIcon.Crown, 0.75f)); Ui.Tooltip("Leader");
                bx += Ui.IconSize(FontAwesomeIcon.Crown, 0.75f).X + S(6);
            }
            if (g.Leads?.Contains(m.CharacterId) ?? false)
            {
                Ui.Icon(dl, new Vector2(bx, by), FontAwesomeIcon.Star, Theme.Gold, 0.75f);
                ImGui.SetCursorScreenPos(new Vector2(bx, by)); ImGui.Dummy(Ui.IconSize(FontAwesomeIcon.Star, 0.75f)); Ui.Tooltip("Lead: can plan and edit");
                bx += Ui.IconSize(FontAwesomeIcon.Star, 0.75f).X + S(6);
            }
            if (IsManual(m.CharacterId))
            {
                string t = m.Claimed ? "link" : "name only";
                Ui.Text(dl, new Vector2(bx, by), t, m.Claimed ? Theme.Live : Theme.Text3, 0.75f);
                ImGui.SetCursorScreenPos(new Vector2(bx, by)); ImGui.Dummy(Vector2.Max(Ui.TextSize(t, 0.75f), S(2, 2)));
                Ui.Tooltip(m.Claimed ? "Uses the guest link" : "Added by name; not linked to anyone yet. Send them the guest link.");
                bx += Ui.TextSize(t, 0.75f).X + S(6);
            }
        }

        private float DrawSlots(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2, rowH = Math.Max(BtnH + S(10), LH + LHs(0.8f) + S(14)), rgap = S(6);
            float h = Pad + CapsH + S(12) + g.Roster.Count * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            Title(dl, cx, cy, "Roster", $"{g.Roster.Count(s => s.Player != null)}/8");
            cy += CapsH + S(12);

            // attendance over the last 8 weeks
            var now = DateTime.UtcNow;
            var past = Occurrences(g, now.AddDays(-56), now).Where(oc => !oc.Cancelled).ToList();
            var candidates = g.Bench.Concat(g.Roster.Where(s => s.Player != null).Select(s => s.Player!)).ToList();

            foreach (var slot in g.Roster)
            {
                var rp = new Vector2(cx, cy);
                var p = slot.Player;
                bool mine = p != null && p.CharacterId == _me;
                var rc = RoleColor(slot.Role);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), (mine ? Ui.Accent.With(0.1f) : Theme.Night2.With(0.55f)).U32(), S(6));
                dl.AddRectFilled(rp, rp + new Vector2(S(3), rowH), rc.With(0.8f).U32(), S(2));
                float mid = rp.Y + rowH / 2;
                // position
                string pos = Position.TryGetValue(slot.Id, out var ps) ? ps : slot.Id;
                Ui.Icon(dl, new Vector2(rp.X + S(12), mid - Ui.IconSize(RoleIcon(slot.Role), 0.75f).Y / 2), RoleIcon(slot.Role), rc, 0.75f);
                Ui.Text(dl, new Vector2(rp.X + S(32), mid - LH / 2), pos, rc);
                ImGui.SetCursorScreenPos(rp); ImGui.Dummy(new Vector2(S(70), rowH));
                Ui.Tooltip(slot.Role == "tank" ? (slot.Id == "tank1" ? "Main tank" : "Off tank") : slot.Role == "healer" ? "Healer" : slot.Id is "dps1" or "dps2" ? "Melee DPS" : "Ranged / caster DPS");
                float nx = rp.X + S(74);

                // right side: unassign, job, attendance
                float rx = rp.X + iw - S(8);
                float bs = BtnH;
                if (p != null && lead)
                {
                    rx -= bs;
                    if (Ui.IconButton("rd_un_" + slot.Id, FontAwesomeIcon.Times, new Vector2(rx, mid - bs / 2), bs, "Move to the bench", Theme.Text3) && !_busy)
                        Act(g.Id, B("unassign", ("slotId", slot.Id)));
                    rx -= S(6);
                }
                if (p != null)
                {
                    bool canJob = lead || mine;
                    string jl = slot.Job ?? (canJob ? "Job" : "-");
                    float jw = Math.Max(S(64), PillW(jl, canJob ? FontAwesomeIcon.CaretDown : null));
                    rx -= jw;
                    if (Pill("rd_job_" + slot.Id, jl, canJob ? FontAwesomeIcon.CaretDown : null, new Vector2(rx, mid - bs / 2), new Vector2(jw, bs), rc, slot.Job != null, canJob && !_busy, "Job for this tier"))
                    {
                        var sid = slot.Id;
                        OpenPicker($"Job · {pos}", new[] { ("No job", (string?)null, true) }.Concat(Jobs[slot.Role].Select(j => (j, (string?)j, true))),
                            v => Act(g.Id, B("setJob", ("slotId", sid), ("job", v))), slot.Job);
                    }
                    rx -= S(10);
                    if (past.Count > 0)
                    {
                        int missed = past.Count(oc => RsvpOf(g, oc, p.CharacterId).S == "no");
                        string pct = $"{(int)Math.Round(100.0 * (past.Count - missed) / past.Count)}%";
                        float pw = Ui.TextSize(pct, 0.85f).X;
                        rx -= pw;
                        Ui.Text(dl, new Vector2(rx, mid - LHs(0.85f) / 2), pct, missed / (float)past.Count > 0.25f ? Theme.Warn : Theme.Text2, 0.85f);
                        ImGui.SetCursorScreenPos(new Vector2(rx, mid - LHs(0.85f) / 2)); ImGui.Dummy(Ui.TextSize(pct, 0.85f));
                        Ui.Tooltip($"Last 8 weeks: {past.Count - missed} of {past.Count} sessions");
                        rx -= S(10);
                    }
                    // name, world, badges
                    float room = rx - nx;
                    string name = p.PlayerName + (mine ? "  (you)" : "");
                    if (p.World is { Length: > 0 } world)
                    {
                        Ui.Text(dl, new Vector2(nx, rp.Y + S(7)), Fit(name, room), Theme.Text);
                        float bx = nx;
                        Ui.Text(dl, new Vector2(bx, rp.Y + S(7) + LH + S(1)), Fit(world, room * 0.6f, 0.8f), Theme.Text3, 0.8f);
                        bx += Ui.TextSize(Fit(world, room * 0.6f, 0.8f), 0.8f).X + S(8);
                        Badges(dl, g, p, ref bx, rp.Y + S(7) + LH + S(1));
                    }
                    else
                    {
                        Ui.Text(dl, new Vector2(nx, mid - LH / 2), Fit(name, room - S(60)), Theme.Text);
                        float bx = nx + Ui.TextSize(Fit(name, room - S(60))).X + S(8);
                        Badges(dl, g, p, ref bx, mid - LHs(0.75f) / 2);
                    }
                }
                else if (lead)
                {
                    string al = candidates.Count > 0 ? "Assign..." : "Bench is empty";
                    float aw = Math.Min(iw - S(90), Math.Max(S(150), PillW(al, FontAwesomeIcon.CaretDown)));
                    if (Pill("rd_as_" + slot.Id, al, FontAwesomeIcon.CaretDown, new Vector2(nx, mid - bs / 2), new Vector2(aw, bs), Theme.Gold, false, candidates.Count > 0 && !_busy))
                    {
                        var sid = slot.Id;
                        var items = g.Bench.Select(b => (b.PlayerName + (b.CharacterId == _me ? " (you)" : "") + "  · bench", (string?)b.CharacterId, true))
                            .Concat(g.Roster.Where(s => s.Player != null).Select(s => ($"{s.Player!.PlayerName}  · move from {Position.GetValueOrDefault(s.Id, s.Id)}", (string?)s.Player!.CharacterId, true)));
                        OpenPicker($"Assign {pos}", items, v => { if (v != null) Act(g.Id, B("assign", ("slotId", sid), ("characterId", v))); });
                    }
                }
                else Ui.Text(dl, new Vector2(nx, mid - LH / 2), "Open", Theme.Text3);
                cy += rowH + rgap;
            }
            return y + h;
        }

        private float DrawBench(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2, rowH = BtnH + S(8), rgap = S(6);
            int rows = Math.Max(1, g.Bench.Count);
            var hint = lead && g.Roster.Any(s => s.Player != null && s.Player.CharacterId != g.CreatorId)
                ? Wrap("To remove a roster player from the static, move them to the bench first.", iw, 0.82f) : new List<string>();
            float h = Pad + CapsH + S(12) + rows * (rowH + rgap) + (lead ? FieldH + S(10) : 0) + BtnH + (hint.Count > 0 ? S(8) + hint.Count * LHs(0.82f) : 0) + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            Title(dl, cx, cy, "Bench", g.Bench.Count.ToString());
            cy += CapsH + S(12);
            if (g.Bench.Count == 0)
            {
                Ui.Text(dl, new Vector2(cx, cy + (rowH - LHs(0.88f)) / 2), Fit($"Nobody on the bench. Share the code {g.Id} or the guest link.", iw, 0.88f), Theme.Text3, 0.88f);
                cy += rowH + rgap;
            }
            foreach (var b in g.Bench)
            {
                var rp = new Vector2(cx, cy);
                bool mine = b.CharacterId == _me;
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), (mine ? Ui.Accent.With(0.1f) : Theme.Night2.With(0.55f)).U32(), S(6));
                float mid = rp.Y + rowH / 2, rx = rp.X + iw - S(6);
                if (lead && b.CharacterId != g.CreatorId)
                {
                    if (_confirmKick == b.CharacterId)
                    {
                        float nw = BtnW("Keep", null), yw = BtnW("Remove", null), bh = rowH - S(8);
                        rx -= nw;
                        if (Ui.Button("rd_kick_no" + b.CharacterId, "Keep", new Vector2(rx, mid - bh / 2), new Vector2(nw, bh))) _confirmKick = "";
                        rx -= yw + S(4);
                        if (Ui.Button("rd_kick_yes" + b.CharacterId, "Remove", new Vector2(rx, mid - bh / 2), new Vector2(yw, bh), Kind.Danger))
                        { _confirmKick = ""; Act(g.Id, B("kick", ("characterId", b.CharacterId))); }
                    }
                    else
                    {
                        float bs = rowH - S(8);
                        rx -= bs;
                        if (Ui.IconButton("rd_kick_" + b.CharacterId, FontAwesomeIcon.UserMinus, new Vector2(rx, mid - bs / 2), bs, "Remove from the static", Theme.Text3)) _confirmKick = b.CharacterId;
                    }
                    rx -= S(6);
                }
                string name = b.PlayerName + (mine ? "  (you)" : "");
                float room = rx - rp.X - S(12);
                string fit = Fit(name, room * 0.7f);
                Ui.Text(dl, new Vector2(rp.X + S(10), mid - LH / 2), fit, Theme.Text);
                float bx = rp.X + S(10) + Ui.TextSize(fit).X + S(8);
                if (b.World is { Length: > 0 } world)
                {
                    var wf = Fit(world, rx - bx - S(40), 0.8f);
                    Ui.Text(dl, new Vector2(bx, mid - LHs(0.8f) / 2), wf, Theme.Text3, 0.8f);
                    bx += Ui.TextSize(wf, 0.8f).X + S(8);
                }
                Badges(dl, g, b, ref bx, mid - LHs(0.75f) / 2);
                cy += rowH + rgap;
            }
            if (lead)
            {
                float ab = FieldH;
                bool add = Input("##rd_addname", "Add a player by name", ref _addName, 40, cx, cy, iw - ab - S(6));
                bool can = _addName.Trim().Length > 0 && !_busy;
                if (Ui.IconButton("rd_addname_go", FontAwesomeIcon.UserPlus, new Vector2(cx + iw - ab, cy), ab,
                        "Add someone without an Aetherhold login. They pick this name through the guest link.", can ? Theme.Gold : Theme.Text3)) add = true;
                if (add && can) Act(g.Id, B("addMember", ("name", _addName.Trim())), () => _addName = "");
                cy += FieldH + S(10);
            }
            float gw = BtnW("Copy guest link", FontAwesomeIcon.Link);
            if (g.GuestOpen)
            {
                if (Ui.Button("rd_guest2", "Copy guest link", new Vector2(cx, cy), new Vector2(Math.Min(gw, iw), BtnH), Kind.Ghost, FontAwesomeIcon.Link))
                    Copy($"{HubClient.BaseUrl}/raid/{g.Id}", "Guest link copied");
                Ui.Tooltip("Players without a login open this link, pick their name, and answer sessions, report absences and track their own gear");
            }
            else Ui.Text(dl, new Vector2(cx, cy + (BtnH - LHs(0.85f)) / 2), "The guest link is off (Settings).", Theme.Text3, 0.85f);
            cy += BtnH;
            if (hint.Count > 0)
            {
                cy += S(8);
                foreach (var l in hint) { Ui.Text(dl, new Vector2(cx, cy), l, Theme.Text3, 0.82f); cy += LHs(0.82f); }
            }
            return y + h;
        }

        // ---- strategy, notes, settings: read here, edit on the website ---------------------------------------------
        private float WebPanel(float x, float y, float w, string title, string text)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2;
            var lines = Wrap(text, iw, 0.9f);
            float h = Pad + CapsH + S(10) + lines.Count * LHs(0.9f) + S(12) + BtnH + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            Title(dl, x + Pad, y + Pad, title);
            float cy = y + Pad + CapsH + S(10);
            foreach (var l in lines) { Ui.Text(dl, new Vector2(x + Pad, cy), l, Theme.Text2, 0.9f); cy += LHs(0.9f); }
            cy += S(12);
            float bw = BtnW("Open on the website", FontAwesomeIcon.ExternalLinkAlt);
            if (Ui.Button("rd_webp_" + title, "Open on the website", new Vector2(x + Pad, cy), new Vector2(Math.Min(bw, iw), BtnH), Kind.Gold, FontAwesomeIcon.ExternalLinkAlt)) OpenWebsite();
            return y + h;
        }

        private float DrawStrategy(Group g, float x, float y, float w)
        {
            var boards = g.Boards ?? new();
            y = WebPanel(x, y, w, "Strategy boards", boards.Count == 0
                ? "No strategy boards yet. Boards (arena, waymarks, players, AoEs, step by step) are drawn on the website."
                : "Boards are drawn and played step by step on the website; the button opens this static's Strategy tab.") + S(12);
            if (boards.Count == 0) return y;
            var dl = Ui.Dl;
            float iw = w - Pad * 2, rowH = LH + S(14), rgap = S(6);
            float h = Pad + boards.Count * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cy = y + Pad;
            foreach (var b in boards)
            {
                var rp = new Vector2(x + Pad, cy);
                bool click = Ui.Hit("rd_bd_" + b.Id, rp, new Vector2(iw, rowH), out float hv);
                Ui.Tooltip("Open on the website");
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(0.55f + 0.3f * hv).U32(), S(6));
                int steps = b.Steps?.Count ?? 0;
                string right = $"{steps} step{(steps == 1 ? "" : "s")}";
                float rw = Ui.TextSize(right, 0.85f).X;
                string fight = b.Fight.Length > 0 ? b.Fight + "  ·  " : "";
                Ui.Text(dl, rp + S(10, 7), Fit(fight + Or(b.Name, "Board"), iw - rw - S(36)), Theme.Text);
                Ui.Text(dl, new Vector2(rp.X + iw - rw - S(10), rp.Y + (rowH - LHs(0.85f)) / 2), right, Theme.Text3, 0.85f);
                if (click) OpenWebsite();
                cy += rowH + rgap;
            }
            return y + h;
        }

        private static readonly Regex MdImage = new(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
        private static readonly Regex MdLink = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);

        /// <summary>Markdown as plain text: headings, bold/italic marks and links stripped, images shown as [image].</summary>
        private static string PlainMd(string md)
        {
            var s = MdImage.Replace(md ?? "", m => m.Groups[1].Value.Length > 0 ? $"[image: {m.Groups[1].Value}]" : "[image]");
            s = MdLink.Replace(s, "$1");
            var lines = s.Replace("\r", "").Split('\n').Select(l =>
            {
                var t = l.TrimStart();
                while (t.StartsWith("#")) t = t[1..];
                if (t.StartsWith("- ") || t.StartsWith("* ")) t = "- " + t[2..];
                return t.Replace("**", "").Replace("__", "").Replace("`", "");
            });
            return string.Join("\n", lines).Trim();
        }

        private float DrawNotes(Group g, float x, float y, float w)
        {
            var notes = (g.Notes ?? new()).OrderByDescending(n => n.Pinned).ThenByDescending(n => n.UpdatedAt).ToList();
            y = WebPanel(x, y, w, "Notes", notes.Count == 0
                ? "No notes yet. Notes (Markdown with images) are written on the website."
                : "Read them here; click a note to open or close it. Writing and images are on the website.") + S(12);
            var dl = Ui.Dl;
            float iw = w - Pad * 2;
            foreach (var n in notes)
            {
                bool open = _openNotes.Contains(n.Id);
                var body = open ? Wrap(PlainMd(n.Body), iw, 0.9f) : new List<string>();
                float headH = LH + LHs(0.8f) + S(4);
                float h = Pad + headH + (open ? S(10) + body.Count * LHs(0.9f) : 0) + Pad;
                Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, n.Pinned ? Theme.GoldLine : null, 10f);
                bool click = Ui.Hit("rd_note_" + n.Id, new Vector2(x, y), new Vector2(w, Pad + headH), out float hv);
                var ic = open ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
                Ui.Icon(dl, new Vector2(x + Pad, y + Pad + (LH - Ui.IconSize(ic, 0.75f).Y) / 2), ic, UIHelper.LerpColor(Theme.Text3, Theme.Text, hv), 0.75f);
                float tx = x + Pad + S(20);
                if (n.Pinned) { Ui.Icon(dl, new Vector2(tx, y + Pad + (LH - Ui.IconSize(FontAwesomeIcon.Thumbtack, 0.75f).Y) / 2), FontAwesomeIcon.Thumbtack, Theme.Gold, 0.75f); tx += S(18); }
                Ui.Text(dl, new Vector2(tx, y + Pad), Fit(Or(n.Title, "Untitled"), x + w - Pad - tx), Theme.Text);
                string meta = (n.Fight is { Length: > 0 } f ? f + "  ·  " : "") + (n.UpdatedAt > 0 ? "updated " + Ui.Ago(FromMs(n.UpdatedAt).ToLocalTime()) : "");
                Ui.Text(dl, new Vector2(tx, y + Pad + LH + S(2)), Fit(meta, x + w - Pad - tx, 0.8f), Theme.Text3, 0.8f);
                if (click) { if (!_openNotes.Remove(n.Id)) _openNotes.Add(n.Id); }
                if (open)
                {
                    float cy = y + Pad + headH + S(10);
                    foreach (var l in body) { Ui.Text(dl, new Vector2(x + Pad, cy), l, l.StartsWith("[image") ? Theme.Text3 : Theme.Text2, 0.9f); cy += LHs(0.9f); }
                }
                y += h + S(8);
            }
            return y;
        }

        private float DrawSettingsTab(Group g, float x, float y, float w)
        {
            var t = g.Tier;
            string tier = t == null || (t.Name.Length == 0 && t.Start.Length == 0) ? "not set" : $"{Or(t.Name, "unnamed")}{(t.Start.Length > 0 ? $", from {t.Start}" : "")}";
            string text = $"Time zone: {Or(g.Timezone, "Europe/Berlin")}.  Guest link: {(g.GuestOpen ? "on" : "off")}.  Tier: {tier}.  Floors: {string.Join(", ", Floors(g))}.  "
                        + $"Loot priority: {(t?.Priority == "even" ? "most need first" : "by role, then most need")}.  Leads: {(g.Leads?.Count > 0 ? string.Join(", ", g.Leads.Select(id => NameOf(g, id))) : "none")}.\n\n"
                        + "Change these (rename, time zone, guest link, new code, leads, tier) on the website.";
            return WebPanel(x, y, w, "Static settings", text);
        }

        // =============================================================================================================
        // Dialog frame helpers and the generic picker (job, assign, sub, who, category)
        // =============================================================================================================
        private enum Dlg { None, Occ, Absence, Session, SlotMenu }
        private Dlg _dlg;
        private int _dlgFrame;
        private float _dlgScroll;

        private sealed class Picker
        {
            public string Title = "";
            public List<(string Label, string? Value, bool On)> Items = new();
            public Action<string?> Pick = _ => { };
            public string? Current;
            public int Frame;
            public float Scroll;
        }
        private Picker? _picker;

        private void OpenDialog(Dlg d) { _dlg = d; _dlgFrame = ImGui.GetFrameCount(); _dlgScroll = 0; }

        private void OpenPicker(string title, IEnumerable<(string Label, string? Value, bool On)> items, Action<string?> pick, string? current = null)
            => _picker = new Picker { Title = title, Items = items.ToList(), Pick = pick, Current = current, Frame = ImGui.GetFrameCount() };

        private static (Vector2 Wp, Vector2 Ws) Scrim()
        {
            var wp = ImGui.GetWindowPos();
            var ws = ImGui.GetWindowSize();
            Ui.Dl.AddRectFilled(wp, wp + ws, Theme.Night0.With(0.55f).U32());
            return (wp, ws);
        }

        private static bool ClickedOutside(int openedFrame, Vector2 a, Vector2 b)
            => ImGui.GetFrameCount() > openedFrame + 1 && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
               && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && !ImGui.IsMouseHoveringRect(a, b, false);

        /// <summary>Dialog panel centred in the window; returns its corners and draws the title row with a close button.</summary>
        private static (Vector2 P, Vector2 Pe, float Y, bool Close) DialogFrame(string id, string title, string? aside, float width, float height, Vector4? line = null)
        {
            var (wp, ws) = Scrim();
            var dl = Ui.Dl;
            float dw = Math.Min(width, ws.X - S(32));
            float dh = Math.Min(height, ws.Y - S(32));
            var p = wp + new Vector2((ws.X - dw) / 2, Math.Max(S(16), (ws.Y - dh) / 2));
            var pe = p + new Vector2(dw, dh);
            Ui.Panel(dl, p, pe, Theme.Night1.With(0.98f), line ?? Theme.Crystal.With(0.4f), 12f);
            float x = p.X + S(18), y = p.Y + S(16);
            float cb = S(28);
            bool close = Ui.IconButton(id + "_x", FontAwesomeIcon.Times, new Vector2(pe.X - S(18) - cb, y - S(3)), cb, "Close");
            Gem(dl, new Vector2(x + S(5), y + LHs(1.1f) / 2), S(5), Theme.Gold);
            float tw = Ui.TextSize(title, 1.1f).X;
            float room = pe.X - S(18) - cb - S(8) - (x + S(18));
            Ui.Text(dl, new Vector2(x + S(18), y), Fit(title, room, 1.1f), Theme.Text, 1.1f);
            if (aside != null && tw + S(12) < room)
                Ui.Text(dl, new Vector2(x + S(18) + tw + S(12), y + (LHs(1.1f) - LHs(0.85f)) / 2), Fit(aside, room - tw - S(12), 0.85f), Theme.Text3, 0.85f);
            return (p, pe, y + LHs(1.1f) + S(14), close);
        }

        /// <summary>Mouse-wheel scrolling for a dialog body; returns the clamped offset.</summary>
        private static float WheelScroll(ref float scroll, Vector2 a, Vector2 b, float content)
        {
            float max = Math.Max(0, content - (b.Y - a.Y));
            if (max > 0 && ImGui.IsMouseHoveringRect(a, b, false)) scroll -= ImGui.GetIO().MouseWheel * LH * 3;
            scroll = Math.Clamp(scroll, 0, max);
            if (max > 0)
            {
                float vh = b.Y - a.Y, bh = vh * vh / content, by = a.Y + (vh - bh) * (scroll / max);
                Ui.Dl.AddRectFilled(new Vector2(b.X - S(3), by), new Vector2(b.X, by + bh), Theme.Text3.With(0.5f).U32(), S(2));
            }
            return scroll;
        }

        private void DrawPicker()
        {
            var pk = _picker!;
            float rowH = BtnH, rgap = S(4);
            float content = pk.Items.Count * (rowH + rgap);
            float wd = Math.Max(S(300), pk.Items.Count == 0 ? S(300) : pk.Items.Max(i => Ui.TextSize(i.Label).X) + S(70));
            var (p, pe, y, close) = DialogFrame("rd_pk", pk.Title, null, Math.Min(wd, S(520)), S(16) + LHs(1.1f) + S(14) + content + S(16), Theme.Gold.With(0.45f));
            var dl = Ui.Dl;
            float x = p.X + S(18), iw = pe.X - p.X - S(36);
            var a = new Vector2(p.X, y);
            var b = new Vector2(pe.X - S(6), pe.Y - S(12));
            float off = WheelScroll(ref pk.Scroll, a, b, content);
            ImGui.PushClipRect(a, b, true);
            float cy = y - off;
            foreach (var (label, value, on) in pk.Items)
            {
                if (cy + rowH > a.Y && cy < b.Y)
                {
                    bool cur = value == pk.Current;
                    if (Pill("rd_pk_" + label + value, label, cur ? FontAwesomeIcon.Check : null, new Vector2(x, cy), new Vector2(iw, rowH), Theme.Gold, cur, on && cy >= a.Y - S(2) && cy + rowH <= b.Y + S(2)))
                    { _picker = null; pk.Pick(value); ImGui.PopClipRect(); return; }
                }
                cy += rowH + rgap;
            }
            ImGui.PopClipRect();
            if (close || ClickedOutside(pk.Frame, p, pe)) _picker = null;
        }

        private void DrawDialog(Group g)
        {
            switch (_dlg)
            {
                case Dlg.Occ: DrawOccDialog(g); break;
                case Dlg.Absence: DrawAbsenceForm(g); break;
                case Dlg.Session: DrawSessionForm(g); break;
                case Dlg.SlotMenu: DrawSlotMenu(g); break;
            }
        }
    }
}
