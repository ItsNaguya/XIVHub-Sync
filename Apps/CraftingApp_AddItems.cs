using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;


namespace XIVHubCompanion.Apps
{
    public partial class CraftingApp
    {
        private string _reqLevelMin = "";
        private string _reqLevelMax = "";
        private string _iLvlMin = "";
        private string _iLvlMax = "";
        private string _rLvlMin = "";
        private string _rLvlMax = "";
        private int _craftedByJob = -1;
        private bool _craftableOnly = false;
        private bool _collectableOnly = false;

        private class SearchResultWrapper
        {
            public Recipe Recipe { get; set; }
            public ISharedImmediateTexture IconTexture { get; set; }
            public ISharedImmediateTexture JobIconTexture { get; set; }
        }

        private List<SearchResultWrapper> _searchResults = new List<SearchResultWrapper>();
        private bool _hasSearched = false;

        private string[] _jobAbbreviations = { "Any", "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };
        private uint[] _jobIconIds = { 0, 62108, 62109, 62110, 62111, 62112, 62113, 62114, 62115 }; 

        private bool _showFilters = false;
        
        private string _searchQuery = "";

        private int _sortMode = 0; // 0 = Relevance, 1 = Name, 2 = Level, 3 = iLvl
        private bool _sortDesc = true;
        
        private HashSet<string> _selectedJobs = new HashSet<string>();

        private Dictionary<string, uint> _jobIcons = new Dictionary<string, uint>
        {
            {"PLD", 62119}, {"WAR", 62121}, {"DRK", 62132}, {"GNB", 62137},
            {"WHM", 62124}, {"SCH", 62128}, {"AST", 62133}, {"SGE", 62140},
            {"MNK", 62120}, {"DRG", 62122}, {"NIN", 62130}, {"SAM", 62134}, {"RPR", 62139}, {"VPR", 62141},
            {"BRD", 62123}, {"MCH", 62131}, {"DNC", 62138},
            {"BLM", 62125}, {"SMN", 62127}, {"RDM", 62135}, {"PCT", 62142}, {"BLU", 62136}
        };
        private string[] _combatJobs = { "PLD","WAR","DRK","GNB","WHM","SCH","AST","SGE","MNK","DRG","NIN","SAM","RPR","VPR","BRD","MCH","DNC","BLM","SMN","RDM","PCT","BLU" };

        private Dictionary<string, uint> _classIcons = new Dictionary<string, uint>
        {
            {"GLA", 62101}, {"MRD", 62103}, {"CNJ", 62106},
            {"PGL", 62102}, {"LNC", 62104}, {"ROG", 62129},
            {"ARC", 62105}, {"THM", 62107}, {"ACN", 62126}
        };
        private string[] _classes = { "GLA","MRD","CNJ","PGL","LNC","ROG","ARC","THM","ACN" };

        private Dictionary<string, uint> _dolIcons = new Dictionary<string, uint>
        {
            {"MIN", 62116}, {"BTN", 62117}, {"FSH", 62118}
        };
        private string[] _dol = { "MIN", "BTN", "FSH" };

        private Dictionary<string, uint> _dohIcons = new Dictionary<string, uint>
        {
            {"CRP", 62108}, {"BSM", 62109}, {"ARM", 62110}, {"GSM", 62111},
            {"LTW", 62112}, {"WVR", 62113}, {"ALC", 62114}, {"CUL", 62115}
        };
        private string[] _doh = { "CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL" };

        private Dictionary<int, int> _addQuantities = new Dictionary<int, int>();

        private void DrawAddItemsTab()
        {
            float width = ImGui.GetContentRegionAvail().X;
            float draftPanelWidth = 320f * PluginUI.AppScale;
            float centerWidth = width - draftPanelWidth - 20f * PluginUI.AppScale;

            UIHelper.BeginSmoothChild("mainSearchPanel", new Vector2(centerWidth, 0), false);
            
            if (_showFilters)
            {
                float filterLeftWidth = 220f * PluginUI.AppScale;
                float wornByWidth = ImGui.GetContentRegionAvail().X - filterLeftWidth - 15f * PluginUI.AppScale;
                
                ImGui.BeginGroup();
                DrawFiltersLeft(filterLeftWidth);
                ImGui.EndGroup();
                
                ImGui.SameLine(0, 15f * PluginUI.AppScale);
                
                ImGui.BeginGroup();
                DrawWornBy(wornByWidth);
                ImGui.EndGroup();
            }
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            DrawSearchAndSortBar();
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            DrawSearchResults();
            
            ImGui.EndChild();

            ImGui.SameLine();
            UIHelper.BeginSmoothChild("draftPanel", new Vector2(draftPanelWidth, 0), true);
            DrawDraftPipeline();
            ImGui.EndChild();
        }

        private void DrawFiltersLeft(float width)
        {
            Vector2 p = ImGui.GetCursorScreenPos();
            UIHelper.DrawCard(p, new Vector2(width, 285f * PluginUI.AppScale), new Vector4(0, 0, 0, 0.2f), 8f * PluginUI.AppScale, new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            
            ImGui.BeginChild("filtersLeftInner", new Vector2(width, 285f * PluginUI.AppScale), false);
            ImGui.SetCursorScreenPos(p + new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            
            float inputWidth = (width - 40f * PluginUI.AppScale) / 2f;

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "REQUIRED LEVEL");
            ImGui.PushItemWidth(inputWidth);
            ImGui.InputText("##rmin", ref _reqLevelMin, 10); ImGui.SameLine(); ImGui.Text("-"); ImGui.SameLine(); ImGui.InputText("##rmax", ref _reqLevelMax, 10);
            ImGui.PopItemWidth();
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "ITEM LEVEL");
            ImGui.PushItemWidth(inputWidth);
            ImGui.InputText("##imin", ref _iLvlMin, 10); ImGui.SameLine(); ImGui.Text("-"); ImGui.SameLine(); ImGui.InputText("##imax", ref _iLvlMax, 10);
            ImGui.PopItemWidth();
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "RECIPE LEVEL");
            ImGui.PushItemWidth(inputWidth);
            ImGui.InputText("##rlmin", ref _rLvlMin, 10); ImGui.SameLine(); ImGui.Text("-"); ImGui.SameLine(); ImGui.InputText("##rlmax", ref _rLvlMax, 10);
            ImGui.PopItemWidth();

            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "CRAFTED BY");
            string[] jobs = { "Any Crafter", "Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian" };
            int currentComboIndex = _craftedByJob + 1;
            ImGui.PushItemWidth(width - 20f * PluginUI.AppScale);
            if (ImGui.BeginCombo("##craftedby", jobs[currentComboIndex]))
            {
                for (int i = 0; i < jobs.Length; i++)
                {
                    if (ImGui.Selectable(jobs[i], currentComboIndex == i))
                    {
                        _craftedByJob = i - 1;
                        PerformSearch();
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();

            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
            
            Vector2 cbP = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(cbP + new Vector2(5f * PluginUI.AppScale, 0));
            if (UIHelper.DrawPremiumCheckbox("##craftable", ImGui.GetCursorScreenPos(), ref _craftableOnly)) PerformSearch();
            ImGui.SameLine(); ImGui.Text("Craftable");
            ImGui.SameLine(width / 2f + 5f * PluginUI.AppScale);
            if (UIHelper.DrawPremiumCheckbox("##collectable", ImGui.GetCursorScreenPos(), ref _collectableOnly)) PerformSearch();
            ImGui.SameLine(); ImGui.Text("Collectable");
            
            ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));

            Vector2 btnPos = ImGui.GetCursorScreenPos();
            if (UIHelper.DrawPremiumButton("resetFilters", btnPos, new Vector2(width - 20f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Reset All Filters", new Vector4(0.15f, 0.15f, 0.15f, 1f), new Vector4(0.2f, 0.2f, 0.2f, 1f), new Vector4(0.8f, 0.8f, 0.8f, 1f), Vector4.One))
            {
                _reqLevelMin = ""; _reqLevelMax = "";
                _iLvlMin = ""; _iLvlMax = "";
                _rLvlMin = ""; _rLvlMax = "";
                _craftedByJob = -1;
                _craftableOnly = false;
                _collectableOnly = false;
                _selectedJobs.Clear();
                _searchQuery = "";
                PerformSearch();
            }
            
            ImGui.EndChild();
        }

        private void DrawWornBy(float width)
        {
            Vector2 p = ImGui.GetCursorScreenPos();
            UIHelper.DrawCard(p, new Vector2(width, 285f * PluginUI.AppScale), new Vector4(0, 0, 0, 0.2f), 8f * PluginUI.AppScale, new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
            
            ImGui.BeginChild("wornByInner", new Vector2(width, 285f * PluginUI.AppScale), false);
            ImGui.SetCursorPosY(10f * PluginUI.AppScale);
            float centerX = ImGui.GetCursorPosX() + width / 2f - 30f * PluginUI.AppScale;
            ImGui.SetCursorPosX(centerX);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "WORN BY");
            
            ImGui.SetCursorPos(new Vector2(15f * PluginUI.AppScale, 40f * PluginUI.AppScale));
            DrawIconGridRow("Job", _combatJobs, _jobIcons);
            
            ImGui.SetCursorPosX(15f * PluginUI.AppScale);
            DrawIconGridRow("Class", _classes, _classIcons);
            
            ImGui.SetCursorPosX(15f * PluginUI.AppScale);
            DrawIconGridRow("DoL", _dol, _dolIcons);
            
            ImGui.SetCursorPosX(15f * PluginUI.AppScale);
            DrawIconGridRow("DoH", _doh, _dohIcons);
            
            ImGui.EndChild();
        }
        
        private void DrawIconGridRow(string label, string[] jobs, Dictionary<string, uint> ids)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), label);
            ImGui.SameLine(70f * PluginUI.AppScale);
            
            float startX = ImGui.GetCursorPosX();
            float x = startX;
            float y = ImGui.GetCursorPosY();
            float btnSize = 26f * PluginUI.AppScale;
            float gap = 4f * PluginUI.AppScale;
            float maxWidth = ImGui.GetContentRegionAvail().X;
            
            foreach (var job in jobs)
            {
                if (x + btnSize + gap > startX + maxWidth)
                {
                    x = startX;
                    y += btnSize + gap;
                }
                
                ImGui.SetCursorPos(new Vector2(x, y));
                
                bool selected = _selectedJobs.Contains(job);
                Vector4 bg = selected ? new Vector4(0, 0.8f, 1f, 0.15f) : new Vector4(0, 0, 0, 0);
                Vector4 border = selected ? new Vector4(0, 0.8f, 1f, 1f) : new Vector4(0.2f, 0.2f, 0.2f, 0.5f);
                
                Vector2 bp = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddRectFilled(bp, bp + new Vector2(btnSize, btnSize), ImGui.ColorConvertFloat4ToU32(bg), 6f);
                ImGui.GetWindowDrawList().AddRect(bp, bp + new Vector2(btnSize, btnSize), ImGui.ColorConvertFloat4ToU32(border), 6f);
                
                ImGui.SetCursorScreenPos(bp + new Vector2(4f, 4f));
                
                if (ids.ContainsKey(job))
                {
                    var tex = _textureProvider.GetFromGameIcon(new GameIconLookup(ids[job])).GetWrapOrDefault();
                    if (tex != null)
                    {
                        ImGui.Image(tex.Handle, new Vector2(18f * PluginUI.AppScale, 18f * PluginUI.AppScale), Vector2.Zero, Vector2.One, selected ? Vector4.One : new Vector4(1,1,1,0.4f));
                    }
                }
                
                ImGui.SetCursorScreenPos(bp);
                if (ImGui.InvisibleButton("job_" + job, new Vector2(btnSize, btnSize)))
                {
                    if (selected) _selectedJobs.Remove(job);
                    else _selectedJobs.Add(job);
                    PerformSearch();
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(job);
                
                x += btnSize + gap;
            }
            ImGui.SetCursorPosY(y + btnSize + 15f * PluginUI.AppScale);
        }

        private void DrawSearchAndSortBar()
        {
            Vector2 p = ImGui.GetCursorScreenPos();
            float cardHeight = 85f * PluginUI.AppScale;
            float totalWidth = ImGui.GetContentRegionAvail().X;
            
            UIHelper.DrawCard(p, new Vector2(totalWidth, cardHeight), new Vector4(0, 0, 0, 0.2f), 8f * PluginUI.AppScale, new Vector4(0.3f, 0.3f, 0.4f, 0.6f));
            
            Vector2 btnPos = p + new Vector2(10f * PluginUI.AppScale, 10f * PluginUI.AppScale);
            if (UIHelper.DrawPremiumButton("hideFiltersBtn", btnPos, new Vector2(120f * PluginUI.AppScale, 30f * PluginUI.AppScale), _showFilters ? "Hide Filters" : "Show Filters", new Vector4(0, 0, 0, 0.3f), new Vector4(0.2f, 0.2f, 0.3f, 0.8f), new Vector4(0.9f, 0.9f, 0.9f, 1f), Vector4.One))
            {
                _showFilters = !_showFilters;
            }
            
            ImGui.SetCursorScreenPos(p + new Vector2(totalWidth - 275f * PluginUI.AppScale, 10f * PluginUI.AppScale));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Sort by:");
            ImGui.SameLine();
            
            string[] sortModes = { "Relevance", "Name", "Level", "iLvl" };
            ImGui.PushItemWidth(100f * PluginUI.AppScale);
            if (ImGui.BeginCombo("##sortMode", sortModes[_sortMode]))
            {
                for (int i = 0; i < sortModes.Length; i++)
                {
                    if (ImGui.Selectable(sortModes[i], _sortMode == i)) _sortMode = i;
                }
                ImGui.EndCombo();
            }
            ImGui.PopItemWidth();
            
            ImGui.SameLine();
            string[] dirs = { "Ascending", "Descending" };
            ImGui.PushItemWidth(100f * PluginUI.AppScale);
            if (ImGui.BeginCombo("##sortDir", dirs[_sortDesc ? 1 : 0]))
            {
                if (ImGui.Selectable("Ascending", !_sortDesc)) _sortDesc = false;
                if (ImGui.Selectable("Descending", _sortDesc)) _sortDesc = true;
            }
            ImGui.PopItemWidth();
            
            Vector2 sp = p + new Vector2(15f * PluginUI.AppScale, 45f * PluginUI.AppScale);
            UIHelper.DrawPremiumInputText("##searchBoxItems", sp, new Vector2(totalWidth - 30f * PluginUI.AppScale, 30f * PluginUI.AppScale), ref _searchQuery, 100);
            
            ImGui.SetCursorScreenPos(p + new Vector2(0, cardHeight + 5f * PluginUI.AppScale));
            if (ImGui.IsItemDeactivatedAfterEdit() || ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                PerformSearch();
            }
        }

        private void DrawSearchResults()
        {
            if (!_hasSearched) return;

            if (_searchResults.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Woops, nothing's here. Try adjusting your search or filters.");
                return;
            }

            // Apply sort
            var results = _searchResults.ToList();
            if (_sortMode == 1) results.Sort((a, b) => string.Compare(a.Recipe.ItemResult.Value.Name.ToString(), b.Recipe.ItemResult.Value.Name.ToString()));
            else if (_sortMode == 2) results.Sort((a, b) => a.Recipe.RecipeLevelTable.RowId.CompareTo(b.Recipe.RecipeLevelTable.RowId));
            else if (_sortMode == 3) results.Sort((a, b) => a.Recipe.ItemResult.Value.LevelItem.RowId.CompareTo(b.Recipe.ItemResult.Value.LevelItem.RowId));
            
            if (!_sortDesc && _sortMode != 0) results.Reverse();

            UIHelper.BeginSmoothChild("searchResultsScroll2", new Vector2(0, 0), false);
            
            foreach (var result in results)
            {
                var recipe = result.Recipe;
                var item = recipe.ItemResult.Value;
                if (item.RowId == 0) continue;
                
                Vector2 p = ImGui.GetCursorScreenPos();
                float rowHeight = 55f * PluginUI.AppScale;
                Vector2 rowSize = new Vector2(ImGui.GetContentRegionAvail().X, rowHeight);
                
                Vector4 bBorder = new Vector4(0.1f, 0.1f, 0.1f, 0.8f);
                if (recipe.RowId != 0) bBorder = new Vector4(0.4f, 0.8f, 0.6f, 0.8f); // #68cfa8 matching
                
                UIHelper.DrawCard(p, rowSize, new Vector4(0, 0, 0, 0.3f), 8f * PluginUI.AppScale, bBorder);
                ImGui.SetCursorScreenPos(p + new Vector2(10f * PluginUI.AppScale, 8f * PluginUI.AppScale));

                // Icon
                if (result.IconTexture != null && result.IconTexture.GetWrapOrDefault() != null)
                {
                    ImGui.Image(result.IconTexture.GetWrapOrDefault().Handle, new Vector2(38f * PluginUI.AppScale, 38f * PluginUI.AppScale));
                }
                else
                {
                    ImGui.Dummy(new Vector2(38f * PluginUI.AppScale, 38f * PluginUI.AppScale));
                }

                ImGui.SameLine();
                ImGui.SetCursorScreenPos(p + new Vector2(55f * PluginUI.AppScale, 8f * PluginUI.AppScale));
                
                ImGui.BeginGroup();
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), item.Name.ToString());
                
                // Badges
                ImGui.Dummy(new Vector2(0, 4f * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"Lv {item.LevelEquip} iLvl {item.LevelItem.RowId}");
                ImGui.EndGroup();

                ImGui.SameLine();
                float rightAlign = rowSize.X - 105f * PluginUI.AppScale;
                ImGui.SetCursorScreenPos(p + new Vector2(rightAlign - 70f * PluginUI.AppScale, 13f * PluginUI.AppScale));
                
                if (!_addQuantities.ContainsKey((int)recipe.RowId)) _addQuantities[(int)recipe.RowId] = 1;
                int qty = _addQuantities[(int)recipe.RowId];
                
                ImGui.PushItemWidth(60f * PluginUI.AppScale);
                ImGui.InputInt("##qty_" + recipe.RowId, ref qty, 0);
                ImGui.PopItemWidth();
                if (qty < 1) qty = 1;
                _addQuantities[(int)recipe.RowId] = qty;

                ImGui.SameLine();
                Vector2 btnPos = p + new Vector2(rightAlign, 9f * PluginUI.AppScale);
                
                if (UIHelper.DrawPremiumButton("Add##" + recipe.RowId, btnPos, new Vector2(90f * PluginUI.AppScale, 36f * PluginUI.AppScale), "Add", new Vector4(0.28f, 0.5f, 0.69f, 1f), new Vector4(0.4f, 0.8f, 0.66f, 1f), Vector4.One, Vector4.One))
                {
                    var existing = _configuration.CraftingDraftPipeline.FirstOrDefault(x => x.RecipeId == recipe.RowId);
                    if (existing != null) existing.Amount += qty;
                    else _configuration.CraftingDraftPipeline.Add(new CraftingPipelineItem { RecipeId = recipe.RowId, Amount = qty });
                    _configuration.Save();
                }
                
                ImGui.SetCursorScreenPos(p + new Vector2(0, rowHeight + 10f * PluginUI.AppScale));
            }
            ImGui.EndChild();
        }

