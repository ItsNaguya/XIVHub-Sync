using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Net.Http;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Textures;

namespace XIVHubCompanion.Apps
{
    public partial class CraftingApp
    {
        private int _solverSelectedTarget = -1;
        private int _solverFoodIndex = 0;
        private int _solverPotionIndex = 0;
        private int _solverInitialQuality = 0;
        
        private bool _solverRequireTargetQuality = false;
        private bool _solverManipulation = true;
        private bool _solverHeartAndSoul = false;
        private bool _solverQuickInnovation = false;

        private bool _solverConfigLoaded = false;

        private bool _isSolving = false;
        private DateTime _lastQueuePollTime = DateTime.MinValue;
        private string _queueStatusText = "Calculating...";
        private string? _solveError = null;
        private MacroSolveResult? _solveResult = null;
        private Recipe? _solverActiveRecipe = null;
        
        private int _solverMaxQuality = 14000;
        private int _solverMaxProgress = 6600;
        private int _solverMaxDurability = 70;
        private int _solverRlvl = 640;
        private int _solverProgDiv = 130;
        private int _solverQualDiv = 115;
        private int _solverProgMod = 100;
        private int _solverQualMod = 100;

        private bool _showCrafterProfilesModal = false;
        private int _selectedProfileJobIndex = 0; // 0=CRP, 1=BSM... 7=CUL

        private class Consumable
        {
            public string Name { get; set; } = string.Empty;
            public float CpPct { get; set; }
            public int CpMax { get; set; }
            public float ControlPct { get; set; }
            public int ControlMax { get; set; }
            public float CraftPct { get; set; }
            public int CraftMax { get; set; }
        }

        private List<Consumable> _foodList = new List<Consumable> {
            new Consumable { Name = "None", CpPct = 0, CpMax = 0, ControlPct = 0, ControlMax = 0, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "All i Pebre (HQ) - CP +26% (100), Control +5% (115)", CpPct = 0.26f, CpMax = 100, ControlPct = 0.05f, ControlMax = 115, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "Rroneek Steak (HQ) - CP +26% (100), Control +5% (115)", CpPct = 0.26f, CpMax = 100, ControlPct = 0.05f, ControlMax = 115, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "Stuffed Peppers (HQ) - CP +26% (100), Craftsmanship +5% (115)", CpPct = 0.26f, CpMax = 100, ControlPct = 0, ControlMax = 0, CraftPct = 0.05f, CraftMax = 115 },
            new Consumable { Name = "Vegetable Soup (HQ) - CP +26% (92), Control +5% (104)", CpPct = 0.26f, CpMax = 92, ControlPct = 0.05f, ControlMax = 104, CraftPct = 0, CraftMax = 0 }
        };

        private List<Consumable> _potionList = new List<Consumable> {
            new Consumable { Name = "None", CpPct = 0, CpMax = 0, ControlPct = 0, ControlMax = 0, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "Cunning Craftsman's Tisane (HQ) - CP +6% (27)", CpPct = 0.06f, CpMax = 27, ControlPct = 0, ControlMax = 0, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "Competent Craftsman's Tisane (HQ) - Craftsmanship +2% (36)", CpPct = 0, CpMax = 0, ControlPct = 0, ControlMax = 0, CraftPct = 0.02f, CraftMax = 36 },
            new Consumable { Name = "Commanding Craftsman's Tisane (HQ) - Control +2% (36)", CpPct = 0, CpMax = 0, ControlPct = 0.02f, ControlMax = 36, CraftPct = 0, CraftMax = 0 },
            new Consumable { Name = "Cunning Craftsman's Draught (HQ) - CP +6% (21)", CpPct = 0.06f, CpMax = 21, ControlPct = 0, ControlMax = 0, CraftPct = 0, CraftMax = 0 }
        };

        private readonly string[] _jobNamesFull = { "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian" };
        private readonly string[] _jobNamesShort = { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };
        private readonly uint[] _macroJobIconIds = { 62008, 62009, 62010, 62011, 62012, 62013, 62014, 62015 }; // Crafting class icons

        private class MacroSolveResult
        {
            public int step { get; set; }
            public int progress { get; set; }
            public int quality { get; set; }
            public int cp { get; set; }
            public int durability { get; set; }
            public List<string> actions { get; set; } = new List<string>();
        }

        private void InitializeCrafterProfiles()
        {
            if (_configuration.CrafterProfiles == null) _configuration.CrafterProfiles = new List<CrafterProfile>();
            while (_configuration.CrafterProfiles.Count < 8)
            {
                _configuration.CrafterProfiles.Add(new CrafterProfile { Name = _jobNamesFull[_configuration.CrafterProfiles.Count], Level = 100, Craftsmanship = 4000, Control = 4000, Cp = 600 });
            }
        }

