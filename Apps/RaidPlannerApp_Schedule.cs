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
    // Schedule tab (website ScheduleView.tsx): next session with my RSVP, calendar / list (prefs.raids.cal), absences,
    // weekly nights; the session dialog (RSVP per player, subs, cancel a date), absence report, one-off session.
    public partial class RaidPlannerApp
    {
        private DateTime _month = new(DateTime.Now.Year, DateTime.Now.Month, 1);
        private Occ? _occ;
        private bool _confirmRemoveSession;

        // absence / session forms
        private string _fWho = "", _fFrom = "", _fTo = "", _fReason = "";
        private string _fTitle = "", _fDate = "", _fHours = "3", _fCat = "raid", _fNote = "", _fErr = "";
        private bool _fFocus;

        private static readonly string[] DateFormats = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-ddTHH:mm", "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "d.M.yyyy HH:mm" };
        private static readonly string[] DayFormats = { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy" };

        private static bool ParseDay(string s, out DateOnly d) => DateOnly.TryParseExact(s.Trim(), DayFormats, Inv, DateTimeStyles.None, out d);

        private void OpenOcc(Occ o) { _occ = o; _confirmRemoveSession = false; OpenDialog(Dlg.Occ); }

        /// <summary>Coming / Maybe / Can't for one member (website RsvpButtons). Returns the width used.</summary>
        private float RsvpButtons(Group g, Occ o, string mid, float x, float y, bool compact, string idp, float h = 0)
        {
            if (h <= 0) h = BtnH;
            var cur = RsvpOf(g, o, mid);
            bool can = mid.Length > 0 && (IsManager(g, _me) || mid == _me) && !_busy && !o.Cancelled;
            float cx = x;
            foreach (var (s, label, icon) in new[] { ("yes", "Coming", FontAwesomeIcon.Check), ("maybe", "Maybe", FontAwesomeIcon.Question), ("no", "Can't", FontAwesomeIcon.Times) })
            {
                float w = compact ? h + S(4) : PillW(label, icon);
                string tip = cur.Away != null && s == "no" && !cur.Explicit ? $"Absence report: {Or(cur.Away.Reason, "away")}" : label;
                if (Pill($"{idp}_{s}", compact ? "" : label, icon, new Vector2(cx, y), new Vector2(w, h), RsvpColor(s), cur.S == s, can, tip))
                    Act(g.Id, B("setRsvp", ("key", o.Key), ("memberId", mid), ("s", cur.S == s && cur.Explicit ? null : s)));
                cx += w + S(4);
            }
            return cx - S(4) - x;
        }

        private static float RsvpW(bool compact, float h = 0)
        {
            if (h <= 0) h = BtnH;
            return compact ? (h + S(4)) * 3 + S(8) : PillW("Coming", FontAwesomeIcon.Check) + PillW("Maybe", FontAwesomeIcon.Question) + PillW("Can't", FontAwesomeIcon.Times) + S(8);
        }

        private static string ReadyTip((int Ready, int Yes, int Maybe, int No, int Subs) r)
            => $"{r.Yes} coming, {r.Maybe} maybe, {r.No} can't{(r.Subs > 0 ? $", {r.Subs} sub" : "")}";

        // =============================================================================================================
        private float DrawSchedule(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            float gap = S(12);
            var now = DateTime.UtcNow;
            var upcoming = Occurrences(g, now.AddHours(-4), now.AddDays(60));
            var next = upcoming.FirstOrDefault(o => !o.Cancelled && o.End > now);

            // ---- next session ----
            if (next != null)
            {
                var r = Readiness(g, next);
                bool stack = w < S(700);
                float rsW = _me.Length > 0 ? RsvpW(false) : 0;
                float infoH = CapsH + S(6) + LHs(1.2f) + S(4) + LH + (next.Note is { Length: > 0 } ? LHs(0.88f) + S(2) : 0);
                float h = Pad * 2 + (stack ? infoH + S(10) + BtnH : Math.Max(infoH, BtnH * 2 + S(8)));
                Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), Theme.Night2.With(0.85f), CatColor(next.Category).With(0.45f), 12f);
                float cx = x + Pad, cy = y + Pad;
                Ui.Caps(dl, new Vector2(cx, cy), "Next " + (next.Category == "raid" ? "raid" : next.Category), CatColor(next.Category));
                cy += CapsH + S(6);
                float tw = stack ? w - Pad * 2 : w - Pad * 3 - rsW;
                string title = Fit(next.Title, tw, 1.2f);
                bool tclick = Ui.Hit("rd_next_t", new Vector2(cx, cy), Vector2.Max(Ui.TextSize(title, 1.2f), S(4, 4)), out float th);
                Ui.Tooltip("Open this session");
                Ui.Text(dl, new Vector2(cx, cy), title, UIHelper.LerpColor(Theme.Text, Theme.Gold, th), 1.2f);
                if (tclick) OpenOcc(next);
                cy += LHs(1.2f) + S(4);
                string when = $"{Day(next.LocalStart)}, {Time(next.LocalStart)}-{Time(next.LocalEnd)}";
                Ui.Text(dl, new Vector2(cx, cy), when, Theme.Text2);
                Ui.Text(dl, new Vector2(cx + Ui.TextSize(when).X + S(8), cy), $"({RelDay(next.LocalStart)})", Theme.Text3);
                cy += LH;
                if (next.Note is { Length: > 0 } note) { Ui.Text(dl, new Vector2(cx, cy + S(2)), Fit(note, tw, 0.88f), Theme.Text3, 0.88f); }

                string ready = $"{r.Ready}/8 ready";
                var rc = r.Ready >= 8 ? Theme.Live : Theme.Warn;
                if (stack)
                {
                    float by = y + h - Pad - BtnH;
                    Ui.Chip(dl, new Vector2(cx, by + (BtnH - LHs(0.82f) - S(6)) / 2), ready, rc, out var cs);
                    ImGui.SetCursorScreenPos(new Vector2(cx, by)); ImGui.Dummy(cs); Ui.Tooltip(ReadyTip(r));
                    if (_me.Length > 0) RsvpButtons(g, next, _me, cx + cs.X + S(12), by, false, "rd_nx");
                }
                else
                {
                    float rx = x + w - Pad - rsW;
                    float cw = Ui.TextSize(ready, 0.82f).X + S(14);
                    Ui.Chip(dl, new Vector2(x + w - Pad - cw, y + Pad), ready, rc, out var cs);
                    ImGui.SetCursorScreenPos(new Vector2(x + w - Pad - cw, y + Pad)); ImGui.Dummy(cs); Ui.Tooltip(ReadyTip(r));
                    if (_me.Length > 0) RsvpButtons(g, next, _me, rx + S(8), y + h - Pad - BtnH, false, "rd_nx");
                }
                y += h + gap;
            }

            // ---- bar: view, month, actions ----
            var viewOpts = new[] { ("calendar", "Calendar"), ("list", "List") };
            float segW = SegW(viewOpts);
            float navW = _cal == "calendar" ? BtnH * 2 + S(8) + Ui.TextSize("September 2026").X + S(16) + BtnW("Today", null) + S(8) : 0;
            var acts = new List<(string Id, string Label, FontAwesomeIcon Icon, string Tip, Action Do, Kind K)>();
            if (_me.Length > 0) acts.Add(("rd_abs", "Report absence", FontAwesomeIcon.CalendarTimes, "Tell the group you are away for some days", () => OpenAbsence(g), Kind.Ghost));
            if (lead) acts.Add(("rd_addses", "Session", FontAwesomeIcon.Plus, "Add a single session (weekly nights: on the website)", () => OpenSessionForm(), Kind.Gold));
            float actW = acts.Sum(a => BtnW(a.Label, a.Icon) + S(6)) + (BtnH + S(6)) * 2;
            bool oneRow = segW + S(12) + navW + S(12) + actW <= w;

            float bx = x;
            var pickedView = Seg("rd_view", bx, y, viewOpts, _cal, out _);
            if (pickedView != null) SavePrefs(cal: pickedView);
            bx += segW + S(12);
            if (_cal == "calendar")
            {
                if (Ui.IconButton("rd_m_prev", FontAwesomeIcon.ChevronLeft, new Vector2(bx, y), BtnH, "Previous month")) _month = _month.AddMonths(-1);
                bx += BtnH + S(4);
                string ml = _month.ToString("MMMM yyyy", Inv);
                float mlw = Ui.TextSize("September 2026").X + S(8);
                Ui.Text(dl, new Vector2(bx + (mlw - Ui.TextSize(ml).X) / 2, y + (BtnH - LH) / 2), ml, Theme.Text);
                bx += mlw + S(4);
                if (Ui.IconButton("rd_m_next", FontAwesomeIcon.ChevronRight, new Vector2(bx, y), BtnH, "Next month")) _month = _month.AddMonths(1);
                bx += BtnH + S(8);
                float tdw = BtnW("Today", null);
                if (Ui.Button("rd_m_today", "Today", new Vector2(bx, y), new Vector2(tdw, BtnH))) _month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            }
            float ay = oneRow ? y : y + BtnH + S(8);
            float ax = x + w;
            foreach (var a in Enumerable.Reverse(acts))
            {
                float aw = BtnW(a.Label, a.Icon);
                ax -= aw;
                if (Ui.Button(a.Id, a.Label, new Vector2(ax, ay), new Vector2(aw, BtnH), a.K, a.Icon)) a.Do();
                Ui.Tooltip(a.Tip);
                ax -= S(6);
            }
            ax -= BtnH;
            if (Ui.IconButton("rd_ics", FontAwesomeIcon.Link, new Vector2(ax, ay), BtnH, "Copy a calendar subscription link (Google, Apple, Outlook)"))
                Copy($"{HubClient.BaseUrl}/api/raids/public/{g.Id}/calendar.ics", "Calendar link copied: add it in Google, Apple or Outlook calendar as a subscription");
            ax -= BtnH + S(6);
            if (Ui.IconButton("rd_avail", FontAwesomeIcon.Th, new Vector2(ax, ay), BtnH, "Availability grid and weekly raid nights: on the website")) OpenWebsite();
            y = ay + BtnH + gap;

            y = _cal == "calendar" ? DrawCalendar(g, x, y, w) : DrawAgenda(g, x, y, w, upcoming, lead);
            y += gap;

            // ---- foot: away + weekly nights ----
            bool two = w >= S(720);
            float fw = two ? (w - gap) / 2 : w;
            float y1 = DrawAway(g, x, y, fw, lead);
            float y2 = DrawRules(g, two ? x + fw + gap : x, two ? y : y1 + gap, fw);
            return Math.Max(y1, y2);
        }

        // ---- month calendar ----------------------------------------------------------------------------------------
        private float DrawCalendar(Group g, float x, float y, float w)
        {
            var dl = Ui.Dl;
            float cw = w / 7, chipH = LHs(0.8f) + S(6), headH = LHs(0.8f) + S(8), numH = LHs(0.85f) + S(6);
            var gridStart = _month.AddDays(-(((int)_month.DayOfWeek + 6) % 7)); // Monday first
            var occs = Occurrences(g, gridStart.AddDays(-1).ToUniversalTime(), gridStart.AddDays(44).ToUniversalTime());
            var byDay = occs.GroupBy(o => o.LocalStart.Date).ToDictionary(k => k.Key, v => v.ToList());
            var today = DateTime.Now.Date;
            var now = DateTime.UtcNow;
            bool compact = cw < S(92);

            string[] wd = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            for (int i = 0; i < 7; i++) Ui.Text(dl, new Vector2(x + i * cw + S(6), y), wd[i], Theme.Text3, 0.8f);
            float cy = y + headH;
            for (int week = 0; week < 6; week++)
            {
                int most = 0;
                for (int d = 0; d < 7; d++) most = Math.Max(most, byDay.TryGetValue(gridStart.AddDays(week * 7 + d).Date, out var l) ? l.Count : 0);
                float rowH = Math.Max(S(64), numH + most * (chipH + S(3)) + S(6));
                for (int d = 0; d < 7; d++)
                {
                    var day = gridStart.AddDays(week * 7 + d).Date;
                    var a = new Vector2(x + d * cw + S(1), cy + S(1));
                    var b = new Vector2(x + (d + 1) * cw - S(1), cy + rowH - S(1));
                    bool other = day.Month != _month.Month;
                    dl.AddRectFilled(a, b, (day == today ? Ui.Accent.With(0.1f) : Theme.Night2.With(other ? 0.25f : 0.55f)).U32(), S(4));
                    if (day == today) dl.AddRect(a, b, Ui.Accent.With(0.5f).U32(), S(4), 0, S(1));
                    Ui.Text(dl, a + S(6, 4), day.Day.ToString(), other ? Theme.Text3 : Theme.Text2, 0.85f);
                    var ds = day.ToString("yyyy-MM-dd", Inv);
                    var away = (g.AbsenceReports ?? new()).Where(r => string.CompareOrdinal(r.From, ds) <= 0 && string.CompareOrdinal(r.To, ds) >= 0).ToList();
                    if (away.Count > 0)
                    {
                        string at = away.Count.ToString();
                        float aw = Ui.IconSize(FontAwesomeIcon.CalendarTimes, 0.65f).X + S(4) + Ui.TextSize(at, 0.75f).X;
                        var ap = new Vector2(b.X - aw - S(6), a.Y + S(5));
                        Ui.Icon(dl, ap, FontAwesomeIcon.CalendarTimes, Theme.Warn, 0.65f);
                        Ui.Text(dl, ap + new Vector2(aw - Ui.TextSize(at, 0.75f).X, -S(1)), at, Theme.Warn, 0.75f);
                        ImGui.SetCursorScreenPos(ap); ImGui.Dummy(new Vector2(aw, LHs(0.75f)));
                        Ui.Tooltip("Away: " + string.Join(", ", away.Select(r => NameOf(g, r.MemberId))));
                    }
                    if (!byDay.TryGetValue(day, out var list)) continue;
                    float oy = a.Y + numH;
                    foreach (var o in list)
                    {
                        var cp = new Vector2(a.X + S(3), oy);
                        var csz = new Vector2(b.X - a.X - S(6), chipH);
                        var r = Readiness(g, o);
                        bool click = Ui.Hit("rd_occ_" + o.Key, cp, csz, out float hv);
                        Ui.Tooltip($"{o.Title}{(o.Cancelled ? " (cancelled)" : "")} · {Time(o.LocalStart)} · {r.Ready}/8 ready{(o.Note is { Length: > 0 } n ? " · " + n : "")}");
                        var col = CatColor(o.Category);
                        float alpha = o.Cancelled || o.End < now ? 0.5f : 1f;
                        dl.AddRectFilled(cp, cp + csz, col.With((0.18f + 0.15f * hv) * alpha).U32(), S(4));
                        dl.AddRectFilled(cp, cp + new Vector2(S(3), csz.Y), col.With(alpha).U32(), S(2));
                        float tx = cp.X + S(6), right = cp.X + csz.X - S(4);
                        var mine = _me.Length > 0 ? RsvpOf(g, o, _me).S : null;
                        if (mine != null) { dl.AddCircleFilled(new Vector2(right - S(3), cp.Y + csz.Y / 2), S(3.5f), RsvpColor(mine).U32()); right -= S(10); }
                        if (!o.Cancelled && !compact)
                        {
                            string cnt = r.Ready.ToString();
                            float cwd = Ui.TextSize(cnt, 0.75f).X;
                            Ui.Text(dl, new Vector2(right - cwd, cp.Y + (csz.Y - LHs(0.75f)) / 2), cnt, r.Ready >= 8 ? Theme.Live : Theme.Warn, 0.75f);
                            right -= cwd + S(4);
                        }
                        string label = compact ? Time(o.LocalStart) : Time(o.LocalStart) + " " + o.Title;
                        Ui.Text(dl, new Vector2(tx, cp.Y + S(3)), Fit(label, right - tx, 0.8f), (o.Cancelled ? Theme.Text3 : Theme.Text).With(alpha), 0.8f);
                        if (o.Cancelled) dl.AddLine(new Vector2(tx, cp.Y + csz.Y / 2), new Vector2(right, cp.Y + csz.Y / 2), Theme.Text3.U32(), S(1));
                        if (click) OpenOcc(o);
                        oy += chipH + S(3);
                    }
                }
                cy += rowH;
            }
            return cy;
        }

        // ---- list (next 60 days) -----------------------------------------------------------------------------------
        private float DrawAgenda(Group g, float x, float y, float w, List<Occ> upcoming, bool lead)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2, rowH = BtnH + S(10), rgap = S(6);
            int rows = Math.Max(1, upcoming.Count);
            float h = Pad + rows * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            if (upcoming.Count == 0)
            {
                Ui.Text(dl, new Vector2(cx, cy + (rowH - LH) / 2), lead ? "Nothing planned. Add a weekly raid night (website) or a single session." : "Nothing planned yet.", Theme.Text3);
                return y + h;
            }
            float rsW = RsvpW(true, rowH - S(10));
            foreach (var o in upcoming)
            {
                var rp = new Vector2(cx, cy);
                var col = CatColor(o.Category);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(o.Cancelled ? 0.3f : 0.55f).U32(), S(6));
                dl.AddRectFilled(rp, rp + new Vector2(S(3), rowH), col.U32(), S(2));
                float mid = rp.Y + rowH / 2, rx = rp.X + iw - S(8);
                if (_me.Length > 0 && !o.Cancelled)
                {
                    rx -= rsW - S(8);
                    RsvpButtons(g, o, _me, rx, mid - (rowH - S(10)) / 2, true, "rd_ag_" + o.Key, rowH - S(10));
                    rx -= S(10);
                }
                if (!o.Cancelled)
                {
                    var r = Readiness(g, o);
                    string t = $"{r.Ready}/8";
                    float cw = Ui.TextSize(t, 0.82f).X + S(14);
                    rx -= cw;
                    Ui.Chip(dl, new Vector2(rx, mid - (LHs(0.82f) + S(6)) / 2), t, r.Ready >= 8 ? Theme.Live : Theme.Warn, out var cs);
                    ImGui.SetCursorScreenPos(new Vector2(rx, mid - cs.Y / 2)); ImGui.Dummy(cs); Ui.Tooltip(ReadyTip(r));
                    rx -= S(10);
                }
                string when = $"{Day(o.LocalStart)}  {Time(o.LocalStart)}";
                float ww = Math.Max(Ui.TextSize("Wed, Sep 30  20:00").X, Ui.TextSize(when).X) + S(10);
                var hitA = new Vector2(rp.X + S(8), rp.Y);
                bool click = Ui.Hit("rd_ag_open_" + o.Key, hitA, new Vector2(Math.Max(S(10), rx - hitA.X), rowH), out float hv);
                Ui.Tooltip("Open this session");
                Ui.Text(dl, new Vector2(rp.X + S(12), mid - LH / 2), when, UIHelper.LerpColor(Theme.Text, Theme.Gold, hv));
                string title = o.Title + (o.Cancelled ? " · cancelled" : "") + (o.Note is { Length: > 0 } n ? " · " + n : "");
                float tx = rp.X + S(12) + ww;
                Ui.Text(dl, new Vector2(tx, mid - LH / 2), Fit(title, rx - tx), o.Cancelled ? Theme.Text3 : Theme.Text2);
                if (click) OpenOcc(o);
                cy += rowH + rgap;
            }
            return y + h;
        }

        // ---- away and weekly nights --------------------------------------------------------------------------------
        private float DrawAway(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            var today = DateTime.Now.ToString("yyyy-MM-dd", Inv);
            var away = (g.AbsenceReports ?? new()).Where(a => string.CompareOrdinal(a.To, today) >= 0).Take(30).ToList();
            float iw = w - Pad * 2, rowH = LH + S(12), rgap = S(4);
            float h = Pad + CapsH + S(12) + Math.Max(1, away.Count) * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            Title(dl, cx, cy, "Away", "absence reports");
            cy += CapsH + S(12);
            if (away.Count == 0) Ui.Text(dl, new Vector2(cx, cy + (rowH - LHs(0.9f)) / 2), "Nobody reported an absence.", Theme.Text3, 0.9f);
            foreach (var a in away)
            {
                var rp = new Vector2(cx, cy);
                float mid = rp.Y + rowH / 2, rx = rp.X + iw;
                if (lead || a.MemberId == _me)
                {
                    float bs = rowH - S(4);
                    rx -= bs;
                    if (Ui.IconButton("rd_away_x_" + a.Id, FontAwesomeIcon.TrashAlt, new Vector2(rx, mid - bs / 2), bs, "Remove this report", Theme.Text3) && !_busy)
                        Act(g.Id, B("removeAbsence", ("id", a.Id)));
                    rx -= S(6);
                }
                string name = NameOf(g, a.MemberId), dates = a.From == a.To ? a.From : $"{a.From} → {a.To}";
                float nw = Math.Min(Ui.TextSize(name).X, (rx - rp.X) * 0.35f);
                Ui.Text(dl, new Vector2(rp.X, mid - LH / 2), Fit(name, nw), a.MemberId == _me ? Theme.Gold : Theme.Text);
                float dx = rp.X + nw + S(10);
                Ui.Text(dl, new Vector2(dx, mid - LHs(0.85f) / 2), Fit(dates, rx - dx, 0.85f), Theme.Text2, 0.85f);
                float rx2 = dx + Ui.TextSize(dates, 0.85f).X + S(10);
                if (a.Reason is { Length: > 0 } reason && rx2 < rx - S(30)) Ui.Text(dl, new Vector2(rx2, mid - LHs(0.85f) / 2), Fit(reason, rx - rx2, 0.85f), Theme.Text3, 0.85f);
                cy += rowH + rgap;
            }
            return y + h;
        }

        private static readonly string[] WeekdayNames = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        private float DrawRules(Group g, float x, float y, float w)
        {
            var dl = Ui.Dl;
            var rules = g.Rules ?? new();
            bool bench = _me.Length > 0 && !g.Roster.Any(s => s.Player?.CharacterId == _me);
            float iw = w - Pad * 2, rowH = LH + S(12), rgap = S(4);
            float h = Pad + CapsH + S(12) + Math.Max(1, rules.Count) * (rowH + rgap) - rgap + (bench ? S(8) + LHs(0.85f) : 0) + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            Title(dl, cx, cy, "Weekly raid nights", Or(g.Timezone, "Europe/Berlin"));
            cy += CapsH + S(12);
            if (rules.Count == 0) Ui.Text(dl, new Vector2(cx, cy + (rowH - LHs(0.9f)) / 2), "No weekly nights yet.", Theme.Text3, 0.9f);
            foreach (var r in rules)
            {
                float mid = cy + rowH / 2;
                Ui.Icon(dl, new Vector2(cx, mid - Ui.IconSize(FontAwesomeIcon.Redo, 0.7f).Y / 2), FontAwesomeIcon.Redo, Theme.Text3, 0.7f);
                string day = r.Weekday is >= 0 and <= 6 ? WeekdayNames[r.Weekday] : "?";
                string main = $"{day} {r.Time} · {Math.Round(r.DurationMin / 60.0, 1).ToString(Inv)} h · {Or(r.Title, "Raid night")}";
                float mw = Ui.TextSize(main, 0.92f).X;
                Ui.Text(dl, new Vector2(cx + S(20), mid - LHs(0.92f) / 2), Fit(main, iw - S(20), 0.92f), Theme.Text, 0.92f);
                string range = $"from {r.From}" + (r.Until is { Length: > 0 } u ? $" until {u}" : "");
                float rx = cx + S(20) + mw + S(10);
                if (rx < cx + iw - S(40)) Ui.Text(dl, new Vector2(rx, mid - LHs(0.8f) / 2), Fit(range, cx + iw - rx, 0.8f), Theme.Text3, 0.8f);
                cy += rowH + rgap;
            }
            if (bench) Ui.Text(dl, new Vector2(cx, cy - rgap + S(8)), Fit("You are on the bench: answer the sessions if you can sub.", iw, 0.85f), Theme.Text3, 0.85f);
            return y + h;
        }

        // =============================================================================================================
        // Session dialog: everyone's answer, subs, cancel / restore one date, delete a one-off session
        // =============================================================================================================
        private void DrawOccDialog(Group g)
        {
            var o0 = _occ;
            if (o0 == null) { _dlg = Dlg.None; return; }
            // pick up changes (RSVPs, cancel) from the refreshed group
            var o = Occurrences(g, o0.Start.AddMinutes(-1), o0.Start.AddMinutes(1)).FirstOrDefault(x => x.Key == o0.Key) ?? o0;
            _occ = o;
            bool lead = IsManager(g, _me);
            var r = Readiness(g, o);
            var session = o.SessionId != null ? g.Schedule.FirstOrDefault(s => s.Id == o.SessionId) : null;

            float rowH = BtnH + S(8), rgap = S(4);
            int benchRows = g.Bench.Count;
            float bodyH = g.Roster.Count * (rowH + rgap) + (benchRows > 0 ? S(10) + CapsH + S(10) + benchRows * (rowH + rgap) : 0);
            float headH = LHs(0.82f) + S(6) + S(10) + (o.Note is { Length: > 0 } ? LHs(0.9f) + S(8) : 0) + (lead ? BtnH + S(10) : 0);
            float top = S(16) + LHs(1.1f) + S(14);
            var (p, pe, y, close) = DialogFrame("rd_occd", o.Title, $"{Day(o.LocalStart)}, {Time(o.LocalStart)}-{Time(o.LocalEnd)}", S(720), top + headH + bodyH + S(18), CatColor(o.Category).With(0.5f));
            var dl = Ui.Dl;
            float x = p.X + S(18), iw = pe.X - p.X - S(36);

            // chips
            Ui.Chip(dl, new Vector2(x, y), o.Category, CatColor(o.Category), out var c1);
            float chx = x + c1.X + S(8);
            if (o.Cancelled) Ui.Chip(dl, new Vector2(chx, y), "Cancelled", Theme.Bad, out _);
            else Ui.Chip(dl, new Vector2(chx, y), Fit($"{r.Ready}/8 ready · {r.Yes} yes · {r.Maybe} maybe · {r.No} can't{(r.Subs > 0 ? $" · {r.Subs} sub" : "")}", iw - c1.X - S(30), 0.82f), r.Ready >= 8 ? Theme.Live : Theme.Warn, out _);
            y += LHs(0.82f) + S(6) + S(10);
            if (o.Note is { Length: > 0 } note) { Ui.Text(dl, new Vector2(x, y), Fit(note, iw, 0.9f), Theme.Text2, 0.9f); y += LHs(0.9f) + S(8); }
            if (lead)
            {
                float bx = x;
                if (o.RuleId != null)
                {
                    string l = o.Cancelled ? "Restore" : "Cancel this date";
                    var ic = o.Cancelled ? FontAwesomeIcon.Undo : FontAwesomeIcon.Ban;
                    float bw = BtnW(l, ic);
                    if (Ui.Button("rd_occ_cancel", l, new Vector2(bx, y), new Vector2(bw, BtnH), Kind.Ghost, ic) && !_busy)
                        Act(g.Id, B("setOccurrence", ("key", o.Key), ("cancelled", !o.Cancelled)));
                    Ui.Tooltip(o.Cancelled ? "Hold this date again" : "Cancel only this date");
                    bx += bw + S(8);
                }
                if (session != null)
                {
                    if (_confirmRemoveSession)
                    {
                        float yw = BtnW("Delete session", null), nw = BtnW("Keep", null);
                        if (Ui.Button("rd_ses_del_yes", "Delete session", new Vector2(bx, y), new Vector2(yw, BtnH), Kind.Danger) && !_busy)
                        { _confirmRemoveSession = false; Act(g.Id, B("removeSession", ("sessionId", session.Id)), () => _dlg = Dlg.None); }
                        bx += yw + S(6);
                        if (Ui.Button("rd_ses_del_no", "Keep", new Vector2(bx, y), new Vector2(nw, BtnH))) _confirmRemoveSession = false;
                        bx += nw + S(8);
                    }
                    else
                    {
                        float bw = BtnW("Delete", FontAwesomeIcon.TrashAlt);
                        if (Ui.Button("rd_ses_del", "Delete", new Vector2(bx, y), new Vector2(bw, BtnH), Kind.Danger, FontAwesomeIcon.TrashAlt)) _confirmRemoveSession = true;
                        bx += bw + S(8);
                    }
                }
                float ew = BtnW("Edit on the website", FontAwesomeIcon.ExternalLinkAlt);
                if (bx + ew <= x + iw && Ui.Button("rd_occ_web", "Edit on the website", new Vector2(bx, y), new Vector2(ew, BtnH), Kind.Ghost, FontAwesomeIcon.ExternalLinkAlt)) OpenWebsite();
                y += BtnH + S(10);
            }

            // body (scrolls when it is taller than the dialog)
            var a = new Vector2(p.X, y);
            var bEnd = new Vector2(pe.X - S(6), pe.Y - S(14));
            float off = WheelScroll(ref _dlgScroll, a, bEnd, bodyH);
            ImGui.PushClipRect(a, bEnd, true);
            float cy = y - off;
            bool Visible(float ry, float rh) => ry >= a.Y - S(1) && ry + rh <= bEnd.Y + S(1);
            float rsW = RsvpW(true, rowH - S(10));
            var usedSubs = new HashSet<string>((g.OccSubs != null && g.OccSubs.TryGetValue(o.Key, out var subsAll) ? subsAll.Values : Enumerable.Empty<Member>()).Select(m => m.CharacterId));

            foreach (var slot in g.Roster)
            {
                if (cy + rowH > a.Y && cy < bEnd.Y)
                {
                    var rp = new Vector2(x, cy);
                    var rc = RoleColor(slot.Role);
                    var pl = slot.Player;
                    var ans = pl != null ? RsvpOf(g, o, pl.CharacterId) : default;
                    dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(ans.S == "no" ? 0.35f : 0.6f).U32(), S(6));
                    dl.AddRectFilled(rp, rp + new Vector2(S(3), rowH), rc.With(0.8f).U32(), S(2));
                    float mid = rp.Y + rowH / 2;
                    string pos = Position.GetValueOrDefault(slot.Id, slot.Id);
                    Ui.Icon(dl, new Vector2(rp.X + S(10), mid - Ui.IconSize(RoleIcon(slot.Role), 0.7f).Y / 2), RoleIcon(slot.Role), rc, 0.7f);
                    Ui.Text(dl, new Vector2(rp.X + S(28), mid - LHs(0.9f) / 2), pos, rc, 0.9f);
                    float nx = rp.X + S(64);
                    if (pl == null) Ui.Text(dl, new Vector2(nx, mid - LH / 2), "Open slot", Theme.Text3);
                    else
                    {
                        float bx = nx + Math.Min(S(170), iw * 0.28f);
                        Ui.Text(dl, new Vector2(nx, mid - LH / 2), Fit(pl.PlayerName + (pl.CharacterId == _me ? " (you)" : ""), bx - nx - S(6)), pl.CharacterId == _me ? Theme.Gold : Theme.Text);
                        if (Visible(cy, rowH)) RsvpButtons(g, o, pl.CharacterId, bx, mid - (rowH - S(10)) / 2, true, $"rd_od_{slot.Id}", rowH - S(10));
                        float sx = bx + rsW + S(4), right = rp.X + iw - S(8);
                        if (ans.S == "no")
                        {
                            var sub = SubOf(g, o.Key, slot.Id);
                            string sl = sub != null ? "Sub: " + sub.PlayerName : g.Bench.Count > 0 ? "Needs sub..." : "No bench";
                            float sw = Math.Min(Math.Max(S(120), PillW(sl, lead ? FontAwesomeIcon.CaretDown : null)), right - sx);
                            if (lead)
                            {
                                if (Visible(cy, rowH) && Pill($"rd_sub_{slot.Id}", Fit(sl, sw - S(34), 0.92f), FontAwesomeIcon.CaretDown, new Vector2(right - sw, mid - (rowH - S(10)) / 2), new Vector2(sw, rowH - S(10)),
                                        sub != null ? Theme.Live : Theme.Warn, sub != null, g.Bench.Count > 0 && !_busy))
                                {
                                    var sid = slot.Id; var key = o.Key;
                                    var items = new List<(string, string?, bool)> { ("No sub", null, true) };
                                    foreach (var b in g.Bench)
                                    {
                                        var br = RsvpOf(g, o, b.CharacterId).S;
                                        if (br == "no" || (usedSubs.Contains(b.CharacterId) && b.CharacterId != sub?.CharacterId)) continue;
                                        items.Add((b.PlayerName + (br == "yes" ? "  (coming)" : br == "maybe" ? "  (maybe)" : ""), b.CharacterId, true));
                                    }
                                    OpenPicker($"Sub for {pos}", items, v => Act(g.Id, B("setSub", ("key", key), ("slotId", sid), ("characterId", v))), sub?.CharacterId);
                                }
                                right -= sw + S(8);
                            }
                            else
                            {
                                float tw = Ui.TextSize(Fit(sl, right - sx, 0.88f), 0.88f).X;
                                Ui.Text(dl, new Vector2(right - tw, mid - LHs(0.88f) / 2), Fit(sl, right - sx, 0.88f), sub != null ? Theme.Live : Theme.Warn, 0.88f);
                                right -= tw + S(8);
                            }
                        }
                        string nt = ans.Note ?? (ans.Away != null ? "absence report" : "");
                        if (nt.Length > 0 && right - sx > S(30)) Ui.Text(dl, new Vector2(sx, mid - LHs(0.82f) / 2), Fit(nt, right - sx, 0.82f), Theme.Text3, 0.82f);
                    }
                }
                cy += rowH + rgap;
            }
            if (benchRows > 0)
            {
                cy += S(10);
                if (cy + CapsH > a.Y && cy < bEnd.Y) Title(dl, x, cy, "Bench", "can they sub?");
                cy += CapsH + S(10);
                foreach (var b in g.Bench)
                {
                    if (cy + rowH > a.Y && cy < bEnd.Y)
                    {
                        var rp = new Vector2(x, cy);
                        dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(0.5f).U32(), S(6));
                        float mid = rp.Y + rowH / 2;
                        float bx = rp.X + S(64) + Math.Min(S(170), iw * 0.28f);
                        Ui.Text(dl, new Vector2(rp.X + S(12), mid - LH / 2), Fit(b.PlayerName + (b.CharacterId == _me ? " (you)" : ""), bx - rp.X - S(18)), b.CharacterId == _me ? Theme.Gold : Theme.Text2);
                        if (Visible(cy, rowH)) RsvpButtons(g, o, b.CharacterId, bx, mid - (rowH - S(10)) / 2, true, "rd_ob_" + b.CharacterId, rowH - S(10));
                        var ans = RsvpOf(g, o, b.CharacterId);
                        string nt = ans.Note ?? "";
                        float sx = bx + rsW + S(4);
                        if (nt.Length > 0) Ui.Text(dl, new Vector2(sx, mid - LHs(0.82f) / 2), Fit(nt, rp.X + iw - S(8) - sx, 0.82f), Theme.Text3, 0.82f);
                    }
                    cy += rowH + rgap;
                }
            }
            ImGui.PopClipRect();
            if (close || ClickedOutside(_dlgFrame, p, pe)) _dlg = Dlg.None;
        }

        // =============================================================================================================
        // Report an absence (website AbsenceForm)
        // =============================================================================================================
        private void OpenAbsence(Group g)
        {
            _fWho = _me;
            _fFrom = _fTo = DateTime.Now.ToString("yyyy-MM-dd", Inv);
            _fReason = _fErr = "";
            _fFocus = true;
            OpenDialog(Dlg.Absence);
        }

        private void DrawAbsenceForm(Group g)
        {
            bool lead = IsManager(g, _me);
            float lab = LHs(0.85f) + S(4), fgap = S(12);
            var hint = Wrap("Every session in these days counts you as \"can't\", unless you answer a session yourself.", S(480), 0.85f);
            float h = S(16) + LHs(1.1f) + S(14) + (lead ? lab + BtnH + fgap : 0) + (lab + FieldH + fgap) * 2 + hint.Count * LHs(0.85f) + S(10)
                      + (_fErr.Length > 0 ? LHs(0.88f) + S(8) : 0) + BtnH + S(18);
            var (p, pe, y, close) = DialogFrame("rd_absd", "Report an absence", null, S(520), h);
            var dl = Ui.Dl;
            float x = p.X + S(18), iw = pe.X - p.X - S(36);

            if (lead)
            {
                Ui.Text(dl, new Vector2(x, y), "Who", Theme.Text2, 0.85f);
                y += lab;
                string who = _fWho.Length > 0 ? NameOf(g, _fWho) + (_fWho == _me ? " (you)" : "") : "Pick a player";
                if (Pill("rd_abs_who", who, FontAwesomeIcon.CaretDown, new Vector2(x, y), new Vector2(iw, BtnH), Theme.Gold, false))
                    OpenPicker("Who is away?", AllMembers(g).Select(m => (m.PlayerName + (m.CharacterId == _me ? " (you)" : ""), (string?)m.CharacterId, true)), v => { if (v != null) _fWho = v; }, _fWho);
                y += BtnH + fgap;
            }
            float half = (iw - S(12)) / 2;
            Ui.Text(dl, new Vector2(x, y), "From (YYYY-MM-DD)", Theme.Text2, 0.85f);
            Ui.Text(dl, new Vector2(x + half + S(12), y), "To", Theme.Text2, 0.85f);
            y += lab;
            bool submit = Input("##rd_abs_from", "YYYY-MM-DD", ref _fFrom, 12, x, y, half, ImGuiInputTextFlags.None, _fFocus);
            _fFocus = false;
            submit |= Input("##rd_abs_to", "YYYY-MM-DD", ref _fTo, 12, x + half + S(12), y, half);
            y += FieldH + fgap;
            Ui.Text(dl, new Vector2(x, y), "Reason (optional, the group sees it)", Theme.Text2, 0.85f);
            y += lab;
            submit |= Input("##rd_abs_reason", "e.g. holiday", ref _fReason, 140, x, y, iw);
            y += FieldH + fgap;
            foreach (var l in Wrap("Every session in these days counts you as \"can't\", unless you answer a session yourself.", iw, 0.85f)) { Ui.Text(dl, new Vector2(x, y), l, Theme.Text3, 0.85f); y += LHs(0.85f); }
            y += S(10);
            if (_fErr.Length > 0) { Ui.Text(dl, new Vector2(x, y), Fit(_fErr, iw, 0.88f), Theme.Bad, 0.88f); y += LHs(0.88f) + S(8); }

            float rw = BtnW("Report", null), cw = BtnW("Cancel", null);
            if (Ui.Button("rd_abs_ok", "Report", new Vector2(x + iw - rw, y), new Vector2(rw, BtnH), Kind.Gold)) submit = true;
            if (Ui.Button("rd_abs_cancel", "Cancel", new Vector2(x + iw - rw - S(8) - cw, y), new Vector2(cw, BtnH))) close = true;

            if (submit && !_busy)
            {
                if (_fWho.Length == 0) _fErr = "Pick who is away.";
                else if (!ParseDay(_fFrom, out var from)) _fErr = "\"From\" is not a date (YYYY-MM-DD).";
                else
                {
                    var to = from;
                    if (_fTo.Trim().Length > 0 && !ParseDay(_fTo, out to)) _fErr = "\"To\" is not a date (YYYY-MM-DD).";
                    else if (to < from) _fErr = "\"To\" is before \"From\".";
                    else
                    {
                        _fErr = "";
                        Act(g.Id, B("addAbsence", ("memberId", _fWho), ("from", Ds(from)), ("to", Ds(to)), ("reason", _fReason.Trim())),
                            () => { if (_dlg == Dlg.Absence) _dlg = Dlg.None; }, "Absence reported. Sessions in that time show you as \"can't\".");
                    }
                }
            }
            if (close || ClickedOutside(_dlgFrame, p, pe)) _dlg = Dlg.None;
        }

        // =============================================================================================================
        // One-off session (lead). Weekly nights and edits stay on the website.
        // =============================================================================================================
        private void OpenSessionForm()
        {
            _fTitle = _fNote = _fErr = "";
            _fDate = DateTime.Now.Date.AddDays(1).AddHours(20).ToString("yyyy-MM-dd HH:mm", Inv);
            _fHours = "3";
            _fCat = "raid";
            _fFocus = true;
            OpenDialog(Dlg.Session);
        }

        private void DrawSessionForm(Group g)
        {
            float lab = LHs(0.85f) + S(4), fgap = S(12);
            bool okDate = DateTime.TryParseExact(_fDate.Trim(), DateFormats, Inv, DateTimeStyles.AssumeLocal, out var local);
            float h = S(16) + LHs(1.1f) + S(14) + (lab + FieldH + fgap) * 3 + LHs(0.85f) + S(6) + (lab + BtnH + fgap) + (_fErr.Length > 0 ? LHs(0.88f) + S(8) : 0) + BtnH + S(18);
            var (p, pe, y, close) = DialogFrame("rd_sesd", "Add a session", "one date", S(560), h);
            var dl = Ui.Dl;
            float x = p.X + S(18), iw = pe.X - p.X - S(36), half = (iw - S(12)) / 2;

            Ui.Text(dl, new Vector2(x, y), "Title (optional)", Theme.Text2, 0.85f);
            y += lab;
            bool submit = Input("##rd_ses_title", "e.g. Prog night", ref _fTitle, 40, x, y, iw, ImGuiInputTextFlags.None, _fFocus);
            _fFocus = false;
            y += FieldH + fgap;
            Ui.Text(dl, new Vector2(x, y), "Date and time (your time)", Theme.Text2, 0.85f);
            Ui.Text(dl, new Vector2(x + half + S(12), y), "Hours", Theme.Text2, 0.85f);
            y += lab;
            submit |= Input("##rd_ses_date", "YYYY-MM-DD HH:mm", ref _fDate, 40, x, y, half);
            submit |= Input("##rd_ses_hours", "3", ref _fHours, 5, x + half + S(12), y, half, ImGuiInputTextFlags.CharsDecimal);
            y += FieldH + S(4);
            Ui.Text(dl, new Vector2(x, y), okDate ? local.ToString("dddd, MMMM d, yyyy, HH:mm", Inv) : "Use YYYY-MM-DD HH:mm", okDate ? Theme.Text3 : Theme.Warn, 0.85f);
            y += LHs(0.85f) + S(6) + fgap - S(4);
            Ui.Text(dl, new Vector2(x, y), "Kind", Theme.Text2, 0.85f);
            y += lab;
            if (Pill("rd_ses_cat", _fCat, FontAwesomeIcon.CaretDown, new Vector2(x, y), new Vector2(half, BtnH), CatColor(_fCat), true))
                OpenPicker("Kind of session", Categories.Select(c => (c, (string?)c, true)), v => { if (v != null) _fCat = v; }, _fCat);
            y += BtnH + fgap;
            Ui.Text(dl, new Vector2(x, y), "Note (optional)", Theme.Text2, 0.85f);
            y += lab;
            submit |= Input("##rd_ses_note", "", ref _fNote, 200, x, y, iw);
            y += FieldH + fgap;
            if (_fErr.Length > 0) { Ui.Text(dl, new Vector2(x, y), Fit(_fErr, iw, 0.88f), Theme.Bad, 0.88f); y += LHs(0.88f) + S(8); }

            float aw = BtnW("Add", FontAwesomeIcon.Plus), cw = BtnW("Cancel", null);
            if (Ui.Button("rd_ses_ok", "Add", new Vector2(x + iw - aw, y), new Vector2(aw, BtnH), Kind.Gold, FontAwesomeIcon.Plus)) submit = true;
            if (Ui.Button("rd_ses_cancel", "Cancel", new Vector2(x + iw - aw - S(8) - cw, y), new Vector2(cw, BtnH))) close = true;

            if (submit && !_busy)
            {
                if (!okDate) _fErr = "The date is not valid (YYYY-MM-DD HH:mm).";
                else if (!double.TryParse(_fHours.Replace(',', '.'), NumberStyles.Float, Inv, out var hours) || hours < 0.5 || hours > 12) _fErr = "Hours: 0.5 to 12.";
                else
                {
                    _fErr = "";
                    Act(g.Id, B("addSession", ("date", local.ToUniversalTime().ToString("o", Inv)), ("title", _fTitle.Trim()), ("category", _fCat),
                            ("durationMin", (int)Math.Round(hours * 60)), ("note", _fNote.Trim())),
                        () => { if (_dlg == Dlg.Session) _dlg = Dlg.None; }, "Session added.");
                }
            }
            if (close || ClickedOutside(_dlgFrame, p, pe)) _dlg = Dlg.None;
        }
    }
}
