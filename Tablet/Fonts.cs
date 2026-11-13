using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;

namespace XIVHubCompanion.Tablet
{
    /// <summary>
    /// Crisp large text. ImGui bakes each font at one pixel size, and drawing it bigger stretches the bitmap
    /// (pixelated clock, numbers, headings). We bake the default and the icon font at a few larger sizes and,
    /// for any text above the normal size, draw with the next larger one scaled down.
    /// </summary>
    public static class Fonts
    {
        private static readonly float[] Mults = { 1.25f, 1.6f, 2.2f, 3.2f };
        private static IFontHandle[] _text = Array.Empty<IFontHandle>();
        private static IFontHandle[] _icon = Array.Empty<IFontHandle>();
        private static float _basePx;

        public static void Init(IUiBuilder ui)
        {
            _basePx = ui.FontDefaultSizePx;
            _text = Mults.Select(m => ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(_basePx * m, null)))).ToArray();
            _icon = Mults.Select(m => ui.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = _basePx * m })))).ToArray();
        }

        public static void Dispose()
        {
            foreach (var h in _text.Concat(_icon)) h.Dispose();
            _text = _icon = Array.Empty<IFontHandle>();
        }

        /// <summary>The smallest baked handle at least <paramref name="px"/> tall, or null when the current font is enough.</summary>
        private static IFontHandle? Pick(IFontHandle[] set, float px)
        {
            if (px <= ImGui.GetFont().FontSize * 1.08f) return null;
            IFontHandle? best = null;
            for (int i = 0; i < set.Length; i++)
            {
                if (!set[i].Available) continue;
                best = set[i];
                if (_basePx * Mults[i] >= px * 0.98f) break;
            }
            return best;
        }

        /// <summary>Text at <paramref name="scale"/> × the current size, from a sharp font.</summary>
        public static void Text(ImDrawListPtr dl, Vector2 pos, string text, uint color, float scale, bool icon = false)
        {
            float px = ImGui.GetFontSize() * scale;
            var h = Pick(icon ? _icon : _text, px);
            if (h != null) h.Push(); else if (icon) ImGui.PushFont(UiBuilder.IconFont);
            dl.AddText(ImGui.GetFont(), px, pos, color, text);
            if (h != null) h.Pop(); else if (icon) ImGui.PopFont();
        }

        /// <summary>Size of <paramref name="text"/> at <paramref name="scale"/> × the current size (same font as <see cref="Text"/>).</summary>
        public static Vector2 Measure(string text, float scale, bool icon = false)
        {
            float px = ImGui.GetFontSize() * scale;
            var h = Pick(icon ? _icon : _text, px);
            if (h != null) h.Push(); else if (icon) ImGui.PushFont(UiBuilder.IconFont);
            // CalcTextSize measures at the pushed font's size × the window scale; rescale to the size we draw at
            var size = ImGui.CalcTextSize(text) * (px / ImGui.GetFontSize());
            if (h != null) h.Pop(); else if (icon) ImGui.PopFont();
            return size;
        }

        /// <summary>
        /// Scales ImGui widgets' text in an app screen to <paramref name="scale"/> × the tablet size; dispose to go back.
        /// It does not swap in a baked font: widgets drawn while one of our handles stayed pushed came out blank
        /// (2026-11-13), so sharp large text goes through <see cref="Text"/> (push, draw, pop at once).
        /// </summary>
        public static Scope Scale(float scale)
        {
            ImGui.SetWindowFontScale(PluginUI.AppScale * scale);
            return new Scope();
        }

        public readonly struct Scope : IDisposable
        {
            public void Dispose() => ImGui.SetWindowFontScale(PluginUI.AppScale);
        }
    }
}