        private void DrawMacroSolverTab()
        {
            if (!_solverConfigLoaded)
            {
                _solverSelectedTarget = _configuration.SolverSelectedTarget;
                _solverFoodIndex = _configuration.SolverFoodIndex;
                _solverPotionIndex = _configuration.SolverPotionIndex;
                _solverInitialQuality = _configuration.SolverInitialQuality;
                _solverRequireTargetQuality = _configuration.SolverRequireTargetQuality;
                _solverManipulation = _configuration.SolverManipulation;
                _solverHeartAndSoul = _configuration.SolverHeartAndSoul;
                _solverQuickInnovation = _configuration.SolverQuickInnovation;
                _solverConfigLoaded = true;
            }

            InitializeCrafterProfiles();

            Vector2 tabContentPos = ImGui.GetCursorScreenPos();
            Vector2 tabContentSize = ImGui.GetContentRegionAvail();

            float width = ImGui.GetContentRegionAvail().X;
            float padding = 15f * PluginUI.AppScale;
            float innerWidthHeader = width - (20f * PluginUI.AppScale); // 10 padding on each side
            
            // --- TOP HEADER: SETUP ---
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            UIHelper.BeginSmoothChild("solverHeader", new Vector2(width, 70f * PluginUI.AppScale), true);
            DrawSolverHeader(innerWidthHeader);
            ImGui.EndChild();
            ImGui.PopStyleVar();
            
            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
            
            // --- MIDDLE BODY: DETAILS & RESULTS ---
            float footerHeight = 45f * PluginUI.AppScale;
            float bodyHeight = ImGui.GetContentRegionAvail().Y - footerHeight - 20f * PluginUI.AppScale; // Increased margin to prevent scrolling
            
            float leftPanelWidth = width * 0.40f;
            float rightPanelWidth = width - leftPanelWidth - 20f * PluginUI.AppScale;

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            UIHelper.BeginSmoothChild("solverConfig", new Vector2(leftPanelWidth, bodyHeight), true);
            DrawSolverConfig();
            ImGui.EndChild();
            ImGui.PopStyleVar();

            ImGui.SameLine();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            UIHelper.BeginSmoothChild("solverResult", new Vector2(rightPanelWidth, bodyHeight), true);
            DrawSolverResult();
            ImGui.EndChild();
            ImGui.PopStyleVar();

            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
            
            // --- BOTTOM FOOTER: ACTION ---
            DrawSolverFooter(width);

            DrawCrafterProfilesModal(tabContentPos, tabContentSize);

            if (_solverSelectedTarget != _configuration.SolverSelectedTarget ||
                _solverFoodIndex != _configuration.SolverFoodIndex ||
                _solverPotionIndex != _configuration.SolverPotionIndex ||
                _solverInitialQuality != _configuration.SolverInitialQuality ||
                _solverRequireTargetQuality != _configuration.SolverRequireTargetQuality ||
                _solverManipulation != _configuration.SolverManipulation ||
                _solverHeartAndSoul != _configuration.SolverHeartAndSoul ||
                _solverQuickInnovation != _configuration.SolverQuickInnovation)
            {
                _configuration.SolverSelectedTarget = _solverSelectedTarget;
                _configuration.SolverFoodIndex = _solverFoodIndex;
                _configuration.SolverPotionIndex = _solverPotionIndex;
                _configuration.SolverInitialQuality = _solverInitialQuality;
                _configuration.SolverRequireTargetQuality = _solverRequireTargetQuality;
                _configuration.SolverManipulation = _solverManipulation;
                _configuration.SolverHeartAndSoul = _solverHeartAndSoul;
                _configuration.SolverQuickInnovation = _solverQuickInnovation;
                _configuration.Save();
            }
        }

