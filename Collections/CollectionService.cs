using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace XIVHubCompanion.Collections
{
    public class CollectionService
    {
        private readonly IDataManager _dataManager;
        private readonly IUnlockState _unlockState;
        
        private readonly Dictionary<int, string> _unlockableEmotes = new();
        
        private Dictionary<CollectionCategory, List<CollectionItem>> _cache = new Dictionary<CollectionCategory, List<CollectionItem>>();
        private DateTime _lastFetch = DateTime.MinValue;

        public CollectionService(IDataManager dataManager, IUnlockState unlockState)
        {
            _dataManager = dataManager;
            _unlockState = unlockState;
            
            var validEmoteUnlocks = new HashSet<uint>();
            foreach (var emote in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>())
            {
                if (emote.UnlockLink != 0) validEmoteUnlocks.Add(emote.UnlockLink);
            }

            // Build cross-references for Items that unlock Collections
            foreach (var item in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>())
            {
                var name = item.Name.ToString();
                var desc = item.Description.ToString();
                if (string.IsNullOrEmpty(name) || item.ItemAction.ValueNullable == null) continue;

                var unlockId = (int)item.ItemAction.Value.Data[0];
                if (validEmoteUnlocks.Contains((uint)unlockId))
                {
                    _unlockableEmotes[unlockId] = desc;
                }
            }
        }

        public List<CollectionItem> GetItems(CollectionCategory category)
        {
            if ((DateTime.Now - _lastFetch).TotalSeconds > 10)
            {
                RefreshCache();
            }
            
            return _cache.ContainsKey(category) ? _cache[category] : new List<CollectionItem>();
        }

        public void RefreshCache()
        {
            _cache[CollectionCategory.Achievements] = GetAchievements();
            _cache[CollectionCategory.Mounts] = GetMounts();
            _cache[CollectionCategory.Minions] = GetMinions();
            _cache[CollectionCategory.Emotes] = GetEmotes();
            _cache[CollectionCategory.Orchestrions] = GetOrchestrions();
            _cache[CollectionCategory.Hairstyles] = GetHairstyles();
            _cache[CollectionCategory.Facewear] = GetFacewear();
            _cache[CollectionCategory.TriadCards] = GetTriadCards();
            _lastFetch = DateTime.Now;
        }

        private unsafe List<CollectionItem> GetAchievements()
        {
            var list = new List<CollectionItem>();
            var achStruct = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement.Instance();

            foreach (var row in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Achievement>())
            {
                if (string.IsNullOrEmpty(row.Name.ToString()) || row.Icon == 0) continue;
                
                bool unlocked = false;
                if (achStruct != null)
                {
                    unlocked = achStruct->IsComplete((int)row.RowId);
                }

                string cat = row.AchievementCategory.Value.Name.ToString();
                if (string.IsNullOrEmpty(cat)) cat = "General";

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Name.ToString(),
                    Description = row.Description.ToString(),
                    IconId = (uint)row.Icon,
                    IsUnlocked = unlocked,
                    Category = CollectionCategory.Achievements,
                    Subcategory = cat
                });
            }
            return list;
        }

        private string GetExpansionFromId(uint id, uint hw, uint sb, uint shb, uint ew, uint dt)
        {
            if (id >= dt) return "Dawntrail";
            if (id >= ew) return "Endwalker";
            if (id >= shb) return "Shadowbringers";
            if (id >= sb) return "Stormblood";
            if (id >= hw) return "Heavensward";
            return "A Realm Reborn";
        }

        private List<CollectionItem> GetMounts()
        {
            var list = new List<CollectionItem>();
            var transients = _dataManager.GetExcelSheet<MountTransient>();

            foreach (var row in _dataManager.GetExcelSheet<Mount>())
            {
                if (string.IsNullOrEmpty(row.Singular.ToString()) || row.Order == -1) continue;
                
                string desc = "";
                if (transients.HasRow(row.RowId))
                {
                    desc = transients.GetRow(row.RowId).Description.ToString();
                }

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Singular.ToString(),
                    Description = desc,
                    IconId = (uint)row.Icon,
                    IsUnlocked = _unlockState.IsMountUnlocked(row),
                    Category = CollectionCategory.Mounts,
                    Subcategory = GetExpansionFromId(row.RowId, 70, 140, 190, 250, 325)
                });
            }
            return list;
        }

        private List<CollectionItem> GetMinions()
        {
            var list = new List<CollectionItem>();
            var transients = _dataManager.GetExcelSheet<CompanionTransient>();

            foreach (var row in _dataManager.GetExcelSheet<Companion>())
            {
                if (string.IsNullOrEmpty(row.Singular.ToString())) continue;
                
                string desc = "";
                if (transients.HasRow(row.RowId))
                {
                    desc = transients.GetRow(row.RowId).Description.ToString();
                }

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Singular.ToString(),
                    Description = desc,
                    IconId = (uint)row.Icon,
                    IsUnlocked = _unlockState.IsCompanionUnlocked(row),
                    Category = CollectionCategory.Minions,
                    Subcategory = GetExpansionFromId(row.RowId, 130, 260, 350, 420, 490)
                });
            }
            return list;
        }

        private List<CollectionItem> GetEmotes()
        {
            var list = new List<CollectionItem>();
            foreach (var row in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Emote>())
            {
                if (row.UnlockLink == 0) continue;
                
                _unlockableEmotes.TryGetValue((int)row.UnlockLink, out var desc);

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Name.ToString(),
                    Description = desc ?? "",
                    IconId = (uint)row.Icon,
                    IsUnlocked = _unlockState.IsEmoteUnlocked(row),
                    Category = CollectionCategory.Emotes,
                    Subcategory = "Emote"
                });
            }
            return list;
        }

        private List<CollectionItem> GetOrchestrions()
        {
            var list = new List<CollectionItem>();
            foreach (var row in _dataManager.GetExcelSheet<Orchestrion>())
            {
                if (string.IsNullOrEmpty(row.Name.ToString()) || row.Name.ToString() == "0") continue;
                
                string cat = GetExpansionFromId(row.RowId, 100, 200, 300, 400, 500); // Generic ranges for Orchestrion

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Name.ToString(),
                    Description = row.Description.ToString(),
                    IconId = 26004, // Default orchestrion roll icon
                    IsUnlocked = _unlockState.IsOrchestrionUnlocked(row),
                    Category = CollectionCategory.Orchestrions,
                    Subcategory = cat
                });
            }
            return list;
        }

        private unsafe List<CollectionItem> GetHairstyles()
        {
            var list = new List<CollectionItem>();
            
            foreach (var entry in HairstyleCatalog.Entries)
            {
                list.Add(new CollectionItem
                {
                    Id = (int)entry.UnlockLink,
                    Name = entry.Name,
                    Description = entry.Description,
                    IconId = entry.IconId,
                    IsUnlocked = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance()->IsUnlockLinkUnlockedOrQuestCompleted(entry.UnlockLink),
                    Category = CollectionCategory.Hairstyles,
                    Subcategory = "Hairstyle",
                    Sources = entry.Sources
                });
            }

            return list;
        }

        private List<CollectionItem> GetFacewear()
        {
            var list = new List<CollectionItem>();
            foreach (var row in _dataManager.GetExcelSheet<Glasses>())
            {
                if (row.Icon == 0 || !row.Style.IsValid || row.Name.ToString() != row.Style.Value.Name.ToString()) continue;
                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Name.ToString(),
                    Description = "",
                    IconId = (uint)row.Icon,
                    IsUnlocked = _unlockState.IsGlassesUnlocked(row),
                    Category = CollectionCategory.Facewear,
                    Subcategory = "Facewear"
                });
            }
            return list;
        }

        private List<CollectionItem> GetTriadCards()
        {
            var list = new List<CollectionItem>();
            foreach (var row in _dataManager.GetExcelSheet<TripleTriadCard>())
            {
                if (string.IsNullOrEmpty(row.Name.ToString()) || row.Name.ToString() == "0") continue;
                
                string rarity = "Card";

                list.Add(new CollectionItem
                {
                    Id = (int)row.RowId,
                    Name = row.Name.ToString(),
                    Description = row.Description.ToString(),
                    IconId = (uint)(88000 + row.RowId), // Card icons offset is 88000
                    IsUnlocked = _unlockState.IsTripleTriadCardUnlocked(row),
                    Category = CollectionCategory.TriadCards,
                    Subcategory = rarity
                });
            }
            return list;
        }
    }
}
