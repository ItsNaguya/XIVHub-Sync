using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using XIVHubCompanion.Collections;

namespace XIVHubCompanion.Apps
{
    public class CollectionApp : IApp
    {
        public string Name => "Collection";
        public string Icon => FontAwesomeIcon.Book.ToIconString();
        public bool IsVisible { get; set; } = false;

        private readonly DataSender _sender;
        private readonly CollectionService _collectionService;
        private readonly ITextureProvider _textureProvider;

        private CollectionCategory _selectedCategory = CollectionCategory.Mounts;
        private string _selectedSubcategory = "All";
        private string _searchQuery = "";
        private bool _showOnlyUnlocked = false;
        private Dictionary<CollectionCategory, bool> _categoryOpenState = new Dictionary<CollectionCategory, bool>();
        private Dictionary<CollectionCategory, float> _categoryAnimProgress = new Dictionary<CollectionCategory, float>();

        public CollectionApp(DataSender sender, CollectionService collectionService, ITextureProvider textureProvider)
        {
            _sender = sender;
            _collectionService = collectionService;
            _textureProvider = textureProvider;
        }

        public bool HasSettings => false;
        public void DrawSettings() { }
        public void Update() { }
        public void Dispose() { }

        public void Draw()
        {
            var region = ImGui.GetContentRegionAvail();
            
            float sidebarWidth = 240f;
            
            UIHelper.BeginSmoothChild("CollectionSidebar", new Vector2(sidebarWidth, region.Y), true);
            DrawSidebar();
            ImGui.EndChild();

            ImGui.SameLine();

            UIHelper.BeginSmoothChild("CollectionContent", new Vector2(0, region.Y), false);
            DrawContent();
            ImGui.EndChild();
        }

        private void DrawSidebar()
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.ParsedGold, FontAwesomeIcon.Book.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text("Categories");
            
            ImGui.Separator();
            ImGui.Spacing();

            foreach (CollectionCategory category in Enum.GetValues(typeof(CollectionCategory)))
            {
                var items = _collectionService.GetItems(category);
                int total = items.Count;
                if (total == 0) continue; 
                
                int unlocked = items.Count(x => x.IsUnlocked);
                
                bool isCategorySelected = _selectedCategory == category && _selectedSubcategory == "All";
                
                if (!_categoryOpenState.ContainsKey(category)) 
                    _categoryOpenState[category] = (category == CollectionCategory.Mounts);

                bool isOpen = _categoryOpenState[category];
                string icon = isOpen ? "▼" : "▶";
                
                Vector4 bgActive = new Vector4(0.0f, 0.4f, 0.7f, 0.4f);
                Vector4 bgNormal = new Vector4(0.12f, 0.12f, 0.14f, 1.0f);
                
                if (UIHelper.DrawPremiumButton($"btn_cat_{category}", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 35f * PluginUI.AppScale), $"{icon}  {category} ({unlocked}/{total})", isCategorySelected ? bgActive : bgNormal, bgActive, Vector4.One, Vector4.One))
                {
                    if (isCategorySelected) {
                        _categoryOpenState[category] = !isOpen;
                    } else {
                        _selectedCategory = category;
                        _selectedSubcategory = "All";
                        _categoryOpenState[category] = true;
                    }
                }

                float target = isOpen ? 1f : 0f;
                if (!_categoryAnimProgress.ContainsKey(category)) _categoryAnimProgress[category] = target;

                float current = _categoryAnimProgress[category];
                if (current != target)
                {
                    current = current + (target - current) * Math.Min(1f, 8f * ImGui.GetIO().DeltaTime);
                    if (Math.Abs(current - target) < 0.01f) current = target;
                    _categoryAnimProgress[category] = current;
                }

