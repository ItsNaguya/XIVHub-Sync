using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion.Apps
{
    // Small tablet widgets (same look as Market's and Gathering's)
    public partial class CollectionApp
    {
        private static float S(float v) => Ui.S(v);
        private static Vector2 S(float x, float y) => Ui.S(x, y);
        private static float LH => Ui.TextSize("Ag").Y;
        private static float FrameH => ImGui.GetFontSize() + S(7) * 2;

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

        private static bool Input(string id, ref string value, string hint, float width, Vector2 pos, int max = 100)
        {
            ImGui.SetCursorScreenPos(pos);
            PushFrame();
            ImGui.SetNextItemWidth(width);
            bool r = ImGui.InputTextWithHint(id, hint, ref value, max);
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

        /// <summary>A small coloured label; returns its width.</summary>
        private static float Tag(Vector2 pos, string text, Vector4 color, float scale = 0.78f)
        {
            var dl = Ui.Dl;
            var size = Ui.TextSize(text, scale) + S(12, 6);
            dl.AddRectFilled(pos, pos + size, color.With(0.14f).U32(), size.Y / 2);
            dl.AddRect(pos, pos + size, color.With(0.45f).U32(), size.Y / 2, 0, S(1));
            Ui.Text(dl, pos + S(6, 3), text, color, scale);
            return size.X;
        }

        private static float TagH(float scale = 0.78f) => Ui.TextSize("Ag", scale).Y + S(6);

        /// <summary>Tag with a hover tooltip; returns its width.</summary>
        private static float TipTag(string id, Vector2 pos, string text, Vector4 color, string tooltip)
        {
            float w = Tag(pos, text, color);
            ImGui.SetCursorScreenPos(pos);
            ImGui.InvisibleButton(id, new Vector2(w, TagH()));
            ImGui.SetItemAllowOverlap();
            Ui.Tooltip(tooltip);
            return w;
        }

        private void GameIcon(uint id, Vector2 pos, float size, float alpha = 1f)
        {
            if (id == 0) { Ui.Dl.AddRectFilled(pos, pos + new Vector2(size, size), Theme.Night1.U32(), S(4)); return; }
            try
            {
                var wrap = _textureProvider.GetFromGameIcon(new GameIconLookup(id)).GetWrapOrDefault();
                if (wrap != null) { Ui.Dl.AddImage(wrap.Handle, pos, pos + new Vector2(size, size), Vector2.Zero, Vector2.One, new Vector4(1, 1, 1, alpha).U32()); return; }
            }
            catch { /* missing icon */ }
            Ui.Dl.AddRectFilled(pos, pos + new Vector2(size, size), Theme.Night1.U32(), S(4));
        }

        private static string Fit(string text, float width, float scale = 1f)
        {
            if (string.IsNullOrEmpty(text) || Ui.TextSize(text, scale).X <= width) return text ?? "";
            if (width <= 0) return "";
            while (text.Length > 1 && Ui.TextSize(text + "...", scale).X > width) text = text[..^1];
            return text + "...";
        }

        private static int Pct(int a, int b) => b > 0 ? (int)Math.Round(a * 100.0 / b) : 0;

        private static void Empty(Vector2 p, float w, string text, FontAwesomeIcon icon = FontAwesomeIcon.InfoCircle)
        {
            var dl = Ui.Dl;
            float h = EmptyH;
            Ui.Panel(dl, p, p + new Vector2(w, h), Theme.Night2.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), (h - LH) / 2), icon, Theme.Text3);
            Ui.Text(dl, p + new Vector2(S(40), (h - LH) / 2), Fit(text, w - S(54)), Theme.Text2);
        }
        private static float EmptyH => LH + S(24);

        /// <summary>A warning panel; the text wraps. Returns the height used.</summary>
        private static float Message(Vector2 p, float w, string text, Vector4 color)
        {
            var dl = Ui.Dl;
            var lines = Wrap(text, w - S(54), 0.9f);
            float lh = Ui.TextSize("Ag", 0.9f).Y, h = lines.Count * lh + S(20);
            Ui.Panel(dl, p, p + new Vector2(w, h), color.With(0.1f), color.With(0.5f));
            Ui.Icon(dl, p + new Vector2(S(14), S(10)), FontAwesomeIcon.ExclamationTriangle, color);
            for (int i = 0; i < lines.Count; i++) Ui.Text(dl, p + new Vector2(S(40), S(10) + i * lh), lines[i], Theme.Text, 0.9f);
            return h;
        }

        private static System.Collections.Generic.List<string> Wrap(string text, float width, float scale)
        {
            var lines = new System.Collections.Generic.List<string>();
            var line = "";
            foreach (var word in text.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Ui.TextSize(next, scale).X > width) { lines.Add(line); line = word; }
                else line = next;
            }
            if (line.Length > 0) lines.Add(line);
            return lines;
        }
    }
}