        private void DrawDraftPipeline()
        {
            Vector2 p = ImGui.GetCursorScreenPos();
            Vector2 s = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y);
            
            UIHelper.DrawCard(p, s, new Vector4(0, 0, 0, 0.3f), 8f * PluginUI.AppScale, new Vector4(0.1f, 0.1f, 0.1f, 0.8f));
            
            ImGui.SetCursorScreenPos(p + new Vector2(15f * PluginUI.AppScale, 15f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), "Draft Pipeline");
            ImGui.SameLine(s.X - 80f * PluginUI.AppScale);
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"{_configuration.CraftingDraftPipeline.Count} items");
            
            ImGui.SetCursorScreenPos(p + new Vector2(0, 45f * PluginUI.AppScale));
            ImGui.Separator();
            
            ImGui.SetCursorScreenPos(p + new Vector2(15f * PluginUI.AppScale, 60f * PluginUI.AppScale));
            Vector2 btnPos = ImGui.GetCursorScreenPos();
            if (UIHelper.DrawPremiumButton("sendPipelineBtn", btnPos, new Vector2(s.X - 30f * PluginUI.AppScale, 40f * PluginUI.AppScale), "Send to Pipeline", new Vector4(0.8f, 0.6f, 0.1f, 1f), new Vector4(0.9f, 0.7f, 0.2f, 1f), Vector4.One, Vector4.One))
            {
                foreach (var draft in _configuration.CraftingDraftPipeline)
                {
                    var existing = _configuration.CraftingActivePipeline.FirstOrDefault(x => x.RecipeId == draft.RecipeId);
                    if (existing != null) existing.Amount += draft.Amount;
                    else _configuration.CraftingActivePipeline.Add(new CraftingPipelineItem { RecipeId = draft.RecipeId, Amount = draft.Amount });
                }
                _configuration.CraftingDraftPipeline.Clear();
                _cachedRollup = null;
                _configuration.Save();
            }
            