                if (current > 0.01f)
                {
                    var subcategories = items.Select(i => i.Subcategory).Distinct().OrderBy(s => s).ToList();
                    int validSubs = subcategories.Count(s => !string.IsNullOrEmpty(s));
                    float itemHeight = 28f * PluginUI.AppScale + ImGui.GetStyle().ItemSpacing.Y;
                    float totalHeight = validSubs * itemHeight;

                    UIHelper.BeginSmoothChild($"sub_{category}", new Vector2(0, totalHeight * current), false, ImGuiWindowFlags.NoScrollbar);
                    ImGui.PushStyleVar(ImGuiStyleVar.Alpha, current);

                    foreach (var sub in subcategories)
                    {
                        if (string.IsNullOrEmpty(sub)) continue;

                        int subTotal = items.Count(x => x.Subcategory == sub);
                        int subUnlocked = items.Count(x => x.Subcategory == sub && x.IsUnlocked);

                        bool isSubSelected = _selectedCategory == category && _selectedSubcategory == sub;
                        
                        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 15f * PluginUI.AppScale);
                        
                        if (UIHelper.DrawPremiumButton($"btn_sub_{category}_{sub}", ImGui.GetCursorScreenPos(), new Vector2(ImGui.GetContentRegionAvail().X, 28f * PluginUI.AppScale), $"{sub} ({subUnlocked}/{subTotal})", isSubSelected ? bgActive : new Vector4(0,0,0,0), bgActive, isSubSelected ? Vector4.One : new Vector4(0.7f, 0.7f, 0.7f, 1f), Vector4.One))
                        {
                            _selectedCategory = category;
                            _selectedSubcategory = sub;
                        }
                    }
                    