        private void DrawSolverHeader(float width)
        {
            float recipeWidth = width * 0.4f;
            float foodWidth = width * 0.28f;
            float potionWidth = width * 0.28f;
            
            ImGui.BeginGroup();
            
            // Labels
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Recipe Selection");
            ImGui.SameLine(recipeWidth + 10f * PluginUI.AppScale);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Food");
            ImGui.SameLine(recipeWidth + foodWidth + 20f * PluginUI.AppScale);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Potion");
            
            ImGui.Dummy(new Vector2(0, 2f * PluginUI.AppScale));
            
            // Combos
            var pipelineNames = new System.Collections.Generic.List<string>();
            if (_configuration.CraftingActivePipeline.Count == 0)
            {
                pipelineNames.Add("No active targets");
                _solverSelectedTarget = -1;
            }
            else
            {
                var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
                foreach (var t in _configuration.CraftingActivePipeline)
                {
                    var rec = sheet?.GetRow(t.RecipeId);
                    if (rec != null && rec.Value.RowId != 0)
                    {
                        pipelineNames.Add(rec.Value.ItemResult.Value.Name.ToString());
                    }
                    else pipelineNames.Add($"Unknown ({t.RecipeId})");
                }
                if (_solverSelectedTarget < 0 || _solverSelectedTarget >= pipelineNames.Count) _solverSelectedTarget = 0;
            }

            ImGui.PushItemWidth(recipeWidth);
            if (ImGui.BeginCombo("##solverTarget", _solverSelectedTarget >= 0 ? pipelineNames[_solverSelectedTarget] : "None"))
            {
                for (int i = 0; i < pipelineNames.Count; i++)
                {
                    bool isSelected = (_solverSelectedTarget == i);
                    if (ImGui.Selectable(pipelineNames[i], isSelected))
                    {
                        _solverSelectedTarget = i;
                        UpdateSolverRecipeContext();
                    }
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            
            if (_solverActiveRecipe == null && _solverSelectedTarget >= 0 && _solverSelectedTarget < _configuration.CraftingActivePipeline.Count)
            {
                UpdateSolverRecipeContext();
            }
            
            ImGui.SameLine(recipeWidth + 10f * PluginUI.AppScale);
            ImGui.PushItemWidth(foodWidth);
            if (ImGui.BeginCombo("##solverFood", _foodList[_solverFoodIndex].Name))
            {
                for (int i = 0; i < _foodList.Count; i++)
                {
                    bool isSelected = (_solverFoodIndex == i);
                    if (ImGui.Selectable(_foodList[i].Name, isSelected)) _solverFoodIndex = i;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            
            ImGui.SameLine(recipeWidth + foodWidth + 20f * PluginUI.AppScale);
            ImGui.PushItemWidth(potionWidth);
            if (ImGui.BeginCombo("##solverPotion", _potionList[_solverPotionIndex].Name))
            {
                for (int i = 0; i < _potionList.Count; i++)
                {
                    bool isSelected = (_solverPotionIndex == i);
                    if (ImGui.Selectable(_potionList[i].Name, isSelected)) _solverPotionIndex = i;
                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            
            ImGui.EndGroup();
        }

        private void DrawSolverFooter(float width)
        {
            if (_isSolving)
            {
                if ((DateTime.Now - _lastQueuePollTime).TotalSeconds > 2)
                {
                    _lastQueuePollTime = DateTime.Now;
                    Task.Run(async () => {
                        try {
                            using var qclient = new HttpClient();
                            qclient.Timeout = TimeSpan.FromSeconds(5);
                            var resStr = await qclient.GetStringAsync("https://xiv.naguya.tech/api/crafting/solve/queue");
                            using var doc = JsonDocument.Parse(resStr);
                            int running = doc.RootElement.GetProperty("running").GetInt32();
                            int queued = doc.RootElement.GetProperty("queued").GetInt32();
                            _queueStatusText = $"Running: {running} | Queued: {queued}";
                        } catch {
                            _queueStatusText = "Calculating...";
                        }
                    });
                }

                var textSize = ImGui.CalcTextSize(_queueStatusText);
                float cardWidth = Math.Max(250f * PluginUI.AppScale, textSize.X + 40f * PluginUI.AppScale);
                
                Vector2 cPos = ImGui.GetCursorScreenPos() + new Vector2((width - cardWidth) * 0.5f, 0);
                
                UIHelper.DrawCard(cPos, new Vector2(cardWidth, 45f * PluginUI.AppScale), new Vector4(0.8f, 0.6f, 0.1f, 0.1f), 4f * PluginUI.AppScale, new Vector4(0.8f, 0.6f, 0.1f, 0.3f));
                
                Vector2 textPos = cPos + new Vector2((cardWidth - textSize.X) * 0.5f, (45f * PluginUI.AppScale - textSize.Y) * 0.5f);
                ImGui.SetCursorScreenPos(textPos);
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), _queueStatusText);
                
                ImGui.SetCursorScreenPos(cPos + new Vector2(0, 50f * PluginUI.AppScale));
            }
            else
            {
                Vector2 cPos = ImGui.GetCursorScreenPos();
                
                // Pulsing animation
                float t = (float)ImGui.GetTime();
                float pulse = 0.5f + 0.5f * (float)Math.Sin(t * 3.0f);
                
                // Sleek premium blue colors
                Vector4 baseBg = new Vector4(0.12f + 0.05f * pulse, 0.28f + 0.05f * pulse, 0.52f + 0.1f * pulse, 1.0f);
                Vector4 hoverBg = new Vector4(0.20f, 0.40f, 0.70f, 1.0f);
                Vector4 textColor = new Vector4(0.9f, 0.95f, 1.0f, 1.0f);
                Vector4 textHover = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);

                if (UIHelper.DrawGarlondButton("solveBtn", cPos, new Vector2(width, 45f * PluginUI.AppScale), "SOLVE MACRO", baseBg, hoverBg, textColor, textHover))
                {
                    RunSolver();
                }
                ImGui.SetCursorScreenPos(cPos + new Vector2(0, 50f * PluginUI.AppScale));
            }

            if (_solveError != null)
            {
                ImGui.TextColored(new Vector4(0.9f, 0.2f, 0.2f, 1f), _solveError);
            }
        }

        private void DrawSolverConfig()
        {
            // Crafter Stats Display
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Crafter Stats");
            
            float buttonWidth = 120f * PluginUI.AppScale;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - buttonWidth);
            if (UIHelper.DrawGarlondButton("crafterProfilesBtn", ImGui.GetCursorScreenPos() + new Vector2(0, -4f * PluginUI.AppScale), new Vector2(buttonWidth, 24f * PluginUI.AppScale), "\uF013 Edit Profiles", new Vector4(0.15f, 0.15f, 0.15f, 1f), new Vector4(0.2f, 0.2f, 0.2f, 1f), new Vector4(0.8f, 0.8f, 0.8f, 1f), Vector4.One))
            {
                _showCrafterProfilesModal = true;
            }
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            int activeJobIndex = 0;
            if (_solverActiveRecipe != null && _solverActiveRecipe.Value.CraftType.RowId >= 0 && _solverActiveRecipe.Value.CraftType.RowId < 8)
            {
                activeJobIndex = (int)_solverActiveRecipe.Value.CraftType.RowId;
            }
            var profile = _configuration.CrafterProfiles[activeJobIndex];
            
            var food = _foodList[_solverFoodIndex];
            var pot = _potionList[_solverPotionIndex];
            
            int buffedCp = profile.Cp + Math.Min(food.CpMax, (int)(profile.Cp * food.CpPct)) + Math.Min(pot.CpMax, (int)(profile.Cp * pot.CpPct));
            int buffedControl = profile.Control + Math.Min(food.ControlMax, (int)(profile.Control * food.ControlPct)) + Math.Min(pot.ControlMax, (int)(profile.Control * pot.ControlPct));
            int buffedCraft = profile.Craftsmanship + Math.Min(food.CraftMax, (int)(profile.Craftsmanship * food.CraftPct)) + Math.Min(pot.CraftMax, (int)(profile.Craftsmanship * pot.CraftPct));

            float statLabelW = 120f * PluginUI.AppScale;
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "Job Level"); ImGui.SameLine(statLabelW); ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), profile.Level.ToString());
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "Craftsmanship"); ImGui.SameLine(statLabelW); ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), buffedCraft.ToString());
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "Control"); ImGui.SameLine(statLabelW); ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), buffedControl.ToString());
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "CP"); ImGui.SameLine(statLabelW); ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), buffedCp.ToString());

            ImGui.Dummy(new Vector2(0, 20f * PluginUI.AppScale));

            // Starting Quality
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Starting Quality");
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            int maxInitQual = _solverMaxQuality / 2;
            float inputQualWidth = 60f * PluginUI.AppScale;
            
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - inputQualWidth - 65f * PluginUI.AppScale);
            ImGui.SliderInt("##initQSlider", ref _solverInitialQuality, 0, maxInitQual, "");
            ImGui.PopItemWidth();
            ImGui.SameLine();
            
            ImGui.PushItemWidth(inputQualWidth);
            ImGui.InputInt("##initQInput", ref _solverInitialQuality, 0);
            ImGui.PopItemWidth();
            if (_solverInitialQuality > maxInitQual) _solverInitialQuality = maxInitQual;
            if (_solverInitialQuality < 0) _solverInitialQuality = 0;
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"/ {maxInitQual}");

            ImGui.Dummy(new Vector2(0, 20f * PluginUI.AppScale));
            
            // Solver Settings
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Solver Settings");
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            UIHelper.DrawGarlondCheckboxWithText("##reqQ", "Solution must reach target quality", ref _solverRequireTargetQuality);
            
            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
            
            UIHelper.DrawGarlondCheckboxWithText("##manip", "Manipulation", ref _solverManipulation);
            UIHelper.DrawGarlondCheckboxWithText("##hns", "Heart and Soul", ref _solverHeartAndSoul);
            UIHelper.DrawGarlondCheckboxWithText("##qi", "Quick Innovation", ref _solverQuickInnovation);
        }

        private void DrawCrafterProfilesModal(Vector2 contentPos, Vector2 contentSize)
        {
            if (UIHelper.BeginPremiumModal("Crafter Profiles", ref _showCrafterProfilesModal, contentPos, contentSize, new Vector2(400f * PluginUI.AppScale, 350f * PluginUI.AppScale), out float alpha))
            {
                Vector2 p = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();
                
                // Header (Close button)
                ImGui.SetCursorScreenPos(p + new Vector2(ImGui.GetWindowWidth() - 35f * PluginUI.AppScale, 5f * PluginUI.AppScale));
                ImGui.PushFont(UiBuilder.IconFont);
                if (UIHelper.DrawGarlondButton("##closeProfiles", ImGui.GetCursorScreenPos(), new Vector2(25f * PluginUI.AppScale, 25f * PluginUI.AppScale), "\uF00D", new Vector4(0.15f, 0.15f, 0.2f, alpha), new Vector4(0.3f, 0.3f, 0.4f, alpha), new Vector4(1f, 1f, 1f, alpha), Vector4.Zero))
                {
                    _showCrafterProfilesModal = false;
                    _configuration.Save();
                }
                ImGui.PopFont();
                
                // Job Icons Row
                ImGui.SetCursorScreenPos(p + new Vector2(15f * PluginUI.AppScale, 40f * PluginUI.AppScale));
                for (int i = 0; i < 8; i++)
                {
                    if (i > 0) ImGui.SameLine(0, 15f * PluginUI.AppScale);
                    
                    Vector2 iconPos = ImGui.GetCursorScreenPos();
                    bool isSelected = (_selectedProfileJobIndex == i);
                    
                    var tex = _textureProvider.GetFromGameIcon(new GameIconLookup(_macroJobIconIds[i]));
                    if (tex != null && tex.GetWrapOrDefault() != null)
                    {
                        if (!isSelected) ImGui.PushStyleColor(ImGuiCol.Button, 0); // Transparent if not selected
                        else ImGui.PushStyleColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.3f, 0.4f, 0.5f)));
                        
                        ImGui.PushID($"job_{i}");
                        if (ImGui.ImageButton(tex.GetWrapOrDefault()!.Handle, new Vector2(24f * PluginUI.AppScale, 24f * PluginUI.AppScale)))
                        {
                            _selectedProfileJobIndex = i;
                        }
                        ImGui.PopID();
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        if (ImGui.Button(_jobNamesShort[i], new Vector2(30f * PluginUI.AppScale, 30f * PluginUI.AppScale))) _selectedProfileJobIndex = i;
                    }
                    
                    // Small text label below icon
                    var labelSize = ImGui.CalcTextSize(_jobNamesShort[i]);
                    drawList.AddText(iconPos + new Vector2(16f * PluginUI.AppScale - labelSize.X/2, 32f * PluginUI.AppScale), isSelected ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.8f, 0.2f, 1f)) : ImGui.ColorConvertFloat4ToU32(new Vector4(0.5f, 0.5f, 0.5f, 1f)), _jobNamesShort[i]);
                }
                
                ImGui.Dummy(new Vector2(0, 40f * PluginUI.AppScale));
                
                // Selected Stats Area
                Vector2 statsPos = ImGui.GetCursorScreenPos();
                UIHelper.DrawCard(statsPos, new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y), new Vector4(0.08f, 0.09f, 0.11f, 1f), 8f * PluginUI.AppScale, new Vector4(0.2f, 0.2f, 0.2f, 1f));
                
                ImGui.SetCursorScreenPos(statsPos + new Vector2(15f * PluginUI.AppScale, 15f * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), $"{_jobNamesFull[_selectedProfileJobIndex]} Stats");
                
                ImGui.SameLine(ImGui.GetWindowWidth() - 120f * PluginUI.AppScale);
                ImGui.PushFont(UiBuilder.IconFont);
                string copyBtnTxt = "\uF0C5";
                ImGui.PopFont();
                copyBtnTxt += " Copy to all";
                
                if (UIHelper.DrawGarlondButton("##copyToAll", ImGui.GetCursorScreenPos() + new Vector2(0, -4f * PluginUI.AppScale), new Vector2(100f * PluginUI.AppScale, 24f * PluginUI.AppScale), copyBtnTxt, new Vector4(0.15f, 0.15f, 0.2f, 1f), new Vector4(0.3f, 0.3f, 0.4f, 1f), Vector4.One, Vector4.One))
                {
                    var source = _configuration.CrafterProfiles[_selectedProfileJobIndex];
                    for (int i=0; i<8; i++)
                    {
                        if (i == _selectedProfileJobIndex) continue;
                        _configuration.CrafterProfiles[i].Level = source.Level;
                        _configuration.CrafterProfiles[i].Craftsmanship = source.Craftsmanship;
                        _configuration.CrafterProfiles[i].Control = source.Control;
                        _configuration.CrafterProfiles[i].Cp = source.Cp;
                    }
                    _configuration.Save();
                }
                
                ImGui.SetCursorScreenPos(statsPos + new Vector2(15f * PluginUI.AppScale, 50f * PluginUI.AppScale));
                
                var profile = _configuration.CrafterProfiles[_selectedProfileJobIndex];
                
                float inputW = 160f * PluginUI.AppScale;
                
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "JOB LEVEL");
                ImGui.SetNextItemWidth(inputW);
                int lvl = profile.Level;
                if (ImGui.InputInt("##p_lvl", ref lvl, 0)) { profile.Level = lvl; _configuration.Save(); }
                
                ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
                
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "CONTROL");
                ImGui.SetNextItemWidth(inputW);
                int ctrl = profile.Control;
                if (ImGui.InputInt("##p_ctrl", ref ctrl, 0)) { profile.Control = ctrl; _configuration.Save(); }
                ImGui.EndGroup();
                
                ImGui.SameLine(190f * PluginUI.AppScale);
                
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "CRAFTSMANSHIP");
                ImGui.SetNextItemWidth(inputW);
                int crft = profile.Craftsmanship;
                if (ImGui.InputInt("##p_crft", ref crft, 0)) { profile.Craftsmanship = crft; _configuration.Save(); }
                
                ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
                
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "CP");
                ImGui.SetNextItemWidth(inputW);
                int cp = profile.Cp;
                if (ImGui.InputInt("##p_cp", ref cp, 0)) { profile.Cp = cp; _configuration.Save(); }
                ImGui.EndGroup();

                UIHelper.EndPremiumModal();
            }
        }

        private void DrawSolverResult()
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Simulation Result");
            if (_solveResult != null)
            {
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - 100f * PluginUI.AppScale);
                ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.4f, 1f), $"SUCCESS ({_solveResult.step} steps)");
            }
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));

            int simProg = _solveResult != null ? Math.Min(100, (int)(((float)_solveResult.progress / _solverMaxProgress) * 100)) : 0;
            int simQual = _solveResult != null ? Math.Min(100, (int)(((float)(_solveResult.quality + _solverInitialQuality) / _solverMaxQuality) * 100)) : Math.Min(100, (int)(((float)_solverInitialQuality / _solverMaxQuality) * 100));
            int simDur = _solveResult != null ? Math.Max(0, _solveResult.durability) : _solverMaxDurability;
            
            // Re-calc buffed CP for the bar max
            int activeJobIndex = 0;
            if (_solverActiveRecipe != null && _solverActiveRecipe.Value.CraftType.RowId >= 0 && _solverActiveRecipe.Value.CraftType.RowId < 8)
                activeJobIndex = (int)_solverActiveRecipe.Value.CraftType.RowId;
            var profile = _configuration.CrafterProfiles[activeJobIndex];
            var food = _foodList[_solverFoodIndex];
            var pot = _potionList[_solverPotionIndex];
            int buffedCp = profile.Cp + Math.Min(food.CpMax, (int)(profile.Cp * food.CpPct)) + Math.Min(pot.CpMax, (int)(profile.Cp * pot.CpPct));

            int simCp = _solveResult != null ? Math.Max(0, _solveResult.cp) : buffedCp;

            DrawProgressBar("PROGRESS", simProg, _solveResult != null ? _solveResult.progress : 0, _solverMaxProgress, new Vector4(0.1f, 0.7f, 0.8f, 1f));
            DrawProgressBar("QUALITY", simQual, _solveResult != null ? _solveResult.quality + _solverInitialQuality : _solverInitialQuality, _solverMaxQuality, new Vector4(0.2f, 0.4f, 0.9f, 1f));
            DrawProgressBar("DURABILITY", (int)(((float)simDur / _solverMaxDurability) * 100), simDur, _solverMaxDurability, new Vector4(0.6f, 0.3f, 0.1f, 1f));
            DrawProgressBar("CP", buffedCp > 0 ? (int)(((float)simCp / buffedCp) * 100) : 0, simCp, buffedCp, new Vector4(0.6f, 0.1f, 0.6f, 1f));

            ImGui.Dummy(new Vector2(0, 15f * PluginUI.AppScale));

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Generated Macro");
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            Vector2 pMac = ImGui.GetCursorScreenPos();
            Vector2 mSize = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y);
            UIHelper.DrawCard(pMac, mSize, new Vector4(0, 0, 0, 0.4f), 6f * PluginUI.AppScale, new Vector4(0.2f, 0.2f, 0.2f, 1f));
            
            ImGui.SetCursorScreenPos(pMac + new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            ImGui.BeginChild("macroTextScroll", mSize - new Vector2(20f * PluginUI.AppScale, 20f * PluginUI.AppScale), false);
            
            var blocks = GenerateMacroBlocks();
            if (blocks.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), "Run simulation to generate action sequence...");
            }
            else
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    string block = blocks[i];
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), $"Macro #{i + 1}");
                    
                    float buttonWidth = 100f * PluginUI.AppScale;
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - buttonWidth - 15f * PluginUI.AppScale);
                    
                    ImGui.PushFont(UiBuilder.IconFont);
                    string mcopyTxt = "\uF0C5";
                    ImGui.PopFont();
                    mcopyTxt += $" Copy #{i + 1}";
                    
                    if (UIHelper.DrawGarlondButton($"copyMacro{i}", ImGui.GetCursorScreenPos() + new Vector2(0, -4f * PluginUI.AppScale), new Vector2(buttonWidth, 24f * PluginUI.AppScale), mcopyTxt, new Vector4(0.15f, 0.15f, 0.15f, 1f), new Vector4(0.2f, 0.2f, 0.2f, 1f), new Vector4(0.8f, 0.8f, 0.8f, 1f), Vector4.One))
                    {
                        ImGui.SetClipboardText(block);
                    }
                    
                    ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
                    
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, 0);
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(new Vector4(0.3f, 0.7f, 0.5f, 1f)));
                    
                    int lineCount = block.Split('\n').Length;
                    float height = (lineCount * ImGui.GetTextLineHeight()) + ImGui.GetStyle().FramePadding.Y * 2;
                    
                    ImGui.InputTextMultiline($"##macro{i}", ref block, 1024, new Vector2(-1, height), ImGuiInputTextFlags.ReadOnly);
                    
                    ImGui.PopStyleColor(2);
                    
                    if (i < blocks.Count - 1)
                    {
                        ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
                        ImGui.Separator();
                        ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
                    }
                }
            }
            ImGui.EndChild();
        }

        private void DrawProgressBar(string label, int percent, int current, int max, Vector4 color)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.4f, 0.4f, 1f), label);
            ImGui.SameLine(120f * PluginUI.AppScale);
            
            Vector2 p = ImGui.GetCursorScreenPos();
            float w = ImGui.GetContentRegionAvail().X;
            float h = 14f * PluginUI.AppScale; // Sleeker
            
            ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w, h), ImGui.ColorConvertFloat4ToU32(new Vector4(0.1f, 0.1f, 0.1f, 0.8f)), 4f);
            if (percent > 0)
            {
                if (percent > 100) percent = 100;
                ImGui.GetWindowDrawList().AddRectFilled(p, p + new Vector2(w * (percent / 100f), h), ImGui.ColorConvertFloat4ToU32(color), 4f);
            }
            
            string txt = $"{current} / {max}";
            var tSize = ImGui.CalcTextSize(txt);
            ImGui.GetWindowDrawList().AddText(p + new Vector2(10f * PluginUI.AppScale, h / 2 - tSize.Y / 2), ImGui.ColorConvertFloat4ToU32(new Vector4(1,1,1,1)), txt);
            
            ImGui.Dummy(new Vector2(0, h + 8f * PluginUI.AppScale));
        }

        private void UpdateSolverRecipeContext()
        {
            if (_solverSelectedTarget < 0 || _solverSelectedTarget >= _configuration.CraftingActivePipeline.Count) return;
            var t = _configuration.CraftingActivePipeline[_solverSelectedTarget];
            var sheet = _dataManager.GetExcelSheet<Recipe>();
            var rec = sheet?.GetRow(t.RecipeId);
            if (rec != null && rec.Value.RowId != 0)
            {
                _solverActiveRecipe = rec.Value;
                
                var rlvl = rec.Value.RecipeLevelTable.Value;
                if (rlvl.RowId != 0)
                {
                    _solverRlvl = (int)rlvl.RowId;
                    
                    _solverProgDiv = rlvl.ProgressDivider;
                    _solverQualDiv = rlvl.QualityDivider;
                    _solverProgMod = rlvl.ProgressModifier;
                    _solverQualMod = rlvl.QualityModifier;

                    _solverMaxProgress = (int)Math.Floor(rlvl.Difficulty * rec.Value.DifficultyFactor / 100f);
                    _solverMaxQuality = (int)Math.Floor(rlvl.Quality * rec.Value.QualityFactor / 100f);
                    _solverMaxDurability = (int)Math.Floor(rlvl.Durability * rec.Value.DurabilityFactor / 100f);
                }
            }
        }

        private List<string> GenerateMacroBlocks()
        {
            if (_solveResult == null || _solveResult.actions == null || _solveResult.actions.Count == 0) return new List<string>();
            var lines = new List<string>();
            foreach (var act in _solveResult.actions)
            {
                lines.Add($"/ac \"{act}\" <wait.3>");
            }
            
            int maxLines = 14;
            var blocks = new List<string>();
            
            if (lines.Count <= 15)
            {
                // Can fit all in one macro without needing an echo at the end (or with echo)
                string block = string.Join("\n", lines);
                if (lines.Count < 15) block += "\n/echo Craft finished! <se.8>";
                blocks.Add(block);
            }
            else
            {
                for (int i = 0; i < lines.Count; i += maxLines)
                {
                    var blockLines = lines.Skip(i).Take(maxLines).ToList();
                    if (i + maxLines >= lines.Count)
                    {
                        if (blockLines.Count < 15) blockLines.Add("/echo Craft finished! <se.8>");
                    }
                    else
                    {
                        blockLines.Add($"/echo Macro #{(i / maxLines) + 1} finished! <se.8>");
                    }
                    blocks.Add(string.Join("\n", blockLines));
                }
            }
            
            return blocks;
        }

        private async void RunSolver()
        {
            if (_solverSelectedTarget < 0) return;
            
            _isSolving = true;
            _queueStatusText = "Calculating...";
            _lastQueuePollTime = DateTime.MinValue;
            _solveError = null;
            _solveResult = null;

            int activeJobIndex = 0;
            if (_solverActiveRecipe != null && _solverActiveRecipe.Value.CraftType.RowId >= 0 && _solverActiveRecipe.Value.CraftType.RowId < 8)
                activeJobIndex = (int)_solverActiveRecipe.Value.CraftType.RowId;
            
            var profile = _configuration.CrafterProfiles[activeJobIndex];
            var food = _foodList[_solverFoodIndex];
            var pot = _potionList[_solverPotionIndex];
            
            int buffedCp = profile.Cp + Math.Min(food.CpMax, (int)(profile.Cp * food.CpPct)) + Math.Min(pot.CpMax, (int)(profile.Cp * pot.CpPct));
            int buffedControl = profile.Control + Math.Min(food.ControlMax, (int)(profile.Control * food.ControlPct)) + Math.Min(pot.ControlMax, (int)(profile.Control * pot.ControlPct));
            int buffedCraft = profile.Craftsmanship + Math.Min(food.CraftMax, (int)(profile.Craftsmanship * food.CraftPct)) + Math.Min(pot.CraftMax, (int)(profile.Craftsmanship * pot.CraftPct));

            try
            {
                var reqBody = new
                {
                    stats = new
                    {
                        level = profile.Level,
                        craftsmanship = buffedCraft,
                        control = buffedControl,
                        cp = buffedCp,
                        manipulation = _solverManipulation,
                        heartAndSoul = _solverHeartAndSoul,
                        quickInnovation = _solverQuickInnovation
                    },
                    recipe = new
                    {
                        progress = _solverMaxProgress,
                        quality = _solverMaxQuality,
                        durability = _solverMaxDurability,
                        rlvl = _solverRlvl,
                        progressDivider = _solverProgDiv,
                        qualityDivider = _solverQualDiv,
                        progressModifier = _solverProgMod,
                        qualityModifier = _solverQualMod
                    },
                    hqInitialQuality = _solverInitialQuality,
                    settings = new
                    {
                        requireTargetQuality = _solverRequireTargetQuality
                    }
                };

                string json = JsonSerializer.Serialize(reqBody);
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                var response = await client.PostAsync("https://xiv.naguya.tech/api/crafting/solve", content);
                string resJson = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    _solveResult = JsonSerializer.Deserialize<MacroSolveResult>(resJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    _solveError = $"API Error: {response.StatusCode} - {resJson}";
                }
            }
            catch (Exception ex)
            {
                _solveError = "Failed to run solver: " + ex.Message;
            }
            finally
            {
                _isSolving = false;
            }
        }
    }
}