            ImGui.SetCursorScreenPos(p + new Vector2(15f * PluginUI.AppScale, 115f * PluginUI.AppScale));
            if (_configuration.CraftingDraftPipeline.Count == 0)
            {
                ImGui.Dummy(new Vector2(0, 20f * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Your draft is empty.\nSearch for recipes and add\nthem here before sending to pipeline.");
            }
            else
            {
                ImGui.BeginChild("draftScroll", new Vector2(s.X - 30f * PluginUI.AppScale, s.Y - 130f * PluginUI.AppScale), false);
                var toRemove = new List<CraftingPipelineItem>();
                var sheet = _dataManager.GetExcelSheet<Recipe>();
                
                foreach (var item in _configuration.CraftingDraftPipeline)
                {
                    var recipe = sheet?.GetRow(item.RecipeId);
                    if (recipe == null || recipe.Value.RowId == 0) continue;
                    
                    Vector2 dp = ImGui.GetCursorScreenPos();
                    UIHelper.DrawCard(dp, new Vector2(ImGui.GetContentRegionAvail().X, 40f * PluginUI.AppScale), new Vector4(0.1f, 0.1f, 0.1f, 0.5f), 4f * PluginUI.AppScale, new Vector4(0.2f, 0.2f, 0.2f, 0.5f));
                    
                    ImGui.SetCursorScreenPos(dp + new Vector2(10f * PluginUI.AppScale, 12f * PluginUI.AppScale));
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), recipe.Value.ItemResult.Value.Name.ToString());
                    
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 80f * PluginUI.AppScale);
                    ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.9f, 1f), $"x{item.Amount}");
                    
                    ImGui.SameLine(ImGui.GetContentRegionAvail().X - 30f * PluginUI.AppScale);
                    Vector2 xBtnPos = ImGui.GetCursorScreenPos();
                    if (UIHelper.DrawPremiumButton("del_" + item.RecipeId, xBtnPos, new Vector2(25f * PluginUI.AppScale, 20f * PluginUI.AppScale), "X", new Vector4(0.83f, 0.69f, 0.22f, 1f), new Vector4(0.93f, 0.79f, 0.32f, 1f), Vector4.One, Vector4.One))
                    {
                        toRemove.Add(item);
                    }
                    ImGui.SetCursorScreenPos(dp + new Vector2(0, 45f * PluginUI.AppScale));
                }
                
                foreach(var r in toRemove) _configuration.CraftingDraftPipeline.Remove(r);
                if (toRemove.Count > 0) _configuration.Save();
                
                ImGui.EndChild();
            }
        }


        private void PerformSearch()
        {
            if (string.IsNullOrWhiteSpace(_searchQuery) && !_craftableOnly && !_collectableOnly)
            {
                _searchResults.Clear();
                _hasSearched = false;
                return;
            }

            var sheet = _dataManager.GetExcelSheet<Recipe>();
            if (sheet == null) return;
            
            int reqLvlMinParsed = string.IsNullOrWhiteSpace(_reqLevelMin) ? 0 : (int.TryParse(_reqLevelMin, out int v1) ? v1 : 0);
            int reqLvlMaxParsed = string.IsNullOrWhiteSpace(_reqLevelMax) ? int.MaxValue : (int.TryParse(_reqLevelMax, out int v2) ? v2 : int.MaxValue);
            int iLvlMinParsed = string.IsNullOrWhiteSpace(_iLvlMin) ? 0 : (int.TryParse(_iLvlMin, out int v3) ? v3 : 0);
            int iLvlMaxParsed = string.IsNullOrWhiteSpace(_iLvlMax) ? int.MaxValue : (int.TryParse(_iLvlMax, out int v4) ? v4 : int.MaxValue);
            int rLvlMinParsed = string.IsNullOrWhiteSpace(_rLvlMin) ? 0 : (int.TryParse(_rLvlMin, out int v5) ? v5 : 0);
            int rLvlMaxParsed = string.IsNullOrWhiteSpace(_rLvlMax) ? int.MaxValue : (int.TryParse(_rLvlMax, out int v6) ? v6 : int.MaxValue);

            string queryLower = _searchQuery.ToLowerInvariant();
            
            var raw = sheet.Where(r => 
            {
                var item = r.ItemResult.Value;
                if (item.RowId == 0) return false;

                if (!string.IsNullOrWhiteSpace(_searchQuery) && !item.Name.ToString().ToLowerInvariant().Contains(queryLower)) return false;
                
                var rLvl = r.RecipeLevelTable.Value;
                if (rLvl.RowId == 0) return false;

                if (rLvl.ClassJobLevel < reqLvlMinParsed || rLvl.ClassJobLevel > reqLvlMaxParsed) return false;
                if (r.RecipeLevelTable.RowId < rLvlMinParsed || r.RecipeLevelTable.RowId > rLvlMaxParsed) return false;
                if (item.LevelItem.RowId < iLvlMinParsed || item.LevelItem.RowId > iLvlMaxParsed) return false;
                
                if (_craftedByJob != -1 && r.CraftType.RowId != _craftedByJob) return false;
                
                if (_collectableOnly && r.ItemResult.Value.IsCollectable == false) return false;

                if (_selectedJobs.Count > 0)
                {
                    var cjc = item.ClassJobCategory.Value;
                    if (cjc.RowId == 0) return false;
                    
                    bool match = false;
                    foreach (var j in _selectedJobs)
                    {
                        var prop = cjc.GetType().GetProperty(j);
                        if (prop != null && prop.PropertyType == typeof(bool))
                        {
                            if ((bool)prop.GetValue(cjc)) { match = true; break; }
                        }
                        else if (prop != null && prop.PropertyType == typeof(byte))
                        {
                            if ((byte)prop.GetValue(cjc) > 0) { match = true; break; }
                        }
                    }
                    if (!match) return false;
                }

                return true;
            }).Take(50).ToList();

            _searchResults.Clear();
            foreach (var r in raw)
            {
                var wrap = new SearchResultWrapper { Recipe = r };
                if (r.ItemResult.Value.Icon != 0)
                {
                    wrap.IconTexture = _textureProvider.GetFromGameIcon(new GameIconLookup(r.ItemResult.Value.Icon));
                }
                if (r.CraftType.RowId >= 0 && r.CraftType.RowId <= 7)
                {
                    wrap.JobIconTexture = _textureProvider.GetFromGameIcon(new GameIconLookup(_jobIconIds[r.CraftType.RowId + 1]));
                }
                _searchResults.Add(wrap);
            }

            _hasSearched = true;
        }
    }
}
