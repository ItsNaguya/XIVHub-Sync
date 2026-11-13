using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // The screen: reset cards, tool bar, category panels, retainer cards and the custom task dialog.
    public partial class RoutinesApp
    {
        private sealed class Draft
        {
            public string? Id;
            public string Label = "";
            public string Type = "daily";
            public string Category = "custom_daily";
        }

        private string _tab = "daily";
        private bool _editing;
        private bool _confirmReset;
        private string? _confirmDelete;
        private Draft? _draft;
        private bool _focusDraft;

        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float CapsH => Ui.TextSize("A", 0.78f).Y;
        private static float BtnH => LH + S(12);

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || Ui.TextSize(text, scale).X <= width) return text ?? "";
            if (width <= 0) return "";
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }

        private static float BtnW(string label, FontAwesomeIcon? icon) => Ui.TextSize(label).X + (icon.HasValue ? Ui.IconSize(icon.Value, 0.9f).X + S(6) : 0) + S(24);

        private static void Gem(ImDrawListPtr dl, Vector2 c, float r, Vector4 col)
            => dl.AddQuadFilled(c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0), col.U32());

        public void Draw()
        {
            var o = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            bool dialog = _draft != null;
            if (dialog) ImGui.BeginDisabled();
            float y = o.Y;
            y = DrawResets(o.X, y, w) + S(12);
            y = DrawBar(o.X, y, w) + S(12);
            y = DrawCategories(o.X, y, w) + S(20);
            y = DrawRetainers(o.X, y, w);
            ImGui.SetCursorScreenPos(new Vector2(o.X, y));
            ImGui.Dummy(new Vector2(S(1), S(4)));
            if (dialog)
            {
                ImGui.EndDisabled();
                DrawDialog();
            }
        }

        // ---- daily / weekly cards ------------------------------------------------------------------------------------
        private float DrawResets(float x, float y, float w)
        {
            bool stack = w < S(520);
            float gap = S(12), h = Math.Max(S(56), LH * 2 + S(8)) + S(24);
            float cw = stack ? w : (w - gap) / 2;
            ResetCard("daily", new Vector2(x, y), new Vector2(cw, h));
            var p2 = stack ? new Vector2(x, y + h + gap) : new Vector2(x + cw + gap, y);
            ResetCard("weekly", p2, new Vector2(cw, h));
            return y + (stack ? h * 2 + gap : h);
        }

        private void ResetCard(string kind, Vector2 p, Vector2 size)
        {
            bool active = _tab == kind;
            if (Ui.Hit("rt_tab_" + kind, p, size, out float hov)) { _tab = kind; _confirmReset = false; _confirmDelete = null; }
            var dl = Ui.Dl;
            var line = active ? Theme.Gold.With(0.6f) : Theme.CrystalLine.With(0.22f + 0.3f * hov);
            Ui.Panel(dl, p, p + size, active ? UIHelper.LerpColor(Theme.Night2, Theme.Gold, 0.1f).With(0.88f) : Theme.Night2.With(0.65f + 0.2f * hov), line, 12f);

            var (done, total) = Progress(kind);
            int pct = total == 0 ? 0 : (int)Math.Round(done * 100.0 / total);
            float pad = S(16);

            // left: title and countdown
            float titleH = Ui.TextSize("Ag", 1.2f).Y;
            float top = p.Y + (size.Y - titleH - S(6) - LH) / 2;
            Gem(dl, new Vector2(p.X + pad + S(5), top + titleH / 2), S(5), active ? Theme.Gold : Theme.Crystal.With(0.7f));
            Ui.Text(dl, new Vector2(p.X + pad + S(18), top), kind == "daily" ? "Daily" : "Weekly", active ? Theme.Text : Theme.Text2, 1.2f);

            var next = kind == "daily" ? Resets.NextDaily() : Resets.NextWeekly();
            var left = next - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            string clock = kind == "daily" ? $"{(int)left.TotalHours}h {left.Minutes:00}m {left.Seconds:00}s" : $"{(int)left.TotalDays}d {left.Hours:00}h {left.Minutes:00}m";
            var local = next.ToLocalTime();
            string at = "at " + (kind == "daily" ? local.ToString("HH:mm") : local.ToString("ddd HH:mm"));
            float ly = top + titleH + S(6);
            Ui.Icon(dl, new Vector2(p.X + pad, ly + S(2)), FontAwesomeIcon.Clock, Theme.Text3, 0.82f);
            float cx = p.X + pad + Ui.IconSize(FontAwesomeIcon.Clock, 0.82f).X + S(7);
            Ui.Text(dl, new Vector2(cx, ly), clock, active ? Theme.Gold : Theme.Text, 1f);
            cx += Ui.TextSize(clock).X + S(8);
            Ui.Text(dl, new Vector2(cx, ly + S(1)), at, Theme.Text3, 0.88f);
            if (ImGui.IsMouseHoveringRect(new Vector2(cx, ly), new Vector2(cx + Ui.TextSize(at, 0.88f).X, ly + LH))) UIHelper.DrawTooltip("Next reset in your local time");

            // right: ring and numbers
            string ps = $"{pct}%", ds = $"{done}/{total}";
            float numW = Math.Max(Ui.TextSize(ps, 1.1f).X, Ui.TextSize(ds, 0.85f).X);
            float r = S(20);
            var ringC = new Vector2(p.X + size.X - pad - numW - S(10) - r, p.Y + size.Y / 2);
            dl.AddCircle(ringC, r, Theme.Night0.With(0.8f).U32(), 48, S(4));
            if (done > 0 && total > 0)
            {
                float a0 = -MathF.PI / 2;
                dl.PathArcTo(ringC, r, a0, a0 + MathF.PI * 2 * done / total, 48);
                dl.PathStroke((done == total ? Theme.Live : Theme.Gold).U32(), ImDrawFlags.None, S(4));
            }
            float nx = ringC.X + r + S(10), ny = p.Y + (size.Y - Ui.TextSize("Ag", 1.1f).Y - Ui.TextSize("Ag", 0.85f).Y) / 2;
            Ui.Text(dl, new Vector2(nx, ny), ps, done == total && total > 0 ? Theme.Live : Theme.Gold, 1.1f);
            Ui.Text(dl, new Vector2(nx, ny + Ui.TextSize("Ag", 1.1f).Y), ds, Theme.Text3, 0.85f);
            if (ImGui.IsMouseHoveringRect(ringC - new Vector2(r, r), new Vector2(nx + numW, ringC.Y + r))) UIHelper.DrawTooltip($"{done} of {total} done");
        }

        // ---- note and buttons ----------------------------------------------------------------------------------------
        private float DrawBar(float x, float y, float w)
        {
            int hiddenCount = All.Count(t => t.Type == _tab && Hidden(t.Id));
            var (curDone, _) = Progress(_tab);
            float gap = S(8);

            // right side, laid out right to left
            var items = new List<(string Id, string Label, FontAwesomeIcon? Icon, Kind Kind, Action? Do, string Tip)>();
            if (_editing && hiddenCount > 0)
                items.Add(("rt_unhide", "Show all", FontAwesomeIcon.Eye, Kind.Ghost, () => UnhideAll(_tab), $"Show the {hiddenCount} hidden {_tab} task{(hiddenCount == 1 ? "" : "s")} again"));
            items.Add(("rt_edit", _editing ? "Done" : "Edit", FontAwesomeIcon.SlidersH, _editing ? Kind.Primary : Kind.Ghost, () => { _editing = !_editing; _confirmDelete = null; }, ""));
            items.Add(("rt_add", "Custom", FontAwesomeIcon.Plus, Kind.Ghost, () => OpenDraft(null), "Add a custom task"));
            string? confirmText = null;
            if (_confirmReset)
            {
                confirmText = $"Clear all {_tab} ticks?";
                items.Add(("rt_reset_yes", "Yes", null, Kind.Danger, () => { ResetChecklist(_tab); _confirmReset = false; }, ""));
                items.Add(("rt_reset_no", "No", null, Kind.Ghost, () => _confirmReset = false, ""));
            }
            else items.Add(("rt_reset", "Reset", FontAwesomeIcon.UndoAlt, curDone == 0 ? Kind.Ghost : Kind.Danger,
                curDone == 0 ? null : () => _confirmReset = true, curDone == 0 ? "Nothing ticked yet" : $"Untick every {_tab} task now"));

            float total = items.Sum(i => BtnW(i.Label, i.Icon)) + gap * (items.Count - 1);
            float confirmW = confirmText != null ? Ui.TextSize(confirmText).X + S(10) : 0;
            float rowW = total + confirmW;
            string note = _editing ? "Tap the eye to hide or show a task. Custom tasks can be edited, moved and removed."
                                   : "Ticks clear on their own: daily 15:00 UTC, weekly Tuesday 08:00 UTC.";
            bool ownLine = w - rowW < S(220);
            float by = y;
            if (ownLine)
            {
                Ui.Text(Ui.Dl, new Vector2(x, y), Fit(note, w, 0.88f), Theme.Text3, 0.88f);
                by = y + LH + S(8);
            }
            else Ui.Text(Ui.Dl, new Vector2(x, y + (BtnH - Ui.TextSize("Ag", 0.88f).Y) / 2), Fit(note, w - rowW - S(16), 0.88f), Theme.Text3, 0.88f);

            float bx = Math.Max(x, x + w - rowW);
            if (confirmText != null)
            {
                Ui.Text(Ui.Dl, new Vector2(bx, by + (BtnH - LH) / 2), confirmText, Theme.Text);
                bx += confirmW;
            }
            Action? run = null;
            foreach (var it in items)
            {
                float bw = BtnW(it.Label, it.Icon);
                bool clicked = Ui.Button(it.Id, it.Label, new Vector2(bx, by), new Vector2(bw, BtnH), it.Kind, it.Icon);
                if (it.Tip.Length > 0) Ui.Tooltip(it.Tip);
                if (clicked && it.Do != null) run = it.Do;
                bx += bw + gap;
            }
            run?.Invoke();
            return by + BtnH;
        }

        // ---- category panels -----------------------------------------------------------------------------------------
        private sealed class CatView
        {
            public RoutineCategory Cat = null!;
            public List<RoutineTask> Tasks = new();
        }

        private List<CatView> Shown()
        {
            var shown = All.Where(t => t.Type == _tab && (_editing || !Hidden(t.Id))).ToList();
            var cats = Categories.Where(c => c.Type == _tab)
                .Select(c => new CatView { Cat = c, Tasks = shown.Where(t => t.CategoryId == c.Id).ToList() })
                .Where(c => c.Tasks.Count > 0).ToList();
            // a custom task with an unknown category still shows, under "Custom Tasks"
            var orphans = shown.Where(t => !KnownCategory(t.CategoryId, t.Type)).ToList();
            if (orphans.Count > 0)
            {
                var home = cats.FirstOrDefault(c => c.Cat.Id == $"custom_{_tab}");
                if (home != null) home.Tasks.AddRange(orphans);
                else cats.Add(new CatView { Cat = Categories.First(c => c.Id == $"custom_{_tab}"), Tasks = orphans });
            }
            return cats;
        }

        private float RowH => LH + S(14);
        private float CatH(CatView c) => S(14) + CapsH + S(10) + c.Tasks.Count * RowH + S(10);

        private float DrawCategories(float x, float y, float w)
        {
            var cats = Shown();
            if (cats.Count == 0)
            {
                var dl = Ui.Dl;
                float h = LH * 2 + S(30);
                Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), Theme.Night2.With(0.5f));
                Ui.Icon(dl, new Vector2(x + S(16), y + (h - LH) / 2), FontAwesomeIcon.SlidersH, Theme.Text3, 1.2f);
                Ui.Text(dl, new Vector2(x + S(50), y + (h - LH) / 2), Fit($"No {_tab} tasks shown. Use Edit to show hidden ones, or add a Custom task.", w - S(64)), Theme.Text2);
                return y + h;
            }

            float gap = S(12), minW = S(300);
            int cols = Math.Clamp((int)((w + gap) / (minW + gap)), 1, 3);
            float cw = (w - gap * (cols - 1)) / cols;
            var colY = Enumerable.Repeat(y, cols).ToArray();
            foreach (var c in cats)
            {
                int k = Array.IndexOf(colY, colY.Min());
                float h = CatH(c);
                DrawCategory(c, new Vector2(x + k * (cw + gap), colY[k]), cw, h);
                colY[k] += h + gap;
            }
            return colY.Max() - gap;
        }

        private void DrawCategory(CatView c, Vector2 p, float w, float h)
        {
            var dl = Ui.Dl;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.72f));
            float pad = S(14);
            var visible = c.Tasks.Where(t => !Hidden(t.Id)).ToList();
            int done = visible.Count(t => Ticked(t.Id));
            Ui.Caps(dl, p + new Vector2(pad, pad), c.Cat.Label);
            string aside = $"{done}/{visible.Count}";
            Ui.Text(dl, new Vector2(p.X + w - pad - Ui.TextSize(aside, 0.85f).X, p.Y + pad - S(1)), aside, done == visible.Count && visible.Count > 0 ? Theme.Live : Theme.Text3, 0.85f);
            float ry = p.Y + pad + CapsH + S(10);
            dl.AddLine(new Vector2(p.X + pad, ry - S(5)), new Vector2(p.X + w - pad, ry - S(5)), Theme.CrystalLine.U32(), S(1));

            var customInCat = c.Tasks.Where(t => IsCustom(t.Id)).ToList();
            foreach (var t in c.Tasks)
            {
                var rp = new Vector2(p.X + S(6), ry);
                float rw = w - S(12);
                if (_editing) EditRow(t, c, customInCat, rp, rw);
                else TaskRow(t, c, rp, rw);
                ry += RowH;
            }
        }

        private void TaskRow(RoutineTask t, CatView c, Vector2 p, float w)
        {
            bool on = Ticked(t.Id);
            var size = new Vector2(w, RowH - S(2));
            if (Ui.Hit($"rt_t_{t.Id}", p, size, out float h)) Toggle(t.Id);
            var dl = Ui.Dl;
            if (h > 0.01f) dl.AddRectFilled(p, p + size, Theme.Text.With(0.05f * h).U32(), S(6));

            float box = S(18), bx = p.X + S(8), by = p.Y + (size.Y - box) / 2;
            if (on)
            {
                dl.AddRectFilled(new Vector2(bx, by), new Vector2(bx + box, by + box), Theme.Gold.U32(), S(4));
                Ui.IconCentered(dl, new Vector2(bx + box / 2, by + box / 2), FontAwesomeIcon.Check.ToIconString(), Theme.Night0, 0.7f);
            }
            else dl.AddRect(new Vector2(bx, by), new Vector2(bx + box, by + box), UIHelper.LerpColor(Theme.Text3, Theme.Gold, h).U32(), S(4), 0, S(1.5f));

            float lx = bx + box + S(12);
            float tagW = 0;
            if (IsCustom(t.Id) && c.Cat.Id != $"custom_{_tab}")
            {
                var ts = Ui.TextSize("custom", 0.72f);
                tagW = ts.X + S(12);
                var tp = new Vector2(p.X + w - tagW - S(6), p.Y + (size.Y - ts.Y - S(4)) / 2);
                dl.AddRectFilled(tp, tp + new Vector2(tagW, ts.Y + S(4)), Theme.Violet.With(0.14f).U32(), S(4));
                Ui.Text(dl, tp + S(6, 2), "custom", Theme.Violet, 0.72f);
                tagW += S(12);
            }
            string label = Fit(t.Label, p.X + w - lx - tagW - S(6));
            float ly = p.Y + (size.Y - LH) / 2;
            Ui.Text(dl, new Vector2(lx, ly), label, on ? Theme.Text3 : Theme.Text);
            if (on) dl.AddLine(new Vector2(lx, ly + LH / 2 + S(1)), new Vector2(lx + Ui.TextSize(label).X, ly + LH / 2 + S(1)), Theme.Text3.With(0.8f).U32(), S(1));
        }

        private void EditRow(RoutineTask t, CatView c, List<RoutineTask> customInCat, Vector2 p, float w)
        {
            var dl = Ui.Dl;
            bool hidden = Hidden(t.Id);
            float rh = RowH - S(2), ib = S(26), iy = p.Y + (rh - ib) / 2;
            if (Ui.IconButton($"rt_eye_{t.Id}", hidden ? FontAwesomeIcon.EyeSlash : FontAwesomeIcon.Eye, new Vector2(p.X + S(2), iy), ib,
                    hidden ? "Hidden: tap to show" : "Shown: tap to hide", hidden ? Theme.Text3 : Theme.Crystal))
                ToggleHidden(t.Id);

            float lx = p.X + ib + S(10), right = p.X + w;
            int ci = customInCat.IndexOf(t);
            if (ci >= 0)
            {
                if (_confirmDelete == t.Id)
                {
                    float yw = BtnW("Yes", null), nw = BtnW("No", null), bh = Math.Min(BtnH, rh);
                    float bx = right - yw - nw - S(6);
                    float by = p.Y + (rh - bh) / 2;
                    bool yes = Ui.Button($"rt_del_yes_{t.Id}", "Yes", new Vector2(bx, by), new Vector2(yw, bh), Kind.Danger);
                    bool no = Ui.Button($"rt_del_no_{t.Id}", "No", new Vector2(bx + yw + S(6), by), new Vector2(nw, bh));
                    string q = "Remove?";
                    right = bx - Ui.TextSize(q).X - S(8);
                    Ui.Text(dl, new Vector2(right + S(4), p.Y + (rh - LH) / 2), q, Theme.Text);
                    if (yes) { RemoveCustom(t.Id); _confirmDelete = null; return; }
                    if (no) _confirmDelete = null;
                }
                else
                {
                    float bx = right - ib * 4 - S(6);
                    bool up = ci > 0, down = ci < customInCat.Count - 1;
                    if (Ui.IconButton($"rt_up_{t.Id}", FontAwesomeIcon.ChevronUp, new Vector2(bx, iy), ib, "Move up", up ? null : Theme.Text3.With(0.4f)) && up) Move(t.Id, -1);
                    if (Ui.IconButton($"rt_dn_{t.Id}", FontAwesomeIcon.ChevronDown, new Vector2(bx + ib, iy), ib, "Move down", down ? null : Theme.Text3.With(0.4f)) && down) Move(t.Id, 1);
                    if (Ui.IconButton($"rt_ed_{t.Id}", FontAwesomeIcon.PencilAlt, new Vector2(bx + ib * 2, iy), ib, "Rename or move")) OpenDraft(t);
                    if (Ui.IconButton($"rt_rm_{t.Id}", FontAwesomeIcon.TrashAlt, new Vector2(bx + ib * 3, iy), ib, "Remove", Theme.Bad)) _confirmDelete = t.Id;
                    right = bx;
                }
            }
            Ui.Text(dl, new Vector2(lx, p.Y + (rh - LH) / 2), Fit(t.Label, right - lx - S(6)), hidden ? Theme.Text3 : Theme.Text);
        }

        // ---- retainers (plugin only) ---------------------------------------------------------------------------------
        private unsafe float DrawRetainers(float x, float y, float w)
        {
            var dl = Ui.Dl;
            Ui.Icon(dl, new Vector2(x, y - S(1)), FontAwesomeIcon.Briefcase, Theme.Gold, 0.8f);
            float tw = Ui.Caps(dl, new Vector2(x + Ui.IconSize(FontAwesomeIcon.Briefcase, 0.8f).X + S(8), y), "Retainer ventures");
            if (PluginUI.RetainersKnown && PluginUI.ActiveRetainersCount > 0)
            {
                int act = PluginUI.ActiveRetainersCount, ret = PluginUI.ReturnedRetainersCount;
                Ui.Text(dl, new Vector2(x + tw + S(34), y - S(1)), $"{ret}/{act} back", ret == act ? Theme.Live : Theme.Text3, 0.82f);
            }
            y += CapsH + S(10);

            var rm = FFXIVClientStructs.FFXIV.Client.Game.RetainerManager.Instance();
            var list = new List<(string Name, int Items, uint Venture, long End)>();
            if (rm != null && rm->IsReady)
                for (uint i = 0; i < 10; i++)
                {
                    var r = rm->GetRetainerBySortedIndex(i);
                    if (r == null || r->RetainerId == 0 || string.IsNullOrEmpty(r->NameString)) continue;
                    list.Add((r->NameString, r->ItemCount, r->VentureId, r->VentureComplete));
                }

            if (list.Count == 0)
            {
                float h = LH + S(24);
                Ui.Panel(dl, new Vector2(x, y), new Vector2(x + w, y + h), Theme.Night2.With(0.5f));
                Ui.Icon(dl, new Vector2(x + S(14), y + (h - LH) / 2), FontAwesomeIcon.InfoCircle, Theme.Text3);
                Ui.Text(dl, new Vector2(x + S(40), y + (h - LH) / 2), Fit("Visit a summoning bell once to see your ventures here.", w - S(54)), Theme.Text2);
                return y + h;
            }

            float gap = S(10), minW = S(210);
            int cols = Math.Clamp((int)((w + gap) / (minW + gap)), 1, 5);
            float cw = (w - gap * (cols - 1)) / cols, ch = S(12) + LH + S(8) + S(5) + S(8) + LH + S(12);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for (int i = 0; i < list.Count; i++)
            {
                var (name, items, venture, end) = list[i];
                var p = new Vector2(x + (i % cols) * (cw + gap), y + (i / cols) * (ch + gap));
                bool back = venture != 0 && end - now <= 0;
                Ui.Panel(dl, p, p + new Vector2(cw, ch), Theme.Night1.With(0.8f), back ? Theme.Live.With(0.45f) : Theme.CrystalLine, 8f);
                float pad = S(12);
                Ui.Text(dl, p + new Vector2(pad, pad), Fit(name, cw - pad * 2), Theme.Text);
                var barP = p + new Vector2(pad, pad + LH + S(8));
                float frac = Math.Min(1f, items / 175f);
                Ui.Progress(dl, barP, cw - pad * 2, frac, frac >= 0.9f ? Theme.Warn : Ui.Accent, 5f);
                if (ImGui.IsMouseHoveringRect(barP - S(0, 4), barP + new Vector2(cw - pad * 2, S(9)))) UIHelper.DrawTooltip($"Inventory: {items} / 175");
                var sp = barP + new Vector2(0, S(5) + S(8));
                if (venture == 0) Ui.Text(dl, sp, "No venture", Theme.Text3, 0.92f);
                else if (back) Ui.Text(dl, sp, "Back", Theme.Live, 0.92f);
                else
                {
                    var ts = TimeSpan.FromSeconds(end - now);
                    Ui.Text(dl, sp, $"{(int)ts.TotalHours:00}h {ts.Minutes:00}m {ts.Seconds:00}s", Theme.Text, 0.92f);
                }
            }
            int rows = (list.Count + cols - 1) / cols;
            return y + rows * ch + (rows - 1) * gap;
        }

        // ---- add / edit dialog ---------------------------------------------------------------------------------------
        private void OpenDraft(RoutineTask? t)
        {
            _draft = t == null
                ? new Draft { Type = _tab, Category = $"custom_{_tab}" }
                : new Draft { Id = t.Id, Label = t.Label, Type = t.Type, Category = KnownCategory(t.CategoryId, t.Type) ? t.CategoryId : $"custom_{t.Type}" };
            _focusDraft = true;
        }

        private void DrawDialog()
        {
            var d = _draft!;
            var dl = Ui.Dl;
            var wp = ImGui.GetWindowPos();
            var ws = ImGui.GetWindowSize();
            dl.AddRectFilled(wp, wp + ws, Theme.Night0.With(0.6f).U32());

            float pad = S(18), dw = Math.Min(S(440), ws.X - S(32));
            float fieldH = ImGui.GetFontSize() + S(7) * 2;
            float dh = pad + LH * 1.1f + S(16) + (LH * 0.9f + S(6) + fieldH + S(14)) * 3 + BtnH + pad;
            var p = wp + new Vector2((ws.X - dw) / 2, Math.Max(S(16), (ws.Y - dh) / 2));
            Ui.Panel(dl, p, p + new Vector2(dw, dh), Theme.Night1.With(0.98f), Theme.Gold.With(0.45f), 12f);

            float x = p.X + pad, y = p.Y + pad, iw = dw - pad * 2;
            Gem(dl, new Vector2(x + S(5), y + LH * 1.1f / 2), S(5), Theme.Gold);
            Ui.Text(dl, new Vector2(x + S(18), y), d.Id != null ? "Edit custom task" : "Add custom task", Theme.Text, 1.1f);
            bool close = Ui.IconButton("rt_dlg_x", FontAwesomeIcon.Times, new Vector2(p.X + dw - pad - S(26), y - S(2)), S(26), "Close");
            y += LH * 1.1f + S(16);

            bool ok = d.Label.Trim().Length > 0 && KnownCategory(d.Category, d.Type);
            bool submit = false;

            Ui.Text(dl, new Vector2(x, y), "Task name", Theme.Text2, 0.9f);
            y += LH * 0.9f + S(6);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            PushFrame();
            ImGui.SetNextItemWidth(iw);
            if (_focusDraft) { ImGui.SetKeyboardFocusHere(); _focusDraft = false; }
            if (ImGui.InputTextWithHint("##rt_label", "e.g. Water FC garden", ref d.Label, 80, ImGuiInputTextFlags.EnterReturnsTrue)) submit = true;
            PopFrame();
            y += fieldH + S(14);

            Ui.Text(dl, new Vector2(x, y), "Resets", Theme.Text2, 0.9f);
            y += LH * 0.9f + S(6);
            float sw = (iw - S(6)) / 2;
            foreach (var (k, i) in new[] { ("daily", 0), ("weekly", 1) })
            {
                var sp = new Vector2(x + i * (sw + S(6)), y);
                if (Ui.Button("rt_dlg_" + k, k == "daily" ? "Daily" : "Weekly", sp, new Vector2(sw, fieldH), d.Type == k ? Kind.Gold : Kind.Ghost) && d.Type != k)
                {
                    d.Type = k;
                    d.Category = $"custom_{k}";
                }
            }
            y += fieldH + S(14);

            Ui.Text(dl, new Vector2(x, y), "Category", Theme.Text2, 0.9f);
            y += LH * 0.9f + S(6);
            ImGui.SetCursorScreenPos(new Vector2(x, y));
            PushFrame();
            ImGui.SetNextItemWidth(iw);
            var cur = Categories.FirstOrDefault(c => c.Id == d.Category && c.Type == d.Type);
            if (ImGui.BeginCombo("##rt_cat", cur?.Label ?? "Pick a category"))
            {
                foreach (var c in Categories.Where(c => c.Type == d.Type))
                    if (ImGui.Selectable(c.Label + "##" + c.Id, c.Id == d.Category)) d.Category = c.Id;
                ImGui.EndCombo();
            }
            PopFrame();
            y += fieldH + S(14);

            string main = d.Id != null ? "Save" : "Add task";
            float mw = BtnW(main, null), cw = BtnW("Cancel", null);
            float bx = p.X + dw - pad - mw;
            if (Ui.Button("rt_dlg_ok", main, new Vector2(bx, y), new Vector2(mw, BtnH), ok ? Kind.Gold : Kind.Ghost)) submit = true;
            if (Ui.Button("rt_dlg_cancel", "Cancel", new Vector2(bx - S(8) - cw, y), new Vector2(cw, BtnH))) close = true;

            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) close = true;
            if (submit && ok) { SaveDraft(d); close = true; }
            if (close) _draft = null;
        }

        private static void PushFrame()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(9, 7));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(6));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Night2.With(0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Night3);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Night3);
            ImGui.PushStyleColor(ImGuiCol.Border, Theme.CrystalLine);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, Theme.Text3);
            ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Night1.With(0.98f));
            ImGui.PushStyleColor(ImGuiCol.Header, Ui.Accent.With(0.22f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Ui.Accent.With(0.3f));
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Night3);
        }

        private static void PopFrame()
        {
            ImGui.PopStyleColor(11);
            ImGui.PopStyleVar(4);
        }
    }
}
