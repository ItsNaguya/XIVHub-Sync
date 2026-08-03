using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace XIVHubCompanion.Apps
{
    public class RaidPlannerApp : IApp
    {
        public string Name => "Raid Planner";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.Khanda).ToString(); public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }

        public void Draw()
        {
            ImGui.TextWrapped("Static Raid Planner");
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "Loot distribution tracking, BIS list verification, and roster management.");
            
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();

            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1.0f), "Status: Work in Progress");
            ImGui.TextWrapped("This feature is currently under active development. Upcoming updates will allow direct synchronization of party members' BIS status.");
        }

        public void Dispose()
        {
        }
    }
}