                    ImGui.PopStyleVar();
                    ImGui.EndChild();
                }
                ImGui.Spacing();
            }
        }

        private void DrawContent()
        {
            var items = _collectionService.GetItems(_selectedCategory);

            // Filtering
            ImGui.SetNextItemWidth(250f * PluginUI.AppScale);
            UIHelper.DrawPremiumInputText("txt_col_search", ImGui.GetCursorScreenPos(), new Vector2(250f * PluginUI.AppScale, 30f * PluginUI.AppScale), ref _searchQuery, 100);
            
            ImGui.SameLine();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5f * PluginUI.AppScale);
            ImGui.Checkbox("Owned Only", ref _showOnlyUnlocked);

            var filtered = items.Where(i => 
                (_selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory) &&
                (string.IsNullOrEmpty(_searchQuery) || i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) &&
                (!_showOnlyUnlocked || i.IsUnlocked)
            ).ToList();

            int total = items.Count(i => _selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory);
            int unlocked = items.Count(i => (_selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory) && i.IsUnlocked);
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), $"Category Progress: {unlocked} / {total} ({((total > 0 ? (float)unlocked/total*100f : 0)):F1}%)");
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, UIHelper.Vec4ToU32(new Vector4(0.0f, 0.65f, 1.0f, 1.0f)));
            ImGui.ProgressBar(total > 0 ? (float)unlocked/total : 0, new Vector2(-1, 4), "");
            ImGui.PopStyleColor();

            ImGui.Spacing();

            if (_selectedCategory == CollectionCategory.Achievements && unlocked == 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudYellow);
                ImGui.TextWrapped(FontAwesomeIcon.ExclamationTriangle.ToIconString() + " To load your achievements, please open the 'Achievements' menu in-game once per session. FFXIV only loads this data into memory when requested!");
                ImGui.PopStyleColor();
                ImGui.Spacing();
            }

            if (filtered.Count == 0)
            {
                ImGui.TextColored(ImGuiColors.DalamudGrey, "No items found.");
                return;
            }

            // Premium Grid rendering
            float cardWidth = 100f * PluginUI.AppScale; // Reduced to fit more cards per row
            float cardHeight = 135f * PluginUI.AppScale;
            float padding = 12f * PluginUI.AppScale;
            float availableWidth = ImGui.GetContentRegionAvail().X;
            int columns = Math.Max(1, (int)(availableWidth / (cardWidth + padding)));

            UIHelper.BeginSmoothChild("CollectionGrid", new Vector2(0, 0), false);

            if (ImGui.BeginTable("CollectionTable", columns))
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    if (i % columns == 0) ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    
                    var item = filtered[i];
                    DrawPremiumCard(item, cardWidth, cardHeight);
                }
                ImGui.EndTable();
            }

            ImGui.Dummy(new Vector2(0, 30)); // Add bottom padding to fix layout clipping
            ImGui.EndChild();
        }

        private void DrawPremiumCard(CollectionItem item, float width, float height)
        {
            var cursorPos = ImGui.GetCursorPos();
            var screenPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            
            float cornerRadius = 12f;
            var bgColor = item.IsUnlocked ? new Vector4(0.0f, 0.15f, 0.25f, 1f) : new Vector4(0.08f, 0.08f, 0.09f, 1f);
            var borderColor = item.IsUnlocked ? new Vector4(0.0f, 0.65f, 1.0f, 0.8f) : new Vector4(0.2f, 0.2f, 0.25f, 0.5f);
            
            drawList.AddRectFilled(screenPos, screenPos + new Vector2(width, height), ImGui.GetColorU32(bgColor), cornerRadius);
            drawList.AddRect(screenPos, screenPos + new Vector2(width, height), ImGui.GetColorU32(borderColor), cornerRadius, ImDrawFlags.None, item.IsUnlocked ? 1.5f : 1f);

            ImGui.SetCursorPos(cursorPos + new Vector2(5, 12));

            float iconSize = width - 40f;
            ImGui.SetCursorPosX(cursorPos.X + (width - iconSize) / 2);
            
            IDalamudTextureWrap texWrap = null;
            try
            {
                texWrap = _textureProvider.GetFromGameIcon(new GameIconLookup(item.IconId)).GetWrapOrDefault();
            }
            catch (Exception)
            {
                // Ignore missing icon exception
            }

            if (texWrap != null)
            {
                Vector4 tint = item.IsUnlocked ? Vector4.One : new Vector4(0.3f, 0.3f, 0.3f, 1f);
                ImGui.Image(texWrap.Handle, new Vector2(iconSize, iconSize), Vector2.Zero, Vector2.One, tint);
            }
            else
            {
                ImGui.Dummy(new Vector2(iconSize, iconSize));
            }

            ImGui.SetCursorPos(cursorPos + new Vector2(5, iconSize + 22));
            ImGui.PushTextWrapPos(cursorPos.X + width - 5);
            
            string display = item.Name;
            if (display.Length > 25) display = display.Substring(0, 22) + "..";
            
            float textWidth = ImGui.CalcTextSize(display).X;
            // Center text loosely
            ImGui.SetCursorPosX(cursorPos.X + (width - Math.Min(textWidth, width - 10)) / 2);
            
            ImGui.TextColored(item.IsUnlocked ? Vector4.One : ImGuiColors.DalamudGrey, display);
            ImGui.PopTextWrapPos();

            // Hover Tooltip overlay
            ImGui.SetCursorPos(cursorPos);
            ImGui.InvisibleButton($"##hover_{item.Id}", new Vector2(width, height));
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                
                ImGui.TextColored(item.IsUnlocked ? new Vector4(0.0f, 0.65f, 1.0f, 1.0f) : ImGuiColors.DalamudGrey, item.Name);
                
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 10);
                ImGui.TextColored(ImGuiColors.ParsedGold, $"[{item.Subcategory}]");
                
                if (!string.IsNullOrEmpty(item.Description))
                {
                    ImGui.Separator();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30f);
                    ImGui.TextUnformatted(item.Description);
                    ImGui.PopTextWrapPos();
                }
                
                ImGui.Separator();
                if (item.IsUnlocked)
                {
                    ImGui.TextColored(new Vector4(0.0f, 0.65f, 1.0f, 1.0f), "Unlocked");
                }
                else
                {
                    ImGui.TextColored(ImGuiColors.DalamudRed, "Locked");
                }
                
                if (item.Sources != null && item.Sources.Length > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, "How to acquire:");
                    foreach (var source in item.Sources)
                    {
                        ImGui.TextColored(ImGuiColors.DalamudWhite, "- " + source);
                    }
                }
                
                ImGui.EndTooltip();
            }

            ImGui.SetCursorPos(cursorPos + new Vector2(0, height + 8));
        }
    }
}
