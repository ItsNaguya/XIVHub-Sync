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
            Vector4 bgActive = new Vector4(0.2f, 0.4f, 0.8f, 1f);
            Vector4 textNormal = new Vector4(0.8f, 0.8f, 0.8f, 1f);
            Vector4 textActive = new Vector4(1f, 1f, 1f, 1f);

            if (UIHelper.DrawGarlondButton("btn_craft_tab_add", ImGui.GetCursorScreenPos(), btnSize, "Add Items", _activeTab == 0 ? bgActive : bgNormal, bgActive, _activeTab == 0 ? textActive : textNormal, textActive))
                _activeTab = 0;
            
            ImGui.SameLine();
            if (UIHelper.DrawGarlondButton("btn_craft_tab_active", ImGui.GetCursorScreenPos(), btnSize, $"Active Pipeline ({_configuration.CraftingActivePipeline.Count})", _activeTab == 1 ? bgActive : bgNormal, bgActive, _activeTab == 1 ? textActive : textNormal, textActive))
                _activeTab = 1;
            
            ImGui.SameLine();
            if (UIHelper.DrawGarlondButton("btn_craft_tab_solver", ImGui.GetCursorScreenPos(), btnSize, "Macro Solver", _activeTab == 2 ? bgActive : bgNormal, bgActive, _activeTab == 2 ? textActive : textNormal, textActive))
                _activeTab = 2;

            ImGui.Dummy(new Vector2(0, 15f * PluginUI.AppScale));
        }

        public void Dispose()
        {
        }
    }
}
