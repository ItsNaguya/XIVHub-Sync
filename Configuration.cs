using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;

namespace XIVHubCompanion
{
    [Serializable]
    public class RouteItem
    {
        public uint ItemId { get; set; }
        public int TargetQuantity { get; set; }
        public bool IsCompleted { get; set; }
    }

    [Serializable]
    public class CraftingPipelineItem
    {
        public uint RecipeId { get; set; }
        public int Amount { get; set; }
        [NonSerialized] public uint ItemId;
    }

    [Serializable]
    public class CrafterProfile
    {
        public string Name { get; set; } = "Default Profile";
        public int Level { get; set; } = 100;
        public int Craftsmanship { get; set; } = 0;
        public int Control { get; set; } = 0;
        public int Cp { get; set; } = 0;
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        
        public bool IsSyncEnabled { get; set; } = true;
        
        public string XivHubId { get; set; } = string.Empty;
        public bool IsVerified { get; set; } = false;
        
        public int TabletSize { get; set; } = 2; // 0=XS, 1=S, 2=M, 3=L, 4=XL
        
        public bool EnableHoverItemFetching { get; set; } = true;
        
        public bool EnableNodeNotifications { get; set; } = false; // default off as per previous request
        public bool EnableNodeAudio { get; set; } = true;
        public int EarlyNodeNotificationMinutes { get; set; } = 0; // 0 means on-time

        public bool IsMinimized { get; set; } = false;

        public bool HideBackgroundAnimation { get; set; } = false;
        public bool HideScanline { get; set; } = false;
        public bool ShowMinimizedOverlay { get; set; } = true;
        public bool ShowMinimizedRetainerOverlay { get; set; } = true;
        public bool ShowMinimizedGatheringOverlay { get; set; } = true;
        
        public bool EnableGatheringPrices { get; set; } = true;
        public bool IsManualRouteOverride { get; set; } = false;
        public bool HideScrollbars { get; set; } = true;
        
        public bool OpenOnStartup { get; set; } = true;
        public bool StartMinimized { get; set; } = false;

        public bool RetainerAudioEnabled { get; set; } = true;
        public bool RetainerAudioFireOnce { get; set; } = true;

        public Dictionary<string, bool> RoutinesChecklist { get; set; } = new Dictionary<string, bool>();
        public List<string> RoutinesHiddenTasks { get; set; } = new List<string>();
        // Custom tasks are stored as JSON string from server
        public string RoutinesCustomTasksJson { get; set; } = "[]";
        public long LastDailyResetTime { get; set; } = 0;
        public long LastWeeklyResetTime { get; set; } = 0;

        public List<RouteItem> GatheringActiveRoute { get; set; } = new List<RouteItem>();
        public List<string> GatheringFavorites { get; set; } = new List<string>();

        public List<CraftingPipelineItem> CraftingDraftPipeline { get; set; } = new List<CraftingPipelineItem>();
        public List<CraftingPipelineItem> CraftingActivePipeline { get; set; } = new List<CraftingPipelineItem>();
        public List<CrafterProfile> CrafterProfiles { get; set; } = new List<CrafterProfile>();

        public int SolverInitialQuality { get; set; } = 0;
        public bool SolverRequireTargetQuality { get; set; } = false;
        public bool SolverManipulation { get; set; } = true;
        public bool SolverHeartAndSoul { get; set; } = false;
        public bool SolverQuickInnovation { get; set; } = false;
        public int SolverFoodIndex { get; set; } = 0;
        public int SolverPotionIndex { get; set; } = 0;
        public int SolverSelectedTarget { get; set; } = -1;

        [NonSerialized]
        private IDalamudPluginInterface pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.pluginInterface.SavePluginConfig(this);
        }
    }
}

