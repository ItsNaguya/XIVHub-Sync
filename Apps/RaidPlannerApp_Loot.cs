using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Loot & BiS tab (website LootView.tsx): BiS plan (slot grid), this week (priority per drop, clears), loot log, books.
    // The section follows prefs.raids.loot. BiS import from XIVGear / Etro stays on the website.
    public partial class RaidPlannerApp
    {
        private int _week;            // 0 = current
        private string _menuCid = "", _menuSlot = "";

        private static (string Key, string Text) StateOf(Gear e)
        {
            if (e.Bis == "other") return ("other", e.Has ? "have it (not from raid or tomes)" : "not from raid or tomes (crafted, relic...)");
            if (NeedsRaid(e)) return ("need-raid", "needs the raid drop");
            if (NeedsTome(e)) return ("need-tome", "needs to buy with tomestones");
            if (NeedsUpgrade(e)) return ("need-up", "has the tome piece, needs the upgrade");
            return ("done", e.Bis == "tome" ? "done (upgraded tome piece)" : "done (raid piece)");
        }

        private static Vector4 StateColor(string key) => key switch
        {
            "need-raid" => Theme.Bad, "need-tome" => Theme.Warn, "need-up" => Theme.Violet, "done" => Theme.Live, _ => Theme.Text3,
        };

        private static Vector4 FloorColor(int f) => f switch { 1 => Theme.Crystal, 2 => Theme.Live, 3 => Theme.Violet, _ => Theme.Gold };

        private float DrawLoot(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            var players = g.Roster.Where(s => s.Player != null).ToList();
            if (players.Count == 0)
            {
                float h = Pad * 2 + LH;
                Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
                Ui.Text(dl, new Vector2(x + Pad, y + Pad), Fit("Place players on the roster first; the loot plan has one row per roster player.", w - Pad * 2), Theme.Text3);
                return y + h;
            }
            var opts = new[] { ("gear", "BiS plan"), ("week", "This week"), ("log", "Loot log"), ("books", "Books") };
            var picked = Seg("rd_loot", x, y, opts, _lootSec, out float sw);
            if (picked != null) SavePrefs(loot: picked);
            string wk = $"Week {WeekNumber(g)}{(g.Tier?.Name is { Length: > 0 } tn ? " of " + tn : "")}";
            float wkw = Ui.TextSize(wk, 0.88f).X;
            if (x + sw + S(16) + wkw <= x + w) Ui.Text(dl, new Vector2(x + w - wkw, y + (BtnH - LHs(0.88f)) / 2), wk, Theme.Text3, 0.88f);
            y += BtnH + S(12);
            return _lootSec switch
            {
                "week" => DrawWeek(g, x, y, w, lead),
                "log" => DrawLog(g, x, y, w, lead),
                "books" => DrawBooks(g, x, y, w, players),
                _ => DrawGear(g, x, y, w, lead, players),
            };
        }

        // ---- BiS plan --------------------------------------------------------------------------------------------
        private float DrawGear(Group g, float x, float y, float w, bool lead, List<Slot> players)
        {
            var dl = Ui.Dl;
            var floors = Floors(g);
            float iw = w - Pad * 2;
            float nameW = Math.Clamp(iw * 0.26f, S(140), S(240));
            float cellW = (iw - nameW) / GearSlots.Length;
            float rowH = LH + LHs(0.78f) + S(14), headH = LHs(0.75f) + S(10), rgap = S(4);
            float legendH = LHs(0.82f) + S(10);
            float h = Pad + legendH + headH + players.Count * (rowH + rgap) - rgap + S(12) + BtnH + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;

            // legend
            float lx = cx;
            foreach (var (key, badge, text) in new[] { ("need-raid", "R", "raid drop"), ("need-tome", "T", "tomestones"), ("need-up", "", "upgrade"), ("done", "", "done"), ("other", "·", "other") })
            {
                var col = StateColor(key);
                float lbw = LHs(0.82f) + S(4);
                if (lx + lbw + Ui.TextSize(text, 0.82f).X + S(14) > cx + iw) break;
                dl.AddRectFilled(new Vector2(lx, cy), new Vector2(lx + lbw, cy + lbw), col.With(0.25f).U32(), S(3));
                if (badge.Length > 0) Ui.Text(dl, new Vector2(lx + (lbw - Ui.TextSize(badge, 0.78f).X) / 2, cy + (lbw - LHs(0.78f)) / 2), badge, col, 0.78f);
                else Ui.IconCentered(dl, new Vector2(lx + lbw / 2, cy + lbw / 2), (key == "done" ? FontAwesomeIcon.Check : FontAwesomeIcon.Gem).ToIconString(), col, 0.6f);
                lx += lbw + S(5);
                Ui.Text(dl, new Vector2(lx, cy + (lbw - LHs(0.82f)) / 2), text, Theme.Text2, 0.82f);
                lx += Ui.TextSize(text, 0.82f).X + S(14);
            }
            cy += legendH;

            // header: slot names coloured by floor
            for (int i = 0; i < GearSlots.Length; i++)
            {
                var s = GearSlots[i];
                string label = Fit(GearLabel[s], cellW - S(2), 0.75f);
                float lw = Ui.TextSize(label, 0.75f).X;
                var lp = new Vector2(cx + nameW + i * cellW + (cellW - lw) / 2, cy);
                Ui.Text(dl, lp, label, FloorColor(FloorOf[s]), 0.75f);
                ImGui.SetCursorScreenPos(new Vector2(cx + nameW + i * cellW, cy)); ImGui.Dummy(new Vector2(cellW, LHs(0.75f)));
                Ui.Tooltip($"{GearLabel[s]}: raid drop from {floors[FloorOf[s] - 1]}");
            }
            cy += headH;

            foreach (var slot in players)
            {
                var p = slot.Player!;
                var cid = p.CharacterId;
                bool mine = cid == _me, canEdit = lead || mine;
                var rp = new Vector2(cx, cy);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), (mine ? Ui.Accent.With(0.1f) : Theme.Night2.With(0.5f)).U32(), S(6));
                dl.AddRectFilled(rp, rp + new Vector2(S(3), rowH), RoleColor(slot.Role).With(0.8f).U32(), S(2));
                // name + still needs
                string name = p.PlayerName + (slot.Job != null ? "  " + slot.Job : "");
                float linkW = 0;
                if (g.BisLinks != null && g.BisLinks.TryGetValue(cid, out var link) && link.Url.StartsWith("http"))
                {
                    float bs = LH;
                    linkW = bs + S(4);
                    if (Ui.IconButton("rd_bis_" + cid, FontAwesomeIcon.ExternalLinkAlt, new Vector2(rp.X + nameW - bs - S(6), rp.Y + S(5)), bs, "BiS: " + Or(link.Name, link.Url), Theme.Text3))
                        Dalamud.Utility.Util.OpenLink(link.Url);
                }
                Ui.Text(dl, rp + S(10, 5), Fit(name, nameW - S(16) - linkW), mine ? Theme.Gold : Theme.Text);
                var sum = NeedSummary(g, cid);
                var parts = new List<string>();
                if (sum.Raid > 0) parts.Add($"{sum.Raid} raid");
                if (sum.Tome > 0) parts.Add($"{sum.Tome} tome (~{sum.Weeks} wk)");
                if (sum.Twine > 0) parts.Add($"{sum.Twine} Twine");
                if (sum.Glaze > 0) parts.Add($"{sum.Glaze} Glaze");
                if (sum.Solvent > 0) parts.Add($"{sum.Solvent} Solvent");
                string need = parts.Count == 0 ? "BiS complete" : string.Join(" · ", parts);
                var np = rp + new Vector2(S(10), S(5) + LH + S(1));
                Ui.Text(dl, np, Fit(need, nameW - S(16), 0.78f), parts.Count == 0 ? Theme.Live : Theme.Text3, 0.78f);
                if (sum.Tome > 0) { ImGui.SetCursorScreenPos(np); ImGui.Dummy(new Vector2(nameW - S(16), LHs(0.78f))); Ui.Tooltip($"{sum.Tomes:N0} tomestones at {WeeklyTomeCap} a week"); }

                // slot cells
                for (int i = 0; i < GearSlots.Length; i++)
                {
                    var s = GearSlots[i];
                    var e = GearOf(g, cid, s);
                    var st = StateOf(e);
                    var col = StateColor(st.Key);
                    float cs = Math.Min(cellW - S(6), rowH - S(10));
                    var a = new Vector2(rp.X + nameW + i * cellW + (cellW - cs) / 2, rp.Y + (rowH - cs) / 2);
                    float hv = 0;
                    bool click = false;
                    string tip = $"{GearLabel[s]}{(e.Item != null ? $": {e.Item}{(e.Ilvl > 0 ? $" (i{e.Ilvl})" : "")}" : "")} · {st.Text}";
                    if (canEdit && !_busy) { click = Ui.Hit($"rd_g_{cid}_{s}", a, new Vector2(cs, cs), out hv); Ui.Tooltip(tip + "\nClick to change"); }
                    else { ImGui.SetCursorScreenPos(a); ImGui.Dummy(new Vector2(cs, cs)); Ui.Tooltip(tip); }
                    dl.AddRectFilled(a, a + new Vector2(cs, cs), col.With(0.16f + 0.18f * hv).U32(), S(5));
                    dl.AddRect(a, a + new Vector2(cs, cs), col.With(0.45f + 0.3f * hv).U32(), S(5), 0, S(1));
                    var c = a + new Vector2(cs, cs) / 2;
                    switch (st.Key)
                    {
                        case "need-raid": Ui.Text(dl, c - Ui.TextSize("R", 0.85f) / 2, "R", col, 0.85f); break;
                        case "need-tome": Ui.Text(dl, c - Ui.TextSize("T", 0.85f) / 2, "T", col, 0.85f); break;
                        case "need-up": Ui.IconCentered(dl, c, FontAwesomeIcon.Gem.ToIconString(), col, 0.7f); break;
                        case "done": Ui.IconCentered(dl, c, FontAwesomeIcon.Check.ToIconString(), col, 0.75f); break;
                        default: dl.AddCircleFilled(c, S(2.5f), col.U32()); break;
                    }
                    if (click) { _menuCid = cid; _menuSlot = s; OpenDialog(Dlg.SlotMenu); }
                }
                cy += rowH + rgap;
            }
            cy += S(12) - rgap;
            float bw = BtnW("Import BiS on the website", FontAwesomeIcon.ExternalLinkAlt);
            if (Ui.Button("rd_bis_web", "Import BiS on the website", new Vector2(cx, cy), new Vector2(Math.Min(bw, iw), BtnH), Kind.Ghost, FontAwesomeIcon.ExternalLinkAlt)) OpenWebsite();
            Ui.Tooltip("Read a set from XIVGear or Etro (raid vs tome per slot) on the website's Loot & BiS tab");
            return y + h;
        }

        /// <summary>One slot: where the BiS comes from, have it / upgraded, and (lead) log it as this week's drop.</summary>
        private void DrawSlotMenu(Group g)
        {
            var cid = _menuCid; var slot = _menuSlot;
            if (!GearSlots.Contains(slot) || AllMembers(g).All(m => m.CharacterId != cid)) { _dlg = Dlg.None; return; }
            var e = GearOf(g, cid, slot);
            string src = e.Bis ?? "raid";
            bool lead = IsManager(g, _me);
            bool canLog = lead && src == "raid" && !e.Has;
            float lab = LHs(0.85f) + S(6);
            float h = S(16) + LHs(1.1f) + S(14) + (e.Item != null ? LHs(0.9f) + S(10) : 0) + (lab + BtnH + S(12)) * 2 + (canLog ? BtnH + S(4) : 0) + S(14);
            var (p, pe, y, close) = DialogFrame("rd_slotd", GearLabel[slot], NameOf(g, cid), S(460), h, Theme.Gold.With(0.45f));
            var dl = Ui.Dl;
            float x = p.X + S(18), iw = pe.X - p.X - S(36);
            if (e.Item != null) { Ui.Text(dl, new Vector2(x, y), Fit(e.Item + (e.Ilvl > 0 ? $"  (i{e.Ilvl})" : ""), iw, 0.9f), Theme.Text2, 0.9f); y += LHs(0.9f) + S(10); }

            void Set(params (string, object?)[] f)
            {
                var body = B("setLoot", ("characterId", cid), ("slot", slot));
                foreach (var (k, v) in f) body[k] = v;
                Act(g.Id, body, () => { if (_dlg == Dlg.SlotMenu) _dlg = Dlg.None; });
            }

            Ui.Text(dl, new Vector2(x, y), "BiS comes from", Theme.Text2, 0.85f);
            y += lab;
            var srcs = new[] { ("raid", "Raid drop"), ("tome", "Tomestones"), ("other", "Other") };
            float bw3 = (iw - S(12)) / 3;
            for (int i = 0; i < 3; i++)
                if (Pill("rd_sm_src_" + srcs[i].Item1, srcs[i].Item2, null, new Vector2(x + i * (bw3 + S(6)), y), new Vector2(bw3, BtnH), Theme.Gold, src == srcs[i].Item1, !_busy) && src != srcs[i].Item1)
                    Set(("bis", srcs[i].Item1));
            y += BtnH + S(12);

            Ui.Text(dl, new Vector2(x, y), "Status", Theme.Text2, 0.85f);
            y += lab;
            var states = new List<(string Id, string Label, bool On, (string, object?)[] Body)>
            {
                ("need", "Still needed", !e.Has, new (string, object?)[] { ("has", false), ("aug", false) }),
                ("have", "Have it", e.Has && !e.Aug, new (string, object?)[] { ("has", true), ("aug", false) }),
            };
            if (src == "tome") states.Add(("aug", "Upgraded", e.Aug, new (string, object?)[] { ("has", true), ("aug", true) }));
            float bws = (iw - S(6) * (states.Count - 1)) / states.Count;
            for (int i = 0; i < states.Count; i++)
                if (Pill("rd_sm_st_" + states[i].Id, states[i].Label, null, new Vector2(x + i * (bws + S(6)), y), new Vector2(bws, BtnH), Theme.Live, states[i].On, !_busy) && !states[i].On)
                    Set(states[i].Body);
            y += BtnH + S(12);

            if (canLog && Ui.Button("rd_sm_log", "Log as dropped this week", new Vector2(x, y), new Vector2(iw, BtnH), Kind.Gold, FontAwesomeIcon.Gift) && !_busy)
                Act(g.Id, B("logLoot", ("item", slot), ("memberId", cid), ("method", "drop")), () => { if (_dlg == Dlg.SlotMenu) _dlg = Dlg.None; });

            if (close || ClickedOutside(_dlgFrame, p, pe)) _dlg = Dlg.None;
        }

        // ---- this week -------------------------------------------------------------------------------------------
        private float DrawWeek(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            var floors = Floors(g);
            int current = WeekNumber(g);
            int week = _week <= 0 ? current : Math.Min(_week, current + 1);
            var rosterIds = g.Roster.Where(s => s.Player != null).Select(s => s.Player!.CharacterId).ToList();
            var log = (g.LootLog ?? new()).Where(l => l.Week == week).ToList();

            // bar
            if (Ui.IconButton("rd_wk_prev", FontAwesomeIcon.Minus, new Vector2(x, y), BtnH, "Previous week")) _week = Math.Max(1, week - 1);
            string wl = $"Week {week}{(week == current ? " (now)" : "")}";
            float wlw = Ui.TextSize("Week 00 (now)").X + S(12);
            Ui.Text(dl, new Vector2(x + BtnH + S(4) + (wlw - Ui.TextSize(wl).X) / 2, y + (BtnH - LH) / 2), wl, Theme.Text);
            if (Ui.IconButton("rd_wk_next", FontAwesomeIcon.Plus, new Vector2(x + BtnH + S(8) + wlw, y), BtnH, "Next week")) _week = Math.Min(current + 1, week + 1);
            if (_week == current) _week = 0;
            float hx = x + BtnH * 2 + S(20) + wlw;
            string prio = g.Tier?.Priority == "even" ? "Priority: most need first; every drop already received lowers it." : "Priority: melee > ranged > caster > tank > healer, then most need; every drop received lowers it.";
            var hint = Wrap(prio, x + w - hx, 0.8f);
            for (int i = 0; i < Math.Min(2, hint.Count); i++) Ui.Text(dl, new Vector2(hx, y + (BtnH - LHs(0.8f) * Math.Min(2, hint.Count)) / 2 + i * LHs(0.8f)), hint[i], Theme.Text3, 0.8f);
            y += BtnH + S(12);

            bool two = w >= S(700);
            float gap = S(12), fw = two ? (w - gap) / 2 : w;
            float rowY = y, colEnd = y;
            for (int floor = 1; floor <= 4; floor++)
            {
                bool left = !two || floor % 2 == 1;
                float fx = left ? x : x + fw + gap;
                float fy = two ? rowY : colEnd;
                float end = DrawFloor(g, fx, fy, fw, floor, floors[floor - 1], week, current, lead, rosterIds, log);
                if (two)
                {
                    colEnd = Math.Max(colEnd, end);
                    if (!left) { rowY = colEnd + gap; colEnd = rowY; }
                }
                else colEnd = end + gap;
            }
            return two ? rowY - gap : colEnd - gap;
        }

        private float DrawFloor(Group g, float x, float y, float w, int floor, string fname, int week, int current, bool lead, List<string> rosterIds, List<LogEntry> log)
        {
            var dl = Ui.Dl;
            float iw = w - Pad * 2;
            var clear = (g.Clears ?? new()).FirstOrDefault(c => c.Week == week && c.Floor == floor);
            var dropped = log.Where(l => l.Floor == floor).ToList();
            var items = GearSlots.Where(s => FloorOf[s] == floor && s != "ring2").Concat(Materials.Where(m => MaterialFloor[m] == floor)).ToList();
            float itemH = LH + S(10), whoH = LHs(0.85f) + S(10);

            // who chips (lead, cleared) and dropped chips wrap: lay them out first
            List<(string Text, float W)> Chips(IEnumerable<string> texts, float scale) => texts.Select(t => (t, Ui.TextSize(t, scale).X + S(16))).ToList();
            int Lines(List<(string Text, float W)> chips)
            {
                if (chips.Count == 0) return 0;
                int n = 1; float lx = 0;
                foreach (var c in chips) { if (lx > 0 && lx + c.W > iw) { n++; lx = 0; } lx += c.W + S(6); }
                return n;
            }
            var whoChips = clear != null && lead ? Chips(rosterIds.Select(id => NameOf(g, id)), 0.85f) : new();
            var dropChips = Chips(dropped.Select(l => $"{ItemLabel(l.Item)} → {NameOf(g, l.MemberId)}{(l.Method == "book" ? " (books)" : "")}"), 0.82f);
            int whoLines = Lines(whoChips), dropLines = Lines(dropChips);
            float h = Pad + BtnH + S(10) + whoLines * (whoH + S(4)) + (whoLines > 0 ? S(6) : 0) + dropLines * (LHs(0.82f) + S(10)) + (dropLines > 0 ? S(6) : 0) + items.Count * itemH + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), clear != null ? Theme.Live.With(0.05f) : null, clear != null ? Theme.Live.With(0.35f) : null, 10f);
            float cx = x + Pad, cy = y + Pad;
            Ui.Text(dl, new Vector2(cx, cy + (BtnH - LHs(1.05f)) / 2), Fit(fname, iw * 0.55f, 1.05f), FloorColor(floor), 1.05f);
            string cl = clear != null ? "Cleared" : "Not cleared";
            float cbw = PillW(cl, clear != null ? FontAwesomeIcon.Check : null);
            if (lead)
            {
                if (Pill($"rd_clr_{floor}", cl, clear != null ? FontAwesomeIcon.Check : null, new Vector2(cx + iw - cbw, cy), new Vector2(cbw, BtnH), Theme.Live, clear != null, !_busy,
                        clear != null ? "Cleared: everyone listed earned a book. Click to undo." : "Mark as cleared this week: the roster earns one book of this floor"))
                    Act(g.Id, B("setClear", ("week", week), ("floor", floor), ("members", clear != null ? null : rosterIds)));
            }
            else if (clear != null) Pill($"rd_clr_{floor}", cl, FontAwesomeIcon.Check, new Vector2(cx + iw - cbw, cy), new Vector2(cbw, BtnH), Theme.Live, true, false);
            cy += BtnH + S(10);

            if (whoLines > 0)
            {
                float lx = cx;
                for (int i = 0; i < rosterIds.Count; i++)
                {
                    var id = rosterIds[i];
                    var (t, cw) = whoChips[i];
                    if (lx > cx && lx + cw > cx + iw) { lx = cx; cy += whoH + S(4); }
                    bool inIt = clear!.Members.Contains(id);
                    if (Pill($"rd_who_{floor}_{id}", t, null, new Vector2(lx, cy), new Vector2(cw, whoH), Theme.Live, inIt, !_busy,
                            inIt ? "Was there (earned a book). Click if not." : "Was not there. Click if they were."))
                    {
                        var members = inIt ? clear.Members.Where(m => m != id).ToList() : clear.Members.Append(id).ToList();
                        Act(g.Id, B("setClear", ("week", week), ("floor", floor), ("members", members)));
                    }
                    lx += cw + S(6);
                }
                cy += whoH + S(4) + S(6);
            }
            if (dropLines > 0)
            {
                float lx = cx;
                foreach (var (t, cw) in dropChips)
                {
                    if (lx > cx && lx + cw > cx + iw) { lx = cx; cy += LHs(0.82f) + S(10); }
                    Ui.Chip(dl, new Vector2(lx, cy), t, Theme.Gold, out _);
                    lx += cw + S(6);
                }
                cy += LHs(0.82f) + S(10) + S(6);
            }

            float labW = Math.Min(S(110), iw * 0.3f);
            foreach (var item in items)
            {
                string label = item == "ring" ? "Ring" : ItemLabel(item);
                Ui.Text(dl, new Vector2(cx, cy + (itemH - LHs(0.9f)) / 2), Fit(label, labW - S(6), 0.9f), Theme.Text2, 0.9f);
                var list = PriorityFor(g, item).Take(4).ToList();
                float lx = cx + labW;
                if (list.Count == 0) Ui.Text(dl, new Vector2(lx, cy + (itemH - LHs(0.85f)) / 2), "nobody needs it", Theme.Text3, 0.85f);
                bool canGive = lead && week == current && !_busy;
                for (int i = 0; i < list.Count; i++)
                {
                    var (s, score, why) = list[i];
                    string nm = s.Player!.PlayerName;
                    float gb = canGive ? LH + S(2) : 0;
                    float room = (cx + iw - lx) - (list.Count - i - 1) * S(40);
                    string fit = Fit(nm, Math.Max(S(30), Math.Min(Ui.TextSize(nm, 0.88f).X, room - gb - S(8))), 0.88f);
                    float nw = Ui.TextSize(fit, 0.88f).X;
                    if (lx + nw + gb > cx + iw) break;
                    var np = new Vector2(lx, cy + (itemH - LHs(0.88f)) / 2);
                    Ui.Text(dl, np, fit, i == 0 ? Theme.Gold : Theme.Text, 0.88f);
                    ImGui.SetCursorScreenPos(np); ImGui.Dummy(new Vector2(Math.Max(nw, S(2)), LHs(0.88f)));
                    Ui.Tooltip($"Score {score}: {why}");
                    lx += nw + S(2);
                    if (canGive)
                    {
                        var cid = s.Player.CharacterId;
                        if (Ui.IconButton($"rd_give_{floor}_{item}_{cid}", FontAwesomeIcon.Gift, new Vector2(lx, cy + (itemH - gb) / 2), gb,
                                $"Give the {label} to {nm} (logs the drop and marks it obtained)", Theme.Gold.With(0.8f)))
                            Act(g.Id, B("logLoot", ("item", item), ("memberId", cid), ("method", "drop"), ("week", week)));
                        lx += gb;
                    }
                    lx += S(12);
                }
                cy += itemH;
            }
            return y + h;
        }

        // ---- loot log ----------------------------------------------------------------------------------------------
        private float DrawLog(Group g, float x, float y, float w, bool lead)
        {
            var dl = Ui.Dl;
            var floors = Floors(g);
            var log = (g.LootLog ?? new()).OrderByDescending(l => l.Week).ThenByDescending(l => l.At).Take(200).ToList();
            float iw = w - Pad * 2, rowH = LH + S(12), rgap = S(4);
            float h = Pad + Math.Max(1, log.Count) * (rowH + rgap) - rgap + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            if (log.Count == 0) { Ui.Text(dl, new Vector2(cx, cy + (rowH - LH) / 2), "No drops logged yet. Give drops from \"This week\" or a slot in the BiS plan.", Theme.Text3); return y + h; }
            foreach (var l in log)
            {
                var rp = new Vector2(cx, cy);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), Theme.Night2.With(0.5f).U32(), S(5));
                float mid = rp.Y + rowH / 2, rx = rp.X + iw - S(6);
                if (lead)
                {
                    float bs = rowH - S(6);
                    rx -= bs;
                    if (Ui.IconButton("rd_log_x_" + l.Id, FontAwesomeIcon.Undo, new Vector2(rx, mid - bs / 2), bs, "Remove this entry (undoes the slot it marked)", Theme.Text3) && !_busy)
                        Act(g.Id, B("removeLog", ("id", l.Id)));
                    rx -= S(8);
                }
                string wk = $"W{l.Week}";
                Ui.Text(dl, new Vector2(rp.X + S(10), mid - LHs(0.85f) / 2), wk, Theme.Text3, 0.85f);
                float fx = rp.X + S(10) + Ui.TextSize("W00", 0.85f).X + S(10);
                string fl = l.Floor is >= 1 and <= 4 ? floors[l.Floor - 1] : "";
                float flw = Math.Min(Ui.TextSize(fl, 0.85f).X, S(90));
                Ui.Text(dl, new Vector2(fx, mid - LHs(0.85f) / 2), Fit(fl, flw, 0.85f), FloorColor(l.Floor), 0.85f);
                float tx = fx + flw + S(12);
                string main = $"{ItemLabel(l.Item)} → {NameOf(g, l.MemberId)}{(l.Method == "book" ? "  (books)" : "")}{(l.Note is { Length: > 0 } n ? "  · " + n : "")}";
                Ui.Text(dl, new Vector2(tx, mid - LH / 2), Fit(main, rx - tx), l.MemberId == _me ? Theme.Gold : Theme.Text);
                cy += rowH + rgap;
            }
            return y + h;
        }

        // ---- books -------------------------------------------------------------------------------------------------
        private float DrawBooks(Group g, float x, float y, float w, List<Slot> players)
        {
            var dl = Ui.Dl;
            var floors = Floors(g);
            float iw = w - Pad * 2, rowH = LH + S(12), rgap = S(4);
            float nameW = Math.Clamp(iw * 0.34f, S(140), S(260)), colW = (iw - nameW) / 4;
            var hint = Wrap("Books (pages) saved per floor: one per cleared floor each week, minus book purchases. Adjust them on the website.", iw, 0.82f);
            float h = Pad + LHs(0.85f) + S(10) + players.Count * (rowH + rgap) - rgap + S(10) + hint.Count * LHs(0.82f) + Pad;
            Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), null, null, 12f);
            float cx = x + Pad, cy = y + Pad;
            for (int f = 0; f < 4; f++)
            {
                string t = Fit(floors[f], colW - S(4), 0.85f);
                Ui.Text(dl, new Vector2(cx + nameW + f * colW + (colW - Ui.TextSize(t, 0.85f).X) / 2, cy), t, FloorColor(f + 1), 0.85f);
            }
            cy += LHs(0.85f) + S(10);
            foreach (var s in players)
            {
                var cid = s.Player!.CharacterId;
                var rp = new Vector2(cx, cy);
                dl.AddRectFilled(rp, rp + new Vector2(iw, rowH), (cid == _me ? Ui.Accent.With(0.1f) : Theme.Night2.With(0.5f)).U32(), S(5));
                Ui.Text(dl, new Vector2(rp.X + S(10), rp.Y + (rowH - LH) / 2), Fit(s.Player.PlayerName, nameW - S(16)), cid == _me ? Theme.Gold : Theme.Text);
                var bk = Books(g, cid);
                for (int f = 0; f < 4; f++)
                {
                    string t = bk[f].ToString();
                    Ui.Text(dl, new Vector2(rp.X + nameW + f * colW + (colW - Ui.TextSize(t).X) / 2, rp.Y + (rowH - LH) / 2), t, bk[f] < 0 ? Theme.Bad : bk[f] == 0 ? Theme.Text3 : Theme.Text);
                }
                cy += rowH + rgap;
            }
            cy += S(10) - rgap;
            foreach (var l in hint) { Ui.Text(dl, new Vector2(cx, cy), l, Theme.Text3, 0.82f); cy += LHs(0.82f); }
            return y + h;
        }
    }
}
