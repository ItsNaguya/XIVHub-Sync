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
        private class MaterialRollup
        {
            public uint ItemId { get; set; }
            public int Amount { get; set; }
            public string Category { get; set; }
            public string Zone { get; set; }
            public int Tier { get; set; }
            public string SpawnTimes { get; set; }
            public int InventoryCount { get; set; }
            public uint IconId { get; set; }
            public ISharedImmediateTexture IconTexture { get; set; }
            public string Name { get; set; }
            public int Stars { get; set; }
            public uint AetheryteId { get; set; }
            public bool IsReadyToCraft { get; set; }
            public int EffectiveAmount { get; set; }
        }

        private List<MaterialRollup> _cachedRollup = null;
        private Dictionary<string, bool> _categoryCollapsedState = new();
        private Dictionary<string, bool> _categoryDoneState = new();

        private Dictionary<uint, int> _priceCache = new();
        private Dictionary<uint, DateTime> _priceCacheTime = new();
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
        private bool _isFetchingPrices = false;
        
        private async void FetchPrices(List<uint> itemIds)
        {
            if (_isFetchingPrices || itemIds.Count == 0) return;
            
            var toFetch = itemIds.Where(id => !_priceCacheTime.ContainsKey(id) || (DateTime.Now - _priceCacheTime[id]).TotalMinutes > 30).Distinct().ToList();
            if (toFetch.Count == 0) return;

            _isFetchingPrices = true;
            try
            {
                System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Starting fetch for {toFetch.Count} items...\n");
                string worldName = "Europe"; 
                for (int i = 0; i < toFetch.Count; i += 5)
                {
                    var chunk = toFetch.Skip(i).Take(5).ToList();
                    string idsStr = string.Join(",", chunk);
                    string url = $"https://universalis.app/api/v2/{worldName}/{idsStr}";
                    
                    System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Requesting: {url}\n");
                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    request.Headers.Add("User-Agent", "XIVHubCompanion/1.0");

                    var response = await _httpClient.SendAsync(request);
                    System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Response Code: {response.StatusCode}\n");
                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"JSON received, length: {json.Length}\n");
                        var jDoc = System.Text.Json.JsonDocument.Parse(json);
                        
                        if (jDoc.RootElement.TryGetProperty("items", out var itemsElement))
                        {
                            System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Found 'items' object\n");
                            foreach (var prop in itemsElement.EnumerateObject())
                            {
                                if (uint.TryParse(prop.Name, out uint id))
                                {
                                    if (prop.Value.TryGetProperty("minPrice", out var minPriceElement) && minPriceElement.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    {
                                        _priceCache[id] = minPriceElement.GetInt32();
                                    }
                                }
                            }
                        }
                        else
                        {
                            System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Did not find 'items' object\n");
                            if (jDoc.RootElement.TryGetProperty("minPrice", out var singleMinPrice) && singleMinPrice.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                _priceCache[chunk[0]] = singleMinPrice.GetInt32();
                            }
                        }
                        
                        foreach (var id in chunk)
                        {
                            if (!_priceCache.ContainsKey(id)) _priceCache[id] = 0;
                            _priceCacheTime[id] = DateTime.Now;
                        }
                    }
                    else
                    {
                        string err = await response.Content.ReadAsStringAsync();
                        System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Error response: {err}\n");
                    }
                }
                System.IO.File.AppendAllText(@"D:\AntiGravity\XIV Hub\universalis_debug.txt", $"Fetch complete. Cache has {_priceCache.Count} items.\n");
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(@"D:\AntiGravity\XIV Hub\universalis_error.txt", ex.ToString()); } catch { }
            }
            finally
            {
                _isFetchingPrices = false;
            }
        }

        private void DrawActivePipelineTab()
        {
            float width = ImGui.GetContentRegionAvail().X;
            float leftPanelWidth = 350f * PluginUI.AppScale;
            float rightPanelWidth = width - leftPanelWidth - 20f * PluginUI.AppScale;

            UIHelper.BeginSmoothChild("activeTargets", new Vector2(leftPanelWidth, 0), true);
            DrawActiveTargets();
            ImGui.EndChild();

            ImGui.SameLine();

            UIHelper.BeginSmoothChild("materialRollup", new Vector2(rightPanelWidth, 0), true);
            DrawMaterialRollup();
            ImGui.EndChild();
            
            if (_showPipelineCompletedPopup) ImGui.OpenPopup("Pipeline Completed");
            if (ImGui.BeginPopupModal("Pipeline Completed", ref _showPipelineCompletedPopup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextColored(new Vector4(0.4f, 1f, 0.4f, 1f), "You have finished crafting all items in the pipeline!");
                ImGui.Dummy(new Vector2(0, 5f * PluginUI.AppScale));
                ImGui.Text("Would you like to clear the pipeline?");
                ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
                
                if (UIHelper.DrawPremiumWarningButton("btn_clear_pipe_yes", ImGui.GetCursorScreenPos(), new Vector2(150f * PluginUI.AppScale, 30f * PluginUI.AppScale), "Yes, Clear Pipeline"))
                {
                    _configuration.CraftingActivePipeline.Clear();
                    _configuration.Save();
                    _cachedRollup = null;
                    _showPipelineCompletedPopup = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (UIHelper.DrawPremiumButton("btn_clear_pipe_no", ImGui.GetCursorScreenPos(), new Vector2(120f * PluginUI.AppScale, 30f * PluginUI.AppScale), "No, Keep It", new Vector4(0.12f, 0.12f, 0.14f, 1f), new Vector4(0.2f, 0.2f, 0.22f, 1f), new Vector4(0.7f, 0.7f, 0.7f, 1f), Vector4.One))
                {
                    _showPipelineCompletedPopup = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private void DrawActiveTargets()
        {
            ImGui.TextColored(new Vector4(0.8f, 0.7f, 0.2f, 1f), "ACTIVE TARGETS");
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));

            if (_configuration.CraftingActivePipeline.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No active targets.");
                return;
            }

            bool pipelineChanged = false;

            ImGui.BeginChild("targetList", new Vector2(0, ImGui.GetContentRegionAvail().Y - 60f * PluginUI.AppScale), false);
            for (int i = 0; i < _configuration.CraftingActivePipeline.Count; i++)
            {
                var target = _configuration.CraftingActivePipeline[i];
                var recipe = _dataManager.GetExcelSheet<Recipe>()?.GetRow(target.RecipeId);
                if (recipe != null && recipe.Value.RowId != 0)
                {
                    var item = recipe.Value.ItemResult.Value;
                    
                    Vector2 p = ImGui.GetCursorScreenPos();
                    float rowHeight = 45f * PluginUI.AppScale;
                    Vector2 rowSize = new Vector2(ImGui.GetContentRegionAvail().X, rowHeight);
                    
                    UIHelper.DrawCard(p, rowSize, new Vector4(0.12f, 0.13f, 0.15f, 0.8f), 8f * PluginUI.AppScale, new Vector4(0.3f, 0.4f, 0.6f, 0.5f));
                    ImGui.SetCursorScreenPos(p + new Vector2(10f * PluginUI.AppScale, 6f * PluginUI.AppScale));

                    var tex = _textureProvider.GetFromGameIcon(new GameIconLookup(item.Icon));
                    if (tex != null && tex.GetWrapOrDefault() != null)
                    {
                        ImGui.Image(tex.GetWrapOrDefault().Handle, new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                    }

                    ImGui.SameLine();
                    ImGui.SetCursorScreenPos(p + new Vector2(50f * PluginUI.AppScale, 6f * PluginUI.AppScale));
                    
                    ImGui.BeginGroup();
                    ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), item.Name.ToString());
                    if (recipe.Value.CraftType.RowId >= 0 && recipe.Value.CraftType.RowId <= 7)
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.9f, 1f), _jobAbbreviations[recipe.Value.CraftType.RowId + 1]);
                    }
                    ImGui.EndGroup();
                    
                    ImGui.SameLine();
                    float rightAlign = rowSize.X - 85f * PluginUI.AppScale;
                    ImGui.SetCursorScreenPos(p + new Vector2(rightAlign, 10f * PluginUI.AppScale));
                    
                    int amt = target.Amount;
                    ImGui.SetNextItemWidth(45f * PluginUI.AppScale);
                    if (ImGui.InputInt("##at_" + target.RecipeId, ref amt, 0))
                    {
                        if (amt <= 0)
                        {
                            _configuration.CraftingActivePipeline.RemoveAt(i);
                            i--;
                        }
                        else target.Amount = amt;
                        _configuration.Save();
                        pipelineChanged = true;
                    }
                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (UIHelper.DrawPremiumButton("##del_" + target.RecipeId, ImGui.GetCursorScreenPos(), new Vector2(25f * PluginUI.AppScale, 25f * PluginUI.AppScale), "\uF00D", new Vector4(0.12f, 0.12f, 0.14f, 1f), new Vector4(0.83f, 0.69f, 0.22f, 1f), Vector4.One, Vector4.One))
                    {
                        _configuration.CraftingActivePipeline.RemoveAt(i);
                        i--;
                        _configuration.Save();
                        pipelineChanged = true;
                    }
                    ImGui.PopFont();

                    ImGui.SetCursorScreenPos(p + new Vector2(0, rowHeight + 5f * PluginUI.AppScale));
                }
            }
            ImGui.EndChild();

            if (pipelineChanged) _cachedRollup = null;

            if (UIHelper.DrawPremiumButton("clearPipe", Vector2.Zero, new Vector2(ImGui.GetContentRegionAvail().X, 35f * PluginUI.AppScale), "Clear Pipeline", new Vector4(0.2f, 0.2f, 0.2f, 1f), new Vector4(0.3f, 0.3f, 0.3f, 1f), Vector4.One, Vector4.One))
            {
                _configuration.CraftingActivePipeline.Clear();
                _configuration.Save();
                _cachedRollup = null;
            }
        }

        private bool _showPipelineCompletedPopup = false;
        private bool _wasPipelineCompleted = false;

        private void DrawMaterialRollup()
        {
            if (_cachedRollup == null)
            {
                CalculateRollup();
            }

            if (_cachedRollup == null || _cachedRollup.Count == 0) return;

            // Pipeline Completion Check
            bool allTargetsDone = true;
            foreach (var target in _configuration.CraftingActivePipeline)
            {
                int inv = GetTotalInventoryItemCount(target.ItemId);
                if (inv < target.Amount) { allTargetsDone = false; break; }
            }

            if (allTargetsDone && !_wasPipelineCompleted)
            {
                _wasPipelineCompleted = true;
                _showPipelineCompletedPopup = true;
            }
            var recipeSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Recipe>();
            
            // 1. Reset EffectiveAmount
            foreach (var mat in _cachedRollup)
            {
                mat.InventoryCount = GetTotalInventoryItemCount(mat.ItemId);
                mat.EffectiveAmount = 0;
            }

            // 2. Add base requirements from Pipeline targets
            foreach (var target in _configuration.CraftingActivePipeline)
            {
                var mat = _cachedRollup.FirstOrDefault(x => x.ItemId == target.ItemId && x.Category == "Final");
                if (mat != null) mat.EffectiveAmount += target.Amount;
            }

            // 3. Process top-down
            var sortedMats = _cachedRollup.Where(x => x.Category == "Final" || x.Category == "Pre-craft")
                                          .OrderByDescending(x => x.Category == "Final" ? 999 : x.Tier)
                                          .ToList();

            foreach (var mat in sortedMats)
            {
                int amountToCraft = Math.Max(0, mat.EffectiveAmount - mat.InventoryCount);
                if (amountToCraft > 0 && recipeSheet != null)
                {
                    if (_recipeIdByResult.TryGetValue(mat.ItemId, out var recId) && recId != 0)
                    {
                        var recipe = recipeSheet.GetRowOrDefault(recId);
                        if (recipe.HasValue)
                        {
                            int craftsNeeded = (int)Math.Ceiling((double)amountToCraft / (recipe.Value.AmountResult > 0 ? recipe.Value.AmountResult : 1));
                            for (int j = 0; j < recipe.Value.Ingredient.Count; j++)
                            {
                                uint ingId = recipe.Value.Ingredient[j].RowId;
                                int amt = recipe.Value.AmountIngredient[j];
                                if (ingId != 0 && ingId != uint.MaxValue && amt > 0)
                                {
                                    var ingMat = _cachedRollup.FirstOrDefault(x => x.ItemId == ingId);
                                    if (ingMat != null) ingMat.EffectiveAmount += amt * craftsNeeded;
                                }
                            }
                        }
                    }
                }
            }

            // 4. Update IsReadyToCraft for Pre-crafts and Finals based on effective needed
            foreach (var mat in sortedMats)
            {
                if (mat.InventoryCount >= mat.EffectiveAmount)
                {
                    mat.IsReadyToCraft = false; // Already done
                }
                else
                {
                    bool ready = true;
                    if (_recipeIdByResult.TryGetValue(mat.ItemId, out var recId) && recId != 0 && recipeSheet != null)
                    {
                        var recipe = recipeSheet.GetRowOrDefault(recId);
                        if (recipe.HasValue)
                        {
                            int amountToCraft = mat.EffectiveAmount - mat.InventoryCount;
                            int craftsNeeded = (int)Math.Ceiling((double)amountToCraft / (recipe.Value.AmountResult > 0 ? recipe.Value.AmountResult : 1));
                            
                            for (int j = 0; j < recipe.Value.Ingredient.Count; j++)
                            {
                                uint ingId = recipe.Value.Ingredient[j].RowId;
                                int amt = recipe.Value.AmountIngredient[j];
                                if (ingId != 0 && ingId != uint.MaxValue && amt > 0)
                                {
                                    if (GetTotalInventoryItemCount(ingId) < (amt * craftsNeeded))
                                    {
                                        ready = false;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    mat.IsReadyToCraft = ready;
                }
            }

            long totalCost = 0;
            long totalValue = 0;
            foreach(var target in _configuration.CraftingActivePipeline)
            {
                if (_priceCache.TryGetValue(target.ItemId, out int targetPrice))
                {
                    totalValue += (long)targetPrice * target.Amount;
                }
            }
            
            foreach(var item in _cachedRollup)
            {
                if ((item.Category == "Gathering" || item.Category == "Timed Node" || item.Category == "Crystals" || item.Category == "Other") && _priceCache.TryGetValue(item.ItemId, out int price))
                {
                    totalCost += (long)price * item.Amount;
                }
            }
            long profit = totalValue - totalCost;

            ImGui.BeginGroup();
            
            // Draw Est Cost / Value / Profit badges
            Vector2 p = ImGui.GetCursorScreenPos();
            float badgeHeight = 44f * PluginUI.AppScale;
            float totalWidth = ImGui.GetContentRegionAvail().X;
            var drawList = ImGui.GetWindowDrawList();
            
            // Sleek premium background
            drawList.AddRectFilled(p, p + new Vector2(totalWidth, badgeHeight), UIHelper.Vec4ToU32(new Vector4(0.09f, 0.11f, 0.15f, 0.9f)), 6f * PluginUI.AppScale);
            drawList.AddRect(p, p + new Vector2(totalWidth, badgeHeight), UIHelper.Vec4ToU32(new Vector4(0.2f, 0.35f, 0.5f, 0.4f)), 6f * PluginUI.AppScale, 0, 1.5f);
            
            // Accent gradient bar on the left
            drawList.AddRectFilled(p, p + new Vector2(5f * PluginUI.AppScale, badgeHeight), UIHelper.Vec4ToU32(new Vector4(0.4f, 0.8f, 1f, 1f)), 6f * PluginUI.AppScale, ImDrawFlags.RoundCornersLeft);
            
            // Subtle separating lines
            drawList.AddLine(p + new Vector2(130f * PluginUI.AppScale, 8f * PluginUI.AppScale), p + new Vector2(130f * PluginUI.AppScale, badgeHeight - 8f * PluginUI.AppScale), UIHelper.Vec4ToU32(new Vector4(1f, 1f, 1f, 0.1f)), 1f);
            drawList.AddLine(p + new Vector2(260f * PluginUI.AppScale, 8f * PluginUI.AppScale), p + new Vector2(260f * PluginUI.AppScale, badgeHeight - 8f * PluginUI.AppScale), UIHelper.Vec4ToU32(new Vector4(1f, 1f, 1f, 0.1f)), 1f);

            // Cost Segment
            ImGui.SetCursorScreenPos(p + new Vector2(20f * PluginUI.AppScale, 6f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.55f, 1f), "EST. COST");
            ImGui.SetCursorScreenPos(p + new Vector2(20f * PluginUI.AppScale, 20f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), $"{totalCost:N0}");
            ImGui.SameLine(0, 2f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "g");

            // Value Segment
            ImGui.SetCursorScreenPos(p + new Vector2(150f * PluginUI.AppScale, 6f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.55f, 1f), "EST. VALUE");
            ImGui.SetCursorScreenPos(p + new Vector2(150f * PluginUI.AppScale, 20f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), $"{totalValue:N0}");
            ImGui.SameLine(0, 2f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "g");

            // Profit Segment
            ImGui.SetCursorScreenPos(p + new Vector2(280f * PluginUI.AppScale, 6f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.55f, 1f), "PROFIT");
            ImGui.SetCursorScreenPos(p + new Vector2(280f * PluginUI.AppScale, 20f * PluginUI.AppScale));
            ImGui.TextColored(new Vector4(0.2f, 0.9f, 0.6f, 1f), $"{profit:N0}");
            ImGui.SameLine(0, 2f);
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "g");

            // Fetch Button perfectly aligned to the right
            Vector2 btnPos = p + new Vector2(totalWidth - 140f * PluginUI.AppScale, 9f * PluginUI.AppScale);
            if (UIHelper.DrawPremiumButton("fetchPrices", btnPos, new Vector2(130f * PluginUI.AppScale, 26f * PluginUI.AppScale), _isFetchingPrices ? "Fetching..." : "Fetch Prices", new Vector4(0.15f, 0.35f, 0.7f, 1f), new Vector4(0.25f, 0.5f, 0.9f, 1f), Vector4.One, Vector4.One))
            {
                FetchPrices(_cachedRollup.Select(x => x.ItemId).ToList());
            }

            ImGui.SetCursorScreenPos(p + new Vector2(0, badgeHeight + 15f * PluginUI.AppScale));
            ImGui.EndGroup();

            ImGui.BeginChild("rollupList", new Vector2(0, 0), false);

            var crystals = _cachedRollup.Where(x => x.Category == "Crystals").ToList();
            if (crystals.Count > 0)
            {
                bool allDone = crystals.All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Crystals", crystals.Count, new Vector4(0.8f, 0.8f, 0.8f, 1f), allDone))
                {
                    DrawCrystalsRow(crystals);
                }
            }

            var timedByZone = _cachedRollup.Where(x => x.Category == "Timed Node").GroupBy(x => x.Zone).OrderBy(g => g.Key).ToList();
            if (timedByZone.Count > 0)
            {
                bool allDone = timedByZone.SelectMany(g => g).All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Timed nodes", timedByZone.Sum(g => g.Count()), new Vector4(0.2f, 0.6f, 0.3f, 1f), allDone))
                {
                    foreach(var grp in timedByZone) DrawGatheringCategory(grp.Key, grp.ToList());
                }
            }

            var gatheringByZone = _cachedRollup.Where(x => x.Category == "Gathering").GroupBy(x => x.Zone).OrderBy(g => g.Key).ToList();
            if (gatheringByZone.Count > 0)
            {
                bool allDone = gatheringByZone.SelectMany(g => g).All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Gathering", gatheringByZone.Sum(g => g.Count()), new Vector4(0.2f, 0.4f, 0.8f, 1f), allDone))
                {
                    foreach(var grp in gatheringByZone) DrawGatheringCategory(grp.Key, grp.ToList());
                }
            }

            var others = _cachedRollup.Where(x => x.Category == "Other").ToList();
            if (others.Count > 0)
            {
                bool allDone = others.All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Other Materials", others.Count, new Vector4(0.5f, 0.5f, 0.5f, 1f), allDone))
                {
                    DrawGatheringCategory("", others);
                }
            }

            var precraftsByTier = _cachedRollup.Where(x => x.Category == "Pre-craft").GroupBy(x => x.Tier).OrderByDescending(g => g.Key).ToList();
            if (precraftsByTier.Count > 0)
            {
                bool allDone = precraftsByTier.SelectMany(g => g).All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Pre-crafts", _cachedRollup.Count(x => x.Category == "Pre-craft"), new Vector4(0.6f, 0.4f, 0.8f, 1f), allDone))
                {
                    foreach(var grp in precraftsByTier) DrawGatheringCategory($"Tier {grp.Key}", grp.ToList());
                }
            }
            
            var finals = _cachedRollup.Where(x => x.Category == "Final").ToList();
            if (finals.Count > 0)
            {
                bool allDone = finals.All(x => x.InventoryCount >= x.EffectiveAmount);
                if (DrawSectionHeader("Items (Final crafts)", finals.Count, new Vector4(0.8f, 0.4f, 0.2f, 1f), allDone))
                {
                    DrawGatheringCategory("", finals);
                }
            }

            ImGui.EndChild();
        }

        private bool DrawSectionHeader(string title, int count, Vector4 color, bool allDone)
        {
            var drawList = ImGui.GetWindowDrawList();
            Vector2 p = ImGui.GetCursorScreenPos();
            float width = ImGui.GetContentRegionAvail().X;
            float height = ImGui.GetFrameHeight() + 10f * PluginUI.AppScale;

            bool wasDone = _categoryDoneState.TryGetValue(title, out var d) && d;
            if (allDone && !wasDone)
            {
                _categoryCollapsedState[title] = true;
            }
            _categoryDoneState[title] = allDone;

            if (!_categoryCollapsedState.TryGetValue(title, out bool isCollapsed))
            {
                isCollapsed = allDone;
                _categoryCollapsedState[title] = isCollapsed;
            }

            ImGui.SetCursorScreenPos(p);
            if (ImGui.InvisibleButton($"##btn_{title}", new Vector2(width, height)))
            {
                isCollapsed = !isCollapsed;
                _categoryCollapsedState[title] = isCollapsed;
            }
            bool isHovered = ImGui.IsItemHovered();

            if (allDone)
            {
                // Completed State - Beautiful Green Card
                drawList.AddRectFilled(p, p + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.05f, 0.2f, 0.1f, isHovered ? 0.8f : 0.6f)), 4f * PluginUI.AppScale);
                drawList.AddRect(p, p + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.1f, 0.4f, 0.2f, isHovered ? 0.6f : 0.4f)), 4f * PluginUI.AppScale);

                // Draw title
                Vector2 titlePos = p + new Vector2(15f * PluginUI.AppScale, 6f * PluginUI.AppScale);
                drawList.AddText(titlePos, ImGui.GetColorU32(new Vector4(0.5f, 0.9f, 0.5f, 1f)), title);
                
                // Draw count badge
                string countText = $"{count} items";
                var titleSize = ImGui.CalcTextSize(title);
                var countSize = ImGui.CalcTextSize(countText);
                Vector2 badgePos = p + new Vector2(15f * PluginUI.AppScale + titleSize.X + 15f * PluginUI.AppScale, 4f * PluginUI.AppScale);
                drawList.AddRectFilled(badgePos, badgePos + new Vector2(countSize.X + 10f * PluginUI.AppScale, countSize.Y + 4f * PluginUI.AppScale), ImGui.GetColorU32(new Vector4(0.2f, 0.6f, 0.3f, 0.3f)), 10f * PluginUI.AppScale);
                drawList.AddText(badgePos + new Vector2(5f * PluginUI.AppScale, 2f * PluginUI.AppScale), ImGui.GetColorU32(new Vector4(0.5f, 0.9f, 0.5f, 1f)), countText);
                
                // Draw Checkmark icon
                ImGui.PushFont(UiBuilder.IconFont);
                var checkSize = ImGui.CalcTextSize("\uF00C");
                drawList.AddText(p + new Vector2(width - checkSize.X - 15f * PluginUI.AppScale, 7f * PluginUI.AppScale), ImGui.GetColorU32(new Vector4(0.3f, 0.8f, 0.3f, 1f)), "\uF00C");
                ImGui.PopFont();

                ImGui.SetCursorScreenPos(p + new Vector2(0, height + 6f * PluginUI.AppScale));
                return !isCollapsed;
            }
            else
            {
                // Open State - Sleek premium card header
                drawList.AddRectFilled(p, p + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.13f, isHovered ? 0.9f : 0.8f)), 4f * PluginUI.AppScale);
                
                // Left accent bar
                drawList.AddRectFilled(p, p + new Vector2(4f * PluginUI.AppScale, height), ImGui.GetColorU32(color), 4f * PluginUI.AppScale, ImDrawFlags.RoundCornersLeft);
                
                // Title
                Vector2 titlePos = p + new Vector2(15f * PluginUI.AppScale, 6f * PluginUI.AppScale);
                drawList.AddText(titlePos, ImGui.GetColorU32(color), title);
                
                // Badge
                string countText = $"{count} items";
                var titleSize = ImGui.CalcTextSize(title);
                var countSize = ImGui.CalcTextSize(countText);
                Vector2 badgePos = titlePos + new Vector2(titleSize.X + 15f * PluginUI.AppScale, -2f * PluginUI.AppScale);
                drawList.AddRectFilled(badgePos, badgePos + new Vector2(countSize.X + 10f * PluginUI.AppScale, countSize.Y + 4f * PluginUI.AppScale), ImGui.GetColorU32(new Vector4(color.X, color.Y, color.Z, 0.15f)), 10f * PluginUI.AppScale);
                drawList.AddText(badgePos + new Vector2(5f * PluginUI.AppScale, 2f * PluginUI.AppScale), ImGui.GetColorU32(color), countText);

                ImGui.SetCursorScreenPos(p + new Vector2(0, height + 4f * PluginUI.AppScale));
                return !isCollapsed;
            }
        }

        private void DrawCrystalsRow(List<MaterialRollup> crystals)
        {
            Vector2 p = ImGui.GetCursorScreenPos();
            
            for (int i = 0; i < crystals.Count; i++)
            {
                var mat = crystals[i];
                ImGui.SetCursorScreenPos(p + new Vector2(i * 45f * PluginUI.AppScale, 0));
                
                if (mat.IconTexture != null && mat.IconTexture.GetWrapOrDefault() != null)
                {
                    ImGui.Image(mat.IconTexture.GetWrapOrDefault().Handle, new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                }
                
                ImGui.SetCursorScreenPos(p + new Vector2(i * 45f * PluginUI.AppScale, 34f * PluginUI.AppScale));
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), $"x{mat.Amount}");
            }
            ImGui.SetCursorScreenPos(p + new Vector2(0, 55f * PluginUI.AppScale));
        }

        private void DrawGatheringCategory(string title, List<MaterialRollup> items)
        {
            if (!string.IsNullOrEmpty(title))
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), title);
                ImGui.Dummy(new Vector2(0, 2f * PluginUI.AppScale));
            }

            foreach (var mat in items)
            {
                bool isDone = mat.InventoryCount >= mat.EffectiveAmount;

                Vector2 mp = ImGui.GetCursorScreenPos();
                float matHeight = 40f * PluginUI.AppScale;
                Vector2 matSize = new Vector2(ImGui.GetContentRegionAvail().X, matHeight);
                
                Vector4 bgCardCol = isDone ? new Vector4(0.05f, 0.15f, 0.08f, 0.5f) : (mat.IsReadyToCraft ? new Vector4(0.1f, 0.25f, 0.3f, 0.6f) : new Vector4(0.12f, 0.12f, 0.15f, 0.8f));
                Vector4 bgBorderCol = isDone ? new Vector4(0.2f, 0.6f, 0.3f, 0.4f) : (mat.IsReadyToCraft ? new Vector4(0.2f, 0.8f, 0.9f, 0.5f) : new Vector4(0.25f, 0.25f, 0.28f, 0.6f));

                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, isDone ? 0.5f : 1f);

                UIHelper.DrawCard(mp, matSize, bgCardCol, 4f * PluginUI.AppScale, bgBorderCol);
                ImGui.SetCursorScreenPos(mp + new Vector2(5f * PluginUI.AppScale, 4f * PluginUI.AppScale));

                if (mat.IconTexture != null && mat.IconTexture.GetWrapOrDefault() != null)
                {
                    ImGui.Image(mat.IconTexture.GetWrapOrDefault().Handle, new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                }
                else
                {
                    ImGui.Dummy(new Vector2(32f * PluginUI.AppScale, 32f * PluginUI.AppScale));
                }

                ImGui.SameLine();
                ImGui.SetCursorScreenPos(mp + new Vector2(45f * PluginUI.AppScale, 12f * PluginUI.AppScale));
                
                ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), mat.Name);
                if (mat.Stars > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(1f, 0.8f, 0.2f, 1f), new string('\u2605', mat.Stars));
                }

                float rightAlign = matSize.X - 250f * PluginUI.AppScale;
                Vector2 btnBasePos = mp + new Vector2(rightAlign, 8f * PluginUI.AppScale);
                float currentBtnX = btnBasePos.X;
                ImGui.SetCursorScreenPos(btnBasePos);
                
                if (mat.Category == "Gathering" || mat.Category == "Timed Node" || mat.Category == "Crystals")
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (UIHelper.DrawPremiumButton("btn_route_" + mat.ItemId, new Vector2(currentBtnX, btnBasePos.Y), new Vector2(24f * PluginUI.AppScale, 24f * PluginUI.AppScale), "\uF277", new Vector4(0.2f, 0.4f, 0.2f, 1f), new Vector4(0.3f, 0.6f, 0.3f, 1f), Vector4.One, Vector4.One))
                    {
                        var existingRoute = _configuration.GatheringActiveRoute.FirstOrDefault(x => x.ItemId == mat.ItemId);
                        if (existingRoute == null)
                        {
                            _configuration.GatheringActiveRoute.Add(new RouteItem { ItemId = mat.ItemId, TargetQuantity = mat.Amount });
                            _configuration.Save();
                        }
                    }
                    ImGui.PopFont();
                    if (ImGui.IsItemHovered())
                    {
                        UIHelper.BeginTooltip();
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 0.4f, 1f), "Send to Gathering Route");
                        UIHelper.EndTooltip();
                    }
                    currentBtnX += 30f * PluginUI.AppScale;
                    
                    if (mat.AetheryteId != 0)
                    {
                        ImGui.SameLine();
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (UIHelper.DrawPremiumButton("btn_tp_" + mat.ItemId, new Vector2(currentBtnX, btnBasePos.Y), new Vector2(24f * PluginUI.AppScale, 24f * PluginUI.AppScale), "\uF3C5", new Vector4(0.6f, 0.2f, 0.6f, 1f), new Vector4(0.7f, 0.3f, 0.7f, 1f), Vector4.One, Vector4.One))
                        {
                            GatheringApp.TeleportToAetheryte(mat.AetheryteId);
                        }
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered())
                        {
                            UIHelper.BeginTooltip();
                            ImGui.TextColored(new Vector4(1f, 0.8f, 0f, 1f), "Teleport");
                            UIHelper.EndTooltip();
                        }
                        currentBtnX += 30f * PluginUI.AppScale;
                    }
                    ImGui.SameLine();
                }

                if (mat.Category != "Final")
                {
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (UIHelper.DrawPremiumButton("btn_cart_" + mat.ItemId, new Vector2(currentBtnX, btnBasePos.Y), new Vector2(24f * PluginUI.AppScale, 24f * PluginUI.AppScale), "\uF07A", new Vector4(0.2f, 0.4f, 0.8f, 1f), new Vector4(0.3f, 0.5f, 0.9f, 1f), Vector4.One, Vector4.One))
                    {
                        MarketApp.OnAddToCart?.Invoke(new CartItem { id = (int)mat.ItemId, name = mat.Name, icon = mat.IconTexture != null ? $"/i/020000/{mat.IconId:000000}.png" : "", quantity = mat.Amount - mat.InventoryCount > 0 ? mat.Amount - mat.InventoryCount : 1, hq = false });
                    }
                    ImGui.PopFont();
                    if (ImGui.IsItemHovered())
                    {
                        UIHelper.BeginTooltip();
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "Send to Shopping Cart");
                        UIHelper.EndTooltip();
                    }
                    currentBtnX += 30f * PluginUI.AppScale;
                }

                if (!string.IsNullOrEmpty(mat.SpawnTimes))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f), mat.SpawnTimes);
                }

                ImGui.SameLine();
                ImGui.SetCursorScreenPos(mp + new Vector2(matSize.X - 70f * PluginUI.AppScale, 12f * PluginUI.AppScale));
                
                ImGui.TextColored(isDone ? new Vector4(0.5f, 0.9f, 0.5f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f), $"{mat.InventoryCount} / {mat.Amount}");
                if (isDone)
                {
                    ImGui.SameLine();
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1f), "\uF00C");
                    ImGui.PopFont();
                }

                ImGui.PopStyleVar();
                ImGui.SetCursorScreenPos(mp + new Vector2(0, matHeight + 2f * PluginUI.AppScale));
            }

            ImGui.Dummy(new Vector2(0, 10f * PluginUI.AppScale));
        }

        private unsafe int GetTotalInventoryItemCount(uint itemId)
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();
            if (manager == null) return 0;
            return manager->GetInventoryItemCount(itemId, false, false, false, 0) + manager->GetInventoryItemCount(itemId, true, false, false, 0);
        }

        private unsafe void CalculateRollup()
        {
            try
            {
                _cachedRollup = new List<MaterialRollup>();
                var sheet = _dataManager.GetExcelSheet<Recipe>();
                var itemSheet = _dataManager.GetExcelSheet<Item>();
                if (sheet == null || itemSheet == null) return;

                Dictionary<uint, int> rawMats = new Dictionary<uint, int>();
                Dictionary<uint, int> crystals = new Dictionary<uint, int>();
                Dictionary<uint, int> precrafts = new Dictionary<uint, int>();

                foreach (var target in _configuration.CraftingActivePipeline)
                {
                    if (target.RecipeId != 0)
                    {
                        var r = sheet.GetRow(target.RecipeId);
                        if (r.RowId != 0 && r.RowId != uint.MaxValue && r.ItemResult.Value.RowId != 0)
                        {
                            target.ItemId = r.ItemResult.Value.RowId; // Ensure target has ItemId for value calculation
                        }
                        ProcessRecipe(target.RecipeId, target.Amount, rawMats, precrafts, crystals, sheet);
                    }
                }

            var manager = FFXIVClientStructs.FFXIV.Client.Game.InventoryManager.Instance();

            foreach (var kvp in precrafts)
            {
                var i = itemSheet.GetRow(kvp.Key);
                int invCount = GetTotalInventoryItemCount(kvp.Key);
                _cachedRollup.Add(new MaterialRollup { ItemId = kvp.Key, Amount = kvp.Value, Category = "Pre-craft", Tier = 1, Name = i.Name.ToString() ?? "Unknown", IconId = i.Icon, IconTexture = i.RowId != 0 ? _textureProvider.GetFromGameIcon(new GameIconLookup(i.Icon)) : null, InventoryCount = invCount });
            }
            foreach (var kvp in rawMats)
            {
                var i = itemSheet.GetRow(kvp.Key);
                int invCount = GetTotalInventoryItemCount(kvp.Key);
                
                string category = "Other";
                string zone = "";
                string spawnTimes = "";
                int stars = 0;
                uint aetheryteId = 0;
                
                var node = GatheringApp.Nodes.FirstOrDefault(n => n.ItemId == kvp.Key);
                if (node != null)
                {
                    zone = node.zone ?? "Unknown Zone";
                    if (node.hours != null && node.hours.Count > 0)
                    {
                        category = "Timed Node";
                        spawnTimes = string.Join(", ", node.hours.Select(h => $"{h:00}:00"));
                    }
                    else
                    {
                        category = "Gathering";
                    }
                    stars = node.stats?.stars ?? 0;

                    var dn = GatheringApp.DataNodesMap.Values.FirstOrDefault(n => (n.items != null && n.items.Contains((int)kvp.Key)) || (n.hiddenItems != null && n.hiddenItems.Contains((int)kvp.Key)));
                    if (dn != null && dn.map > 0)
                    {
                        var maps = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
                        var mapRow = maps?.GetRowOrDefault((uint)dn.map);
                        if (mapRow.HasValue)
                        {
                            var territoryId = mapRow.Value.TerritoryType.RowId;
                            var aetherytes = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
                            var a = aetherytes?.FirstOrDefault(x => x.IsAetheryte && x.Territory.RowId == territoryId);
                            if (a.HasValue) aetheryteId = a.Value.RowId;
                        }
                    }
                }
                else
                {
                    var dn = GatheringApp.DataNodesMap.Values.FirstOrDefault(n => (n.items != null && n.items.Contains((int)kvp.Key)) || (n.hiddenItems != null && n.hiddenItems.Contains((int)kvp.Key)));
                    if (dn != null)
                    {
                        category = "Gathering";
                        zone = "Unknown Zone";

                        if (dn.zoneid > 0)
                        {
                            var placeSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>();
                            var place = placeSheet?.GetRowOrDefault((uint)dn.zoneid);
                            if (place.HasValue && !string.IsNullOrEmpty(place.Value.Name.ToString()))
                            {
                                zone = place.Value.Name.ToString();
                            }
                        }

                        if (dn.map > 0)
                        {
                            var maps = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>();
                            var mapRow = maps?.GetRowOrDefault((uint)dn.map);
                            if (mapRow.HasValue)
                            {
                                var territoryId = mapRow.Value.TerritoryType.RowId;
                                var aetherytes = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>();
                                var a = aetherytes?.FirstOrDefault(x => x.IsAetheryte && x.Territory.RowId == territoryId);
                                if (a.HasValue) aetheryteId = a.Value.RowId;
                            }
                        }
                    }
                }

                _cachedRollup.Add(new MaterialRollup { ItemId = kvp.Key, Amount = kvp.Value, Category = category, Zone = zone, SpawnTimes = spawnTimes, Stars = stars, Name = i.Name.ToString() ?? "Unknown", IconId = i.Icon, IconTexture = i.RowId != 0 ? _textureProvider.GetFromGameIcon(new GameIconLookup(i.Icon)) : null, InventoryCount = invCount, AetheryteId = aetheryteId });
            }
            foreach (var kvp in crystals)
            {
                var i = itemSheet.GetRow(kvp.Key);
                int invCount = GetTotalInventoryItemCount(kvp.Key);
                _cachedRollup.Add(new MaterialRollup { ItemId = kvp.Key, Amount = kvp.Value, Category = "Crystals", Name = i.Name.ToString() ?? "Unknown", IconId = i.Icon, IconTexture = i.RowId != 0 ? _textureProvider.GetFromGameIcon(new GameIconLookup(i.Icon)) : null, InventoryCount = invCount });
            }
            
            foreach (var target in _configuration.CraftingActivePipeline)
            {
                var r = sheet.GetRow(target.RecipeId);
                if (r.RowId == 0) continue;
                var i = r.ItemResult.Value;
                int invCount = manager != null ? manager->GetInventoryItemCount(i.RowId, false, false, false, 0) : 0;
                
                var existing = _cachedRollup.FirstOrDefault(x => x.ItemId == i.RowId && x.Category == "Final");
                if (existing != null)
                {
                    existing.Amount += target.Amount;
                }
                else
                {
                    _cachedRollup.Add(new MaterialRollup { ItemId = i.RowId, Amount = target.Amount, Category = "Final", Name = i.Name.ToString() ?? "Unknown", IconId = i.Icon, IconTexture = i.RowId != 0 ? _textureProvider.GetFromGameIcon(new GameIconLookup(i.Icon)) : null, InventoryCount = invCount });
                }
            }

            foreach (var mat in _cachedRollup)
            {
                if (mat.Category == "Pre-craft" || mat.Category == "Final")
                {
                    if (mat.InventoryCount >= mat.Amount)
                    {
                        mat.IsReadyToCraft = false;
                        continue;
                    }
                    
                    if (_recipeIdByResult.TryGetValue(mat.ItemId, out var recId) && recId != 0)
                    {
                        var recipe = sheet.GetRow(recId);
                        int neededRemaining = mat.Amount - mat.InventoryCount;
                        int craftsNeeded = (int)Math.Ceiling((double)neededRemaining / (recipe.AmountResult > 0 ? recipe.AmountResult : 1));
                        bool ready = true;
                        
                        for (int j = 0; j < recipe.Ingredient.Count; j++)
                        {
                            uint ingId = recipe.Ingredient[j].RowId;
                            int amt = recipe.AmountIngredient[j];
                            if (ingId != 0 && ingId != uint.MaxValue && amt > 0)
                            {
                                int reqTotal = amt * craftsNeeded;
                                if (GetTotalInventoryItemCount(ingId) < reqTotal)
                                {
                                    ready = false;
                                    break;
                                }
                            }
                        }
                        mat.IsReadyToCraft = ready;
                    }
                }
            }
            }
            catch (Exception ex)
            {
                // Log exception omitted for version compatibility
                _cachedRollup = new List<MaterialRollup>();
            }
        }

        private Dictionary<uint, uint>? _recipeIdByResult = null;

        private void InitRecipeCache(Lumina.Excel.ExcelSheet<Recipe> recipeSheet)
        {
            if (_recipeIdByResult != null) return;
            _recipeIdByResult = new Dictionary<uint, uint>();
            foreach (var r in recipeSheet)
            {
                if (r.RowId != uint.MaxValue && r.ItemResult.RowId != 0 && r.ItemResult.RowId != uint.MaxValue && !_recipeIdByResult.ContainsKey(r.ItemResult.RowId))
                {
                    _recipeIdByResult[r.ItemResult.RowId] = r.RowId;
                }
            }
        }

        private void ProcessRecipe(uint recipeId, int multiplier, Dictionary<uint, int> rawMats, Dictionary<uint, int> precrafts, Dictionary<uint, int> crystals, Lumina.Excel.ExcelSheet<Recipe> recipeSheet)
        {
            InitRecipeCache(recipeSheet);
            
            var recipe = recipeSheet.GetRow(recipeId);
            if (recipe.RowId == 0) return;

            var ingredientIds = new uint[recipe.Ingredient.Count];
            var ingredientAmts = new int[recipe.Ingredient.Count];
            var isCrystal = new bool[recipe.Ingredient.Count];

            var itemSheet = _dataManager.GetExcelSheet<Item>();

            for (int i = 0; i < recipe.Ingredient.Count; i++)
            {
                uint ingId = recipe.Ingredient[i].RowId;
                ingredientIds[i] = ingId;
                if (ingId != 0 && ingId != uint.MaxValue)
                {
                    var item = itemSheet.GetRow(ingId);
                    isCrystal[i] = item.ItemUICategory.RowId == 59;
                }
                ingredientAmts[i] = recipe.AmountIngredient[i];
            }

            for (int i = 0; i < ingredientIds.Length; i++)
            {
                uint ingId = ingredientIds[i];
                int amt = ingredientAmts[i];
                if (ingId != 0 && ingId != uint.MaxValue && amt > 0)
                {
                    int totalNeeded = amt * multiplier;
                    
                    if (_recipeIdByResult!.TryGetValue(ingId, out var subRecipeId) && subRecipeId != 0)
                    {
                        var subRecipe = recipeSheet.GetRow(subRecipeId);
                        int subAmountResult = subRecipe.AmountResult;
                        
                        if (precrafts.ContainsKey(ingId)) precrafts[ingId] += totalNeeded;
                        else precrafts[ingId] = totalNeeded;
                        
                        int craftsNeeded = (int)Math.Ceiling((double)totalNeeded / (subAmountResult > 0 ? subAmountResult : 1));
                        ProcessRecipe(subRecipeId, craftsNeeded, rawMats, precrafts, crystals, recipeSheet);
                    }
                    else if (isCrystal[i])
                    {
                        if (crystals.ContainsKey(ingId)) crystals[ingId] += totalNeeded;
                        else crystals[ingId] = totalNeeded;
                    }
                    else
                    {
                        if (rawMats.ContainsKey(ingId)) rawMats[ingId] += totalNeeded;
                        else rawMats[ingId] = totalNeeded;
                    }
                }
            }
        }
    }
}


