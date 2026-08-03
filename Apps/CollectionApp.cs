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

            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 16f);

            foreach (CollectionCategory category in Enum.GetValues(typeof(CollectionCategory)))
            {
                var items = _collectionService.GetItems(category);
                int total = items.Count;
                if (total == 0) continue; 
                
                int unlocked = items.Count(x => x.IsUnlocked);
                
                bool isCategorySelected = _selectedCategory == category && _selectedSubcategory == "All";
                var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
                if (isCategorySelected) flags |= ImGuiTreeNodeFlags.Selected;
                
                if (category == CollectionCategory.Mounts && _selectedSubcategory == "All") 
                {
                    ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
                }

                if (isCategorySelected) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
                bool isOpen = ImGui.TreeNodeEx($"{category} ({unlocked}/{total})##main{category}", flags);
                if (isCategorySelected) ImGui.PopStyleColor();

                if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
                {
                    _selectedCategory = category;
                    _selectedSubcategory = "All";
                }

                if (isOpen)
                {
                    var subcategories = items.Select(i => i.Subcategory).Distinct().OrderBy(s => s).ToList();
                    
                    foreach (var sub in subcategories)
                    {
                        if (string.IsNullOrEmpty(sub)) continue;

                        int subTotal = items.Count(x => x.Subcategory == sub);
                        int subUnlocked = items.Count(x => x.Subcategory == sub && x.IsUnlocked);

                        bool isSubSelected = _selectedCategory == category && _selectedSubcategory == sub;
                        
                        var subFlags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
                        if (isSubSelected) subFlags |= ImGuiTreeNodeFlags.Selected;

                        if (isSubSelected) ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.ParsedGold);
                        ImGui.TreeNodeEx($"{sub} ({subUnlocked}/{subTotal})##{category}_{sub}", subFlags);
                        if (ImGui.IsItemClicked())
                        {
                            _selectedCategory = category;
                            _selectedSubcategory = sub;
                        }
                        if (isSubSelected) ImGui.PopStyleColor();
                    }
                    ImGui.TreePop();
                }
            }

            ImGui.PopStyleVar();
        }

        private void DrawContent()
        {
            var items = _collectionService.GetItems(_selectedCategory);

            // Filtering
            ImGui.SetNextItemWidth(250f);
            ImGui.InputTextWithHint("##search", "Search ", ref _searchQuery, 100);
            ImGui.SameLine();
            ImGui.Checkbox("Owned Only", ref _showOnlyUnlocked);

            var filtered = items.Where(i => 
                (_selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory) &&
                (string.IsNullOrEmpty(_searchQuery) || i.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)) &&
                (!_showOnlyUnlocked || i.IsUnlocked)
            ).ToList();

            int total = items.Count(i => _selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory);
            int unlocked = items.Count(i => (_selectedSubcategory == "All" || i.Subcategory == _selectedSubcategory) && i.IsUnlocked);
            
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - 250f);
            ImGui.TextColored(ImGuiColors.ParsedGold, $"Progress: {unlocked} / {total} ({((total > 0 ? (float)unlocked/total*100f : 0)):F1}%)");

            ImGui.ProgressBar(total > 0 ? (float)unlocked/total : 0, new Vector2(-1, 4), "");

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
            float cardWidth = 110f;
            float cardHeight = 145f;
            float padding = 12f;
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
            var bgColor = item.IsUnlocked ? new Vector4(0.12f, 0.18f, 0.12f, 1f) : new Vector4(0.08f, 0.08f, 0.08f, 1f);
            var borderColor = item.IsUnlocked ? new Vector4(0.4f, 0.7f, 0.4f, 0.8f) : new Vector4(0.2f, 0.2f, 0.2f, 0.5f);
            
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
                
                ImGui.TextColored(item.IsUnlocked ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudGrey, item.Name);
                
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
                    ImGui.TextColored(ImGuiColors.HealerGreen, "Unlocked");
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
