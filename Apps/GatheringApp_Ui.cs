using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Small tablet widgets (same look as Market's and Crafting's)
    public partial class GatheringApp
    {
        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float FrameH => ImGui.GetFontSize() + S(7) * 2;
        private static float CapsH => Ui.TextSize("A", 0.78f).Y;

        private static readonly Vector4 ColLegendary = Theme.Bad, ColUnspoiled = Theme.Crystal, ColEphemeral = Theme.Violet, ColHidden = Theme.Warn;
        private static Vector4 TypeColor(string t) => t switch { "Legendary" => ColLegendary, "Ephemeral" => ColEphemeral, "Hidden" => ColHidden, _ => ColUnspoiled };

        private static void Advance(Vector2 p, float h)
        {
            ImGui.SetCursorScreenPos(p);
            ImGui.Dummy(new Vector2(S(1), Math.Max(1, h)));
        }

        /// <summary>True when [y, y+h] is inside the current window's visible area (rows outside are skipped).</summary>
        private static bool Visible(float y, float h)
        {
            float top = ImGui.GetWindowPos().Y, bottom = top + ImGui.GetWindowHeight();
            return y + h >= top - S(40) && y <= bottom + S(40);
        }

        private static void PushFrame()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, S(9, 7));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, S(6));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, S(6));
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Night1.With(0.9f));
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Night2);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Night2);
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

        private static bool Input(string id, ref string value, string hint, float width, Vector2 pos, int max = 100, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
        {
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            bool r = ImGui.InputTextWithHint(id, hint, ref value, max, flags);
            PopFrame();
            return r;
        }

        /// <summary>A combo of (value, label) pairs; returns the picked value or null.</summary>
        private static string? Combo(string id, string current, (string Value, string Label)[] options, float width, Vector2 pos, string tooltip = "")
        {
            string? picked = null;
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            string label = Array.Find(options, o => o.Value == current).Label ?? current;
            if (ImGui.BeginCombo(id, label))
            {
                foreach (var (v, l) in options)
                    if (ImGui.Selectable(l + "##" + v, v == current) && v != current) picked = v;
                ImGui.EndCombo();
            }
            PopFrame();
            if (tooltip.Length > 0) Ui.Tooltip(tooltip);
            return picked;
        }

        private static bool ChipToggle(string id, string text, bool active, ref Vector2 at, float left, float right, float scale = 0.85f, string tooltip = "")
        {
            var size = Ui.TextSize(text, scale) + S(16, 8);
            if (at.X + size.X > right && at.X > left) at = new Vector2(left, at.Y + size.Y + S(6));
            bool clicked = Ui.Hit(id, at, size, out float h);
            if (tooltip.Length > 0) Ui.Tooltip(tooltip);
            var dl = Ui.Dl;
            var col = active ? Ui.Accent : Theme.Text2;
            dl.AddRectFilled(at, at + size, (active ? col.With(0.2f + 0.1f * h) : Theme.Night1.With(0.7f + 0.2f * h)).U32(), size.Y / 2);
            dl.AddRect(at, at + size, col.With(active ? 0.7f : 0.2f + 0.3f * h).U32(), size.Y / 2, 0, S(1));
            Ui.Text(dl, at + S(8, 4), text, active ? Theme.Text : UIHelper.LerpColor(Theme.Text2, Theme.Text, h), scale);
            at.X += size.X + S(6);
            return clicked;
        }

        private static float ChipH(float scale = 0.85f) => Ui.TextSize("Ag", scale).Y + S(8);

        /// <summary>A small coloured label (node type, Hidden, Big fish...); returns its width.</summary>
        private static float Tag(Vector2 pos, string text, Vector4 color, float scale = 0.72f)
        {
            var dl = Ui.Dl;
            var ts = Ui.TextSize(text, scale);
            var size = ts + S(10, 4);
            dl.AddRectFilled(pos, pos + size, color.With(0.14f).U32(), S(4));
            dl.AddRect(pos, pos + size, color.With(0.45f).U32(), S(4), 0, S(1));
            Ui.Text(dl, pos + S(5, 2), text, color, scale);
            return size.X;
        }

        private static float TagH(float scale = 0.72f) => Ui.TextSize("Ag", scale).Y + S(4);

        /// <summary>[-] n [+]; returns true when the value changed. Width is StepperW.</summary>
        private static bool Stepper(string id, ref int value, Vector2 pos, int min = 1, int max = 9999)
        {
            float b = S(26);
            bool changed = false;
            if (Ui.IconButton(id + "_m", FontAwesomeIcon.Minus, pos, b) && value > min) { value--; changed = true; }
            var txt = value.ToString();
            var ts = Ui.TextSize(txt);
            Ui.Dl.AddRectFilled(pos + new Vector2(b + S(2), 0), pos + new Vector2(b + S(52), b), Theme.Night1.With(0.8f).U32(), S(4));
            Ui.Text(Ui.Dl, pos + new Vector2(b + (S(54) - ts.X) / 2, (b - ts.Y) / 2), txt, Theme.Text);
            if (Ui.IconButton(id + "_p", FontAwesomeIcon.Plus, pos + new Vector2(b + S(54), 0), b) && value < max) { value++; changed = true; }
            // right-click on the number types an exact amount
            ImGui.SetCursorScreenPos(pos + new Vector2(b + S(2), 0));
            ImGui.InvisibleButton(id + "_n", new Vector2(S(50), b));
            ImGui.SetItemAllowOverlap();
            if (ImGui.IsItemHovered()) UIHelper.DrawTooltip("Right-click to type an amount");
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) { _typeBuf = value.ToString(); ImGui.OpenPopup(id + "_pop"); }
            if (ImGui.BeginPopup(id + "_pop"))
            {
                ImGui.SetNextItemWidth(S(90));
                if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
                if (ImGui.InputText("##amt", ref _typeBuf, 5, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    if (int.TryParse(_typeBuf, out var nv)) { nv = Math.Clamp(nv, min, max); if (nv != value) { value = nv; changed = true; } }
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            return changed;
        }

        private static float StepperW => S(106);
        private static string _typeBuf = "";

        /// <summary>A small button sized to its label; flows onto the next line inside [left, right].</summary>
        private static bool FlowButton(string id, string label, FontAwesomeIcon? icon, Kind kind, ref Vector2 at, float left, float right, float h)
        {
            float w = Ui.TextSize(label).X + (icon.HasValue ? Ui.IconSize(icon.Value, 0.9f).X + S(6) : 0) + S(24);
            if (at.X + w > right && at.X > left) at = new Vector2(left, at.Y + h + S(6));
            bool r = Ui.Button(id, label, at, new Vector2(w, h), kind, icon);
            at.X += w + S(6);
            return r;
        }

        // website icons: v2.xivapi.com/api/asset?path=ui/icon/020000/020001.tex&format=png (older: .../020001.png or a bare id)
        private static readonly Regex IconRx = new(@"(\d+)(?:_hr1)?\.(?:tex|png)|(\d+)\s*$", RegexOptions.Compiled);
        private static uint IconId(string? s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = IconRx.Match(s);
            if (!m.Success) return 0;
            var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return uint.TryParse(g, out var id) ? id : 0;
        }

        private static string IconUrl(uint icon)
        {
            if (icon == 0) return "";
            string pad = icon.ToString("D6"), folder = (icon / 1000 * 1000).ToString("D6");
            return $"https://v2.xivapi.com/api/asset?path=ui/icon/{folder}/{pad}.tex&format=png";
        }

        private void GameIcon(uint id, Vector2 pos, float size, float alpha = 1f)
        {
            if (id == 0) { Ui.Dl.AddRectFilled(pos, pos + new Vector2(size, size), Theme.Night1.U32(), S(4)); return; }
            var wrap = _textureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrDefault();
            if (wrap != null) Ui.Dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size), Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, alpha).U32());
        }

        private void GameIcon(string? icon, Vector2 pos, float size, float alpha = 1f) => GameIcon(IconId(icon), pos, size, alpha);

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || Ui.TextSize(text, scale).X <= width) return text ?? "";
            if (width <= 0) return "";
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }

        private static string Plural(int n, string word) => $"{n:N0} {word}{(n == 1 ? "" : "s")}";
        private static string Hours(IEnumerable<int> hours) => string.Join(" · ", System.Linq.Enumerable.Select(hours, h => $"{h:00}:00"));

        /// <summary>Text pieces separated by " · ", cut to the width.</summary>
        private static void Meta(Vector2 pos, float width, IEnumerable<string> parts, Vector4 color, float scale = 0.85f)
        {
            var text = string.Join("  ·  ", System.Linq.Enumerable.Where(parts, s => !string.IsNullOrEmpty(s)));
            Ui.Text(Ui.Dl, pos, Fit(text, width, scale), color, scale);
        }

        private static void Empty(Vector2 p, float w, string text, FontAwesomeIcon icon = FontAwesomeIcon.InfoCircle)
        {
            var dl = Ui.Dl;
            float h = EmptyH;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), (h - LH) / 2), icon, Theme.Text3);
            Ui.Text(dl, p + new Vector2(S(40), (h - LH) / 2), Fit(text, w - S(54)), Theme.Text2);
        }
        private static float EmptyH => LH + S(24);

        private static void Message(Vector2 p, float w, string text, Vector4 color)
        {
            var dl = Ui.Dl;
            float h = LH + S(20);
            Ui.Panel(dl, p, p + new Vector2(w, h), color.With(0.1f), color.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), (h - LH) / 2), FontAwesomeIcon.ExclamationTriangle, color);
            Ui.Text(dl, p + new Vector2(S(40), (h - LH) / 2), Fit(text, w - S(54)), Theme.Text);
        }

        /// <summary>Gold caps title with a count on the right of it; returns the height used.</summary>
        private static float GroupTitle(Vector2 p, string title, int? count = null, Vector4? color = null)
        {
            float tw = Ui.Caps(Ui.Dl, p, title, color);
            if (count.HasValue) Ui.Text(Ui.Dl, p + new Vector2(tw + S(10), -S(1)), count.Value.ToString("N0"), Theme.Text3, 0.82f);
            return CapsH + S(8);
        }

        /// <summary>"×12" (have) or "12/40" (have/need), green when enough.</summary>
        private static float HeldChip(Vector2 pos, int have, int? need = null)
        {
            if (need == null && have <= 0) return 0;
            string t = need.HasValue ? $"{have}/{need}" : $"×{have}";
            var col = need.HasValue ? (have >= need ? Theme.Live : have > 0 ? Theme.Warn : Theme.Text3) : Theme.Live;
            float w = Tag(pos, t, col, 0.78f);
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton($"##held{pos.X}_{pos.Y}", new Vector2(w, TagH(0.78f)));
            ImGui.SetItemAllowOverlap();
            Ui.Tooltip(have > 0 ? $"You have {have} in your bags." : "You have none in your bags.");
            return w;
        }
    }
}
