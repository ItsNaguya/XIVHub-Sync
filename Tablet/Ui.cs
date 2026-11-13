using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace XIVHubCompanion.Tablet
{
    public enum Kind { Primary, Ghost, Gold, Danger }

    /// <summary>
    /// Shared building blocks in the website's Aetheryte look (cut-corner panels, crystal hairlines, gold small caps).
    /// Everything is drawn at <see cref="PluginUI.AppScale"/>; pass unscaled numbers.
    /// </summary>
    public static class Ui
    {
        public static float S(float v) => v * PluginUI.AppScale;
        public static Vector2 S(float x, float y) => new(x * PluginUI.AppScale, y * PluginUI.AppScale);
        public static ImDrawListPtr Dl => ImGui.GetWindowDrawList();

        /// <summary>The shell's accent (Settings > Appearance): crystal, gold or violet.</summary>
        public static Vector4 Accent = Theme.Crystal;

        // ---- shapes -----------------------------------------------------------------------------------------------
        /// <summary>Panel with the website's cut top-left and bottom-right corners.</summary>
        public static void Panel(ImDrawListPtr dl, Vector2 a, Vector2 b, Vector4? fill = null, Vector4? line = null, float cut = 10f)
        {
            float c = Math.Min(S(cut), Math.Min(b.X - a.X, b.Y - a.Y) / 3);
            Span<Vector2> pts = stackalloc Vector2[6];
            pts[0] = new(a.X + c, a.Y); pts[1] = new(b.X, a.Y); pts[2] = new(b.X, b.Y - c);
            pts[3] = new(b.X - c, b.Y); pts[4] = new(a.X, b.Y); pts[5] = new(a.X, a.Y + c);
            dl.AddConvexPolyFilled(ref pts[0], 6, (fill ?? Theme.Night2.With(0.82f)).U32());
            dl.AddPolyline(ref pts[0], 6, (line ?? Theme.CrystalLine).U32(), ImDrawFlags.Closed, S(1f));
        }

        public static void Dot(ImDrawListPtr dl, Vector2 center, float r, Vector4 color, bool pulse = false)
        {
            if (pulse)
            {
                float t = (float)((ImGui.GetTime() * 1.6) % 1.0);
                dl.AddCircleFilled(center, S(r) * (1 + t * 1.6f), color.With(0.35f * (1 - t)).U32());
            }
            dl.AddCircleFilled(center, S(r), color.U32());
        }

        public static void Progress(ImDrawListPtr dl, Vector2 a, float width, float frac, Vector4 color, float h = 4f)
        {
            var b = a + new Vector2(width, S(h));
            dl.AddRectFilled(a, b, Theme.Night0.With(0.8f).U32(), S(h));
            if (frac > 0) dl.AddRectFilled(a, a + new Vector2(width * Math.Clamp(frac, 0, 1), S(h)), color.U32(), S(h));
        }

        // ---- text -------------------------------------------------------------------------------------------------
        public static Vector2 TextSize(string text, float scale = 1f) => Fonts.Measure(text, scale);

        public static void Text(ImDrawListPtr dl, Vector2 pos, string text, Vector4 color, float scale = 1f)
            => Fonts.Text(dl, pos, text, color.U32(), scale);

        /// <summary>Gold small caps with letter spacing (website section headers).</summary>
        public static float Caps(ImDrawListPtr dl, Vector2 pos, string text, Vector4? color = null, float scale = 0.78f)
        {
            float x = pos.X, gap = S(1.4f);
            foreach (var ch in text.ToUpperInvariant())
            {
                var s = ch.ToString();
                Text(dl, new Vector2(x, pos.Y), s, color ?? Theme.Gold, scale);
                x += TextSize(s, scale).X + gap;
            }
            return x - pos.X;
        }

        public static Vector2 IconSize(FontAwesomeIcon icon, float scale = 1f) => Fonts.Measure(icon.ToIconString(), scale, icon: true);

        public static void Icon(ImDrawListPtr dl, Vector2 pos, FontAwesomeIcon icon, Vector4 color, float scale = 1f)
            => Fonts.Text(dl, pos, icon.ToIconString(), color.U32(), scale, icon: true);

        public static void IconCentered(ImDrawListPtr dl, Vector2 center, string glyph, Vector4 color, float scale = 1f)
            => Fonts.Text(dl, center - Fonts.Measure(glyph, scale, icon: true) / 2, glyph, color.U32(), scale, icon: true);

        public static void Tooltip(string text)
        {
            if (!ImGui.IsItemHovered()) return;
            UIHelper.DrawTooltip(text);
        }

        // ---- interaction ------------------------------------------------------------------------------------------
        /// <summary>An invisible hit area with an eased hover value (0..1). Returns clicked.</summary>
        public static bool Hit(string id, Vector2 pos, Vector2 size, out float hover)
        {
            ImGui.SetCursorScreenPos(pos);
            bool clicked = ImGui.InvisibleButton(id, size);
            ImGui.SetItemAllowOverlap();
            hover = UIHelper.GetHoverState(ImGui.GetID(id), ImGui.IsItemHovered(), 12f);
            if (ImGui.IsItemHovered()) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            return clicked;
        }

        public static bool Button(string id, string label, Vector2 pos, Vector2 size, Kind kind = Kind.Ghost, FontAwesomeIcon? icon = null)
        {
            bool clicked = Hit(id, pos, size, out float h);
            var dl = Dl;
            var col = kind switch { Kind.Primary => Accent, Kind.Gold => Theme.Gold, Kind.Danger => Theme.Bad, _ => Theme.Text2 };
            var fill = kind == Kind.Ghost ? Theme.Night2.With(0.6f + 0.3f * h) : col.With(0.16f + 0.18f * h);
            Panel(dl, pos, pos + size, fill, col.With(kind == Kind.Ghost ? 0.25f + 0.35f * h : 0.55f + 0.35f * h), 6f);
            var text = kind == Kind.Ghost ? UIHelper.LerpColor(Theme.Text2, Theme.Text, h) : UIHelper.LerpColor(col, Theme.Text, h * 0.6f);
            float iconW = icon.HasValue ? IconSize(icon.Value, 0.9f).X + S(6) : 0;
            var ts = TextSize(label);
            var start = pos + new Vector2((size.X - ts.X - iconW) / 2, (size.Y - ts.Y) / 2);
            if (icon.HasValue) Icon(dl, start + new Vector2(0, S(1)), icon.Value, text, 0.9f);
            Text(dl, start + new Vector2(iconW, 0), label, text);
            return clicked;
        }

        public static bool IconButton(string id, FontAwesomeIcon icon, Vector2 pos, float size, string? tooltip = null, Vector4? color = null, bool active = false)
        {
            bool clicked = Hit(id, pos, new Vector2(size, size), out float h);
            if (tooltip != null) Tooltip(tooltip);
            var dl = Dl;
            if (h > 0.01f || active) dl.AddRectFilled(pos, pos + new Vector2(size, size), (active ? Accent.With(0.18f + 0.1f * h) : Theme.Text.With(0.08f * h)).U32(), S(6));
            IconCentered(dl, pos + new Vector2(size, size) / 2, icon.ToIconString(), UIHelper.LerpColor(color ?? Theme.Text2, active ? Accent : Theme.Text, Math.Max(h, active ? 1 : 0)), 0.95f);
            return clicked;
        }

        public static void Chip(ImDrawListPtr dl, Vector2 pos, string text, Vector4 color, out Vector2 size)
        {
            var ts = TextSize(text, 0.82f);
            size = ts + S(14, 6);
            dl.AddRectFilled(pos, pos + size, color.With(0.14f).U32(), size.Y / 2);
            dl.AddRect(pos, pos + size, color.With(0.4f).U32(), size.Y / 2, 0, S(1));
            Text(dl, pos + S(7, 3), text, color, 0.82f);
        }

        // ---- formatting -------------------------------------------------------------------------------------------
        public static string Span(TimeSpan t) => t.TotalSeconds <= 0 ? "now"
            : t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h"
            : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
            : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s" : $"{t.Seconds}s";

        public static string Gil(long v) => v >= 1_000_000 ? $"{v / 1_000_000.0:0.##}M" : v >= 10_000 ? $"{v / 1000.0:0.#}k" : v.ToString("N0");

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes}m ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours}h ago" : $"{(int)d.TotalDays}d ago";
        }
    }
}
