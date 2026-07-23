using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace XIVHubCompanion
{
    public class PluginUI : IDisposable
    {
        private Configuration configuration;
        private DataSender sender;
        private bool settingsVisible = false;

        public bool SettingsVisible
        {
            get { return settingsVisible; }
            set { settingsVisible = value; }
        }

        public PluginUI(Configuration configuration, DataSender sender)
        {
            this.configuration = configuration;
            this.sender = sender;
        }

        public void Dispose()
        {
        }

        public void Draw()
        {
            DrawSettingsWindow();
        }

        public void DrawSettingsWindow()
        {
            if (!SettingsVisible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(400, 250), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("XIV Hub Companion Settings", ref settingsVisible, ImGuiWindowFlags.NoCollapse))
            {
                if (ImGui.BeginTabBar("##Tabs"))
                {
                    if (ImGui.BeginTabItem("Settings"))
                    {
                        var enabled = this.configuration.IsSyncEnabled;
                        if (ImGui.Checkbox("Enable Live Sync", ref enabled))
                        {
                            this.configuration.IsSyncEnabled = enabled;
                            this.configuration.Save();
                        }
                        
                        ImGui.Spacing();
                        ImGui.TextWrapped("When enabled, this plugin will automatically sync your character data, inventory, and gear to your XIV Hub web dashboard.");
                        ImGui.EndTabItem();
                    }
                    
                    if (ImGui.BeginTabItem("Stats"))
                    {
                        ImGui.Text($"Total Sync Attempts: {sender.TotalSyncs}");
                        ImGui.Text($"Failed Syncs: {sender.FailedSyncs}");
                        ImGui.Text($"Last Sync Time: {(sender.LastSyncTime == DateTime.MinValue ? "Never" : sender.LastSyncTime.ToString("HH:mm:ss"))}");
                        ImGui.Spacing();
                        ImGui.TextWrapped($"Last Sync Status: {sender.LastSyncStatus}");
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();
        }
    }
}
