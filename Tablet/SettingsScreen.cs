using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using XIVHubCompanion.Tablet;

namespace XIVHubCompanion
{
    public partial class PluginUI
    {
        private static readonly string[] SizeNames = { "Extra small", "Small", "Standard", "Large", "Extra large" };
        private static readonly (string Id, string Label, Vector4 Color)[] Accents = { ("crystal", "Crystal", Theme.Crystal), ("gold", "Gold", Theme.Gold), ("violet", "Violet", Theme.Violet) };
        private bool _wipeOpen;

        private static void Section(string title)
        {
            ImGui.Dummy(Ui.S(0, 10));
            Ui.Caps(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(), title);
            ImGui.Dummy(Ui.S(0, 20));
        }

        private bool Switch(string id, string label, bool value, Action<bool> set)
        {
            bool v = value;
            if (!UIHelper.DrawPremiumSwitchWithText(id, label, ref v)) return false;
            set(v);
            configuration.Save();
            return true;
        }

        /// <summary>The Settings app: tablet, account and sync, then each app's own settings.</summary>
        private void DrawSettings()
        {
            if (!ImGui.BeginTabBar("SettingsTabs")) return;

            if (ImGui.BeginTabItem("Tablet"))
            {
                Section("Size");
                int size = configuration.TabletSize;
                ImGui.SetNextItemWidth(Ui.S(260));
                if (ImGui.Combo("##size", ref size, SizeNames, SizeNames.Length)) { configuration.TabletSize = size; configuration.Save(); }

                Section("Wallpaper");
                float tw = Ui.S(150), th = Ui.S(90), gap = Ui.S(12);
                var start = ImGui.GetCursorScreenPos();
                var dl = ImGui.GetWindowDrawList();
                for (int i = 0; i < Wallpapers.Length; i++)
                {
                    var (id, label) = Wallpapers[i];
                    var p = start + new Vector2(i * (tw + gap), 0);
                    bool sel = configuration.TabletWallpaper == id;
                    bool click = Ui.Hit("wp_" + id, p, new Vector2(tw, th + Ui.S(22)), out float hv);
                    var saved = configuration.TabletWallpaper;
                    configuration.TabletWallpaper = id;
                    ImGui.PushClipRect(p, p + new Vector2(tw, th), true);
                    DrawWallpaper(dl, p, p + new Vector2(tw, th), preview: true);
                    ImGui.PopClipRect();
                    configuration.TabletWallpaper = saved;
                    dl.AddRect(p, p + new Vector2(tw, th), (sel ? Ui.Accent : Theme.CrystalLine.With(0.22f + 0.4f * hv)).U32(), Ui.S(6), 0, Ui.S(sel ? 2 : 1));
                    Ui.Text(dl, p + new Vector2(0, th + Ui.S(4)), label, sel ? Ui.Accent : Theme.Text2, 0.85f);
                    if (click) { configuration.TabletWallpaper = id; SaveTablet(); }
                }
                ImGui.SetCursorScreenPos(start + new Vector2(0, th + Ui.S(30)));
                if (configuration.TabletWallpaper == "constellation")
                {
                    Switch("chk_bg", "Still constellation (no animation)", configuration.HideBackgroundAnimation, v => configuration.HideBackgroundAnimation = v);
                    Switch("chk_scan", "Hide the scanline", configuration.HideScanline, v => configuration.HideScanline = v);
                }

                Section("Accent");
                start = ImGui.GetCursorScreenPos();
                for (int i = 0; i < Accents.Length; i++)
                {
                    var (id, label, color) = Accents[i];
                    var p = start + new Vector2(i * Ui.S(120), 0);
                    bool sel = configuration.TabletAccent == id;
                    if (Ui.Hit("acc_" + id, p, Ui.S(110, 30), out float hv)) { configuration.TabletAccent = id; SaveTablet(); }
                    Ui.Panel(dl, p, p + Ui.S(110, 30), color.With(sel ? 0.22f : 0.08f + 0.1f * hv), color.With(sel ? 0.9f : 0.35f), 6);
                    Ui.Dot(dl, p + Ui.S(16, 15), 5, color);
                    Ui.Text(dl, p + new Vector2(Ui.S(30), Ui.S(15) - ImGui.GetTextLineHeight() / 2), label, sel ? Theme.Text : Theme.Text2);
                }
                ImGui.SetCursorScreenPos(start + new Vector2(0, Ui.S(40)));

                Section("Behaviour");
                Switch("chk_scroll", "Hide scrollbars", configuration.HideScrollbars, v => configuration.HideScrollbars = v);
                Switch("chk_hover", "Open Market when you hover an item for a second", configuration.EnableHoverItemFetching, v => configuration.EnableHoverItemFetching = v);
                Switch("chk_startup", "Open the tablet when you log in", configuration.OpenOnStartup, v => configuration.OpenOnStartup = v);
                if (configuration.OpenOnStartup)
                {
                    ImGui.Indent(Ui.S(20));
                    Switch("chk_minimized", "Start asleep (small view)", configuration.StartMinimized, v => configuration.StartMinimized = v);
                    ImGui.Unindent(Ui.S(20));
                }

                Section("Sleep view");
                Switch("chk_overlay", "Show alerts in the small view", configuration.ShowMinimizedOverlay, v => configuration.ShowMinimizedOverlay = v);
                if (configuration.ShowMinimizedOverlay)
                {
                    ImGui.Indent(Ui.S(20));
                    Switch("chk_gathering", "Favourite nodes that are up", configuration.ShowMinimizedGatheringOverlay, v => configuration.ShowMinimizedGatheringOverlay = v);
                    Switch("chk_retainer", "Retainer ventures", configuration.ShowMinimizedRetainerOverlay, v => configuration.ShowMinimizedRetainerOverlay = v);
                    ImGui.Unindent(Ui.S(20));
                }
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Account & sync"))
            {
                Section("Aetherhold account");
                if (configuration.IsVerified)
                {
                    string role = sender.CurrentUserRole switch { "admin" => " · admin", "friend" => " · friend", _ => "" };
                    ImGui.TextColored(Theme.Live, $"Connected{role}");
                    ImGui.TextColored(Theme.Text3, $"Token {TokenHint(configuration.XivHubId)}  ·  {HubClient.BaseUrl.Replace("https://", "")}");
                }
                else ImGui.TextColored(Theme.Warn, "Not connected");
                ImGui.Dummy(Ui.S(0, 6));
                if (Ui.Button("btn_unlink", "Enter a new token / disconnect", ImGui.GetCursorScreenPos(), Ui.S(280, 32), Kind.Ghost, FontAwesomeIcon.Unlink)) SignOut();
                ImGui.Dummy(Ui.S(0, 36));

                Section("LiveSync");
                Switch("chk_sync", "Send my character to Aetherhold live", configuration.IsSyncEnabled, v => configuration.IsSyncEnabled = v);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text2);
                ImGui.TextWrapped("Gear, stats, bags, retainers and collections go to your Aetherhold page while you play. Cart, routines, venture timers and crafter stats are shared with the website both ways.");
                ImGui.PopStyleColor();
                ImGui.Dummy(Ui.S(0, 6));
                ImGui.TextColored(Theme.Text2, $"Syncs {sender.TotalSyncs}  ·  failed {sender.FailedSyncs}  ·  last {(sender.LastSyncTime == DateTime.MinValue ? "never" : sender.LastSyncTime.ToString("HH:mm:ss"))}");
                ImGui.TextColored(sender.FailedSyncs > 0 && sender.LastSyncStatus != "Success" ? Theme.Warn : Theme.Text3, $"Status: {sender.LastSyncStatus}");

                Section("Data reset");
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text2);
                ImGui.TextWrapped("Wiping the calendar deletes all your custom events on every device.");
                ImGui.PopStyleColor();
                ImGui.Dummy(Ui.S(0, 4));
                if (!_wipeOpen)
                {
                    if (Ui.Button("btn_wipe", "Wipe calendar…", ImGui.GetCursorScreenPos(), Ui.S(170, 30), Kind.Danger, FontAwesomeIcon.Trash)) _wipeOpen = true;
                }
                else
                {
                    var p = ImGui.GetCursorScreenPos();
                    if (Ui.Button("btn_wipe_yes", "Yes, wipe it", p, Ui.S(140, 30), Kind.Danger)
                        && _objectTable[0] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
                    {
                        var (n, w) = (player.Name.ToString(), player.HomeWorld.Value.Name.ToString());
                        Task.Run(() => sender.WipeCalendarAsync(n, w));
                        _wipeOpen = false;
                    }
                    if (Ui.Button("btn_wipe_no", "Cancel", p + Ui.S(150, 0), Ui.S(110, 30))) _wipeOpen = false;
                }
                ImGui.Dummy(Ui.S(0, 40));
                ImGui.EndTabItem();
            }

            foreach (var app in _apps)
            {
                if (!app.HasSettings || !ImGui.BeginTabItem(app.Name)) continue;
                app.DrawSettings();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        /// <summary>Never show the whole token on screen (streams, screenshots).</summary>
        private static string TokenHint(string token) => string.IsNullOrEmpty(token) || token.Length < 8 ? "set" : $"{token[..3]}…{token[^4..]}";
    }
}
