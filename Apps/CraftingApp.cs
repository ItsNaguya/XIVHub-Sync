using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace XIVHubCompanion.Apps
{
    public partial class CraftingApp : IApp
    {
        public string Name => "Crafting";
        public string Icon => ((char)Dalamud.Interface.FontAwesomeIcon.Hammer).ToString();
        public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }
        
        private readonly DataSender _sender;
        private readonly Configuration _configuration;
        private readonly IDataManager _dataManager;
        private readonly IPluginLog _log;
        private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;

        private int _activeTab = 0; // 0=Add Items, 1=Active Pipeline, 2=Macro Solver

        public CraftingApp(DataSender sender, Configuration configuration, IDataManager dataManager, IPluginLog log, Dalamud.Plugin.Services.ITextureProvider textureProvider)
        {
            _sender = sender;
            _configuration = configuration;
            _dataManager = dataManager;
            _log = log;
            _textureProvider = textureProvider;
        }

        public void Draw()
        {
            DrawTabs();
            
            if (_activeTab == 0) DrawAddItemsTab();
            else if (_activeTab == 1) DrawActivePipelineTab();
            else if (_activeTab == 2) DrawMacroSolverTab();
        }

        private void DrawTabs()
        {
            float w = ImGui.GetContentRegionAvail().X;
            Vector2 btnSize = new Vector2(w / 3f - 10f * PluginUI.AppScale, 35f * PluginUI.AppScale);

            Vector4 bgNormal = new Vector4(0.12f, 0.12f, 0.14f, 1f);
            string[] tabs = new string[] { 
                "Add Items", 
                $"Active Pipeline ({_configuration.CraftingActivePipeline.Count})", 
                "Macro Solver" 
            };
            
            UIHelper.DrawPremiumTabSegment(tabs, ref _activeTab, ImGui.GetContentRegionAvail().X);
            
            ImGui.Spacing();
            ImGui.Spacing();
        }

        public void Dispose()
        {
        }
    }
}
