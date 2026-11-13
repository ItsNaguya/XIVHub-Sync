using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace XIVHubCompanion
{
    /// <summary>The Aetheryte look of aetherhold.naguya.tech (styles/aetheryte.css): navy panels, crystal hairlines, gold accents.</summary>
    public static class Theme
    {
        public static readonly Vector4 Night0 = Hex(0x04060f);
        public static readonly Vector4 Night1 = Hex(0x080d1f);
        public static readonly Vector4 Night2 = Hex(0x0e1631);
        public static readonly Vector4 Night3 = Hex(0x16214a);
        public static readonly Vector4 Crystal = Hex(0x6fd3ff);
        public static readonly Vector4 CrystalDeep = Hex(0x2d8fd8);
        public static readonly Vector4 CrystalLine = Crystal.With(0.22f);
        public static readonly Vector4 Gold = Hex(0xdcb978);
        public static readonly Vector4 GoldDeep = Hex(0xa9813f);
        public static readonly Vector4 GoldLine = Gold.With(0.28f);
        public static readonly Vector4 Violet = Hex(0xa99bff);
        public static readonly Vector4 Text = Hex(0xe8eefc);
        public static readonly Vector4 Text2 = Hex(0xa8b6d6);
        public static readonly Vector4 Text3 = Hex(0x6c7aa0);
        public static readonly Vector4 Live = Hex(0x4ee59a);
        public static readonly Vector4 Warn = Hex(0xffc56b);
        public static readonly Vector4 Bad = Hex(0xff7a8a);

        public static Vector4 With(this Vector4 c, float alpha) => new(c.X, c.Y, c.Z, alpha);
        public static uint U32(this Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

        private static Vector4 Hex(uint rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);

        private static readonly (ImGuiCol, Vector4)[] Colors =
        {
            (ImGuiCol.Text, Text), (ImGuiCol.TextDisabled, Text3),
            (ImGuiCol.PopupBg, Night1.With(0.97f)), (ImGuiCol.Border, CrystalLine),
            (ImGuiCol.FrameBg, Night2), (ImGuiCol.FrameBgHovered, Night3), (ImGuiCol.FrameBgActive, Night3),
            (ImGuiCol.Button, Night2), (ImGuiCol.ButtonHovered, Night3), (ImGuiCol.ButtonActive, CrystalDeep.With(0.6f)),
            (ImGuiCol.Header, Night3), (ImGuiCol.HeaderHovered, CrystalDeep.With(0.35f)), (ImGuiCol.HeaderActive, CrystalDeep.With(0.55f)),
            (ImGuiCol.Tab, Night2), (ImGuiCol.TabHovered, Night3), (ImGuiCol.TabActive, Night3),
            (ImGuiCol.CheckMark, Crystal), (ImGuiCol.SliderGrab, Crystal), (ImGuiCol.SliderGrabActive, Gold),
            (ImGuiCol.Separator, CrystalLine), (ImGuiCol.SeparatorHovered, Crystal.With(0.5f)), (ImGuiCol.SeparatorActive, Crystal),
            (ImGuiCol.ScrollbarBg, Night0.With(0.4f)), (ImGuiCol.ScrollbarGrab, Night3), (ImGuiCol.ScrollbarGrabHovered, CrystalDeep.With(0.6f)),
            (ImGuiCol.ScrollbarGrabActive, Crystal.With(0.7f)), (ImGuiCol.TextSelectedBg, CrystalDeep.With(0.45f)),
            (ImGuiCol.TableHeaderBg, Night2), (ImGuiCol.TableBorderStrong, CrystalLine), (ImGuiCol.TableBorderLight, Crystal.With(0.1f)),
            (ImGuiCol.TableRowBgAlt, Crystal.With(0.03f)), (ImGuiCol.ResizeGrip, CrystalLine), (ImGuiCol.NavHighlight, Gold),
        };

        /// <summary>Pushes the Aetheryte colours for ImGui's own widgets (inputs, combos, tabs, tables). Pair with <see cref="Pop"/>.</summary>
        public static void Push()
        {
            foreach (var (col, value) in Colors) ImGui.PushStyleColor(col, value);
        }

        public static void Pop() => ImGui.PopStyleColor(Colors.Length);
    }
}
