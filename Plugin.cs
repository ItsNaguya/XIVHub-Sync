using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Collections.Generic;
using Lumina.Excel.Sheets;
using Dalamud.Game.Gui.ContextMenu;

namespace XIVHubCompanion
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "NAGU PAD (XIV HUB COMPANION)";
        private const string CommandName = "/xivhub";

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly ICommandManager _commandManager;
        private readonly IObjectTable _objectTable;
        private readonly IFramework _framework;
        private readonly IDataManager _dataManager;
        private readonly IContextMenu _contextMenu;
        private readonly IClientState _clientState;
        private readonly IPluginLog _log;
        
        private readonly DataSender _sender;
        private readonly Configuration _configuration;
        private readonly PluginUI _ui;
        private readonly Collections.CollectionService _collectionService;
        
        // State tracking
        private uint _lastJobId = 0;
        private int _lastLevel = 0;
        private DateTime _lastSync = DateTime.MinValue;

        private static readonly InventoryType[] BagTypes = { InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4 };
        private static readonly InventoryType[] SaddlebagTypes = { InventoryType.SaddleBag1, InventoryType.SaddleBag2, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2 };
        private static readonly InventoryType[] ArmouryTypes = { InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands, InventoryType.ArmoryWaist, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal };
        private static readonly InventoryType[] RetainerBagTypes = { InventoryType.RetainerPage1, InventoryType.RetainerPage2, InventoryType.RetainerPage3, InventoryType.RetainerPage4, InventoryType.RetainerPage5, InventoryType.RetainerPage6, InventoryType.RetainerPage7, InventoryType.RetainerCrystals };

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager,
            IObjectTable objectTable,
            IFramework framework,
            IDataManager dataManager,
            IGameGui gameGui,
            IChatGui chatGui,
            IContextMenu contextMenu,
            IAddonLifecycle addonLifecycle,
            ITextureProvider textureProvider,
            ISigScanner sigScanner,
            IClientState clientState,
            IPluginLog log,
            ICondition condition,
            IUnlockState unlockState)
        {
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _objectTable = objectTable;
            _framework = framework;
            _dataManager = dataManager;
            _contextMenu = contextMenu;
            _clientState = clientState;
            _log = log;

            MemoryOffsets.Initialize(sigScanner, log);

            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);
            
            _collectionService = new Collections.CollectionService(_dataManager, unlockState);

            _sender = new DataSender(_log, _configuration);
            _ui = new PluginUI(_configuration, _sender, gameGui, chatGui, addonLifecycle, textureProvider, _pluginInterface, _log, objectTable, dataManager, _clientState, _commandManager, condition, unlockState);
            _sender.StartStreaming();

            _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the NAGU-PAD window."
            });

            _pluginInterface.UiBuilder.Draw += DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            _pluginInterface.UiBuilder.OpenMainUi += DrawConfigUI;

            _framework.Update += OnFrameworkUpdate;
            _contextMenu.OnMenuOpened += OnContextMenuOpened;
            _clientState.Login += OnLogin;

            if (_clientState.IsLoggedIn && _configuration.OpenOnStartup)
            {
                _configuration.IsMinimized = _configuration.StartMinimized;
                _ui.SettingsVisible = true;
            }

            _log.Info("XIV Hub Companion initialized.");
        }

        private void OnLogin()
        {
            if (_configuration.OpenOnStartup)
            {
                _configuration.IsMinimized = _configuration.StartMinimized;
                _ui.SettingsVisible = true;
            }
        }

        private void OnContextMenuOpened(IMenuOpenedArgs args)
        {
            if (args.Target is MenuTargetInventory inventoryTarget && inventoryTarget.TargetItem.HasValue)
            {
                var itemId = inventoryTarget.TargetItem.Value.ItemId;
                if (itemId > 0)
                {
                    var trueItemId = (int)(itemId % 500000);
                    
                    var searchMenuItem = new MenuItem
                    {
                        Name = "Search NAGU PAD",
                        PrefixChar = 'N', // Just a placeholder char if we want an icon later
                        OnClicked = (i) =>
                        {
                            var row = _dataManager.GetExcelSheet<Item>()?.GetRow((uint)trueItemId);
                            if (row.HasValue)
                            {
                                bool canBeHq = row.Value.CanBeHq;
                                _ui.OpenMarketAppWithItem(trueItemId, row.Value.Name.ToString(), row.Value.Icon.ToString(), canBeHq);
                            }
                        }
                    };
                    args.AddMenuItem(searchMenuItem);
                }
            }
        }

        public void Dispose()
        {
            _framework.Update -= OnFrameworkUpdate;
            _contextMenu.OnMenuOpened -= OnContextMenuOpened;
            _clientState.Login -= OnLogin;
            _pluginInterface.UiBuilder.Draw -= DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            _pluginInterface.UiBuilder.OpenMainUi -= DrawConfigUI;
            _commandManager.RemoveHandler(CommandName);
            _sender.StopStreaming();
            _ui.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            _ui.SettingsVisible = true;
        }

        private void DrawUI()
        {
            _ui.Draw();
        }

        private void DrawConfigUI()
        {
            _ui.SettingsVisible = true;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!_configuration.IsSyncEnabled) return;

            var localPlayer = _objectTable[0] as Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter;
            if (localPlayer == null) return;
            if (!localPlayer.ClassJob.IsValid) return;

            var currentJobId = localPlayer.ClassJob.RowId;
            var currentLevel = localPlayer.Level;

            // Throttle to 1 sync per 2 seconds, and only if state changed (or every 10s as a heartbeat)
            var now = DateTime.Now;
            bool jobChanged = currentJobId != _lastJobId || currentLevel != _lastLevel;
            bool heartbeat = (now - _lastSync).TotalSeconds > 10;

            if ((jobChanged || heartbeat) && (now - _lastSync).TotalSeconds > 2)
            {
                _lastJobId = currentJobId;
                _lastLevel = currentLevel;
                _lastSync = now;

                SyncData(localPlayer);
            }
        }

        private unsafe List<object> GetContainerItems(InventoryType type, int bagIndex = -1)
        {
            var list = new List<object>();
            var manager = InventoryManager.Instance();
            if (manager == null) return list;

            var container = manager->GetInventoryContainer(type);
            if (container == null) return list;

            var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            for (int i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && slot->ItemId != 0 && slot->Quantity > 0)
                {
                    var itemRow = itemSheet.GetRow(slot->ItemId);
                    bool isHq = (slot->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                    list.Add(new {
                        slot = i,
                        containerId = bagIndex >= 0 ? (uint)bagIndex : (uint)type,
                        itemId = slot->ItemId,
                        quantity = slot->Quantity,
                        hq = isHq,
                        iconId = itemRow.Icon,
                        name = itemRow.Name.ToString(),
                        category = itemRow.ItemUICategory.RowId,
                        uiCategorySort = itemRow.ItemSortCategory.RowId,
                        uiCategoryOrderMajor = itemRow.ItemUICategory.Value.OrderMajor,
                        uiCategoryOrderMinor = itemRow.ItemUICategory.Value.OrderMinor,
                        ilvl = itemRow.LevelItem.RowId
                    });
                }
            }
            return list;
        }

        private unsafe List<object> GetMultipleContainers(InventoryType[] types, bool normalizeBagIndex = false)
        {
            var list = new List<object>();
            for (int i = 0; i < types.Length; i++) {
                list.AddRange(GetContainerItems(types[i], normalizeBagIndex ? i : -1));
            }
            return list;
        }

        private unsafe object GetActiveRetainerInventory()
        {
            var retainerManager = RetainerManager.Instance();
            if (retainerManager == null || !retainerManager->IsReady) return null;

            var active = retainerManager->GetActiveRetainer();
            if (active == null || active->RetainerId == 0) return null;

            var manager = InventoryManager.Instance();
            if (manager == null) return null;

            // We don't check IsLoaded since it might be unreliable. FFXIV will naturally zero out ItemIds if unloaded.
            var inventoryItems = GetMultipleContainers(RetainerBagTypes, true);
            if (inventoryItems.Count == 0) return null;

            return new {
                retainerId = active->RetainerId.ToString(),
                name = active->NameString,
                inventory = inventoryItems
            };
        }

        private unsafe List<uint> GetCaughtFishes()
        {
            var caught = new List<uint>();
            var ps = PlayerState.Instance();
            if (ps == null) return caught;
            
            var fishSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.FishParameter>();
            if (fishSheet != null && ps->CaughtFishBitArray.Pointer != null)
            {
                var ptr = ps->CaughtFishBitArray.Pointer;
                foreach (var fish in fishSheet)
                {
                    if (fish.Item.RowId == 0) continue;
                    uint id = fish.RowId;
                    var offset = id / 8;
                    var bit = (byte)(id % 8);
                    if (((ptr[offset] >> bit) & 1) == 1)
                    {
                        caught.Add(fish.Item.RowId);
                    }
                }
            }

            var spearSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.SpearfishingItem>();
            if (spearSheet != null && ps->CaughtSpearfishBitArray.Pointer != null)
            {
                var ptr = ps->CaughtSpearfishBitArray.Pointer;
                foreach (var fish in spearSheet)
                {
                    if (fish.Item.RowId == 0) continue;
                    uint id = fish.RowId;
                    var offset = id / 8;
                    var bit = (byte)(id % 8);
                    if (((ptr[offset] >> bit) & 1) == 1)
                    {
                        caught.Add(fish.Item.RowId);
                    }
                }
            }
            
            return caught;
        }

        private unsafe void SyncData(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
        {
            try
            {
                var gearList = new List<object>();
                var invManager = InventoryManager.Instance();
                long gil = 0;

                if (invManager != null)
                {
                    gil = invManager->GetGil();
                    var container = invManager->GetInventoryContainer(InventoryType.EquippedItems);
                    if (container != null)
                    {
                        var itemSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        var matSheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Materia>();
                        for (int i = 0; i < 14; i++)
                        {
                            var slot = container->GetInventorySlot(i);
                            if (slot != null && slot->ItemId != 0)
                            {
                                var itemRow = itemSheet.GetRow(slot->ItemId);
                                if (itemRow.RowId > 0)
                                {
                                    var materiaArr = new List<string>();
                                    for (int m = 0; m < 5; m++)
                                    {
                                        ushort mId = slot->Materia[m];
                                        byte grade = slot->MateriaGrades[m];
                                        if (mId != 0)
                                        {
                                            var mRow = matSheet.GetRow(mId);
                                            try {
                                                var mItemId = mRow.Item[grade].RowId;
                                                var mItemRow = itemSheet.GetRow(mItemId);
                                                var mValue = mRow.Value[grade];
                                                materiaArr.Add($"{mItemRow.Name.ToString()}:{mValue}");
                                            } catch {
                                                materiaArr.Add($"Unknown Materia {mId}");
                                            }
                                        }
                                    }

                                    int maxSlots = itemRow.MateriaSlotCount;
                                    if (itemRow.IsAdvancedMeldingPermitted) {
                                        maxSlots = 5;
                                    }

                                    gearList.Add(new
                                    {
                                        slot = i,
                                        itemId = slot->ItemId,
                                        name = itemRow.Name.ToString(),
                                        iconId = itemRow.Icon,
                                        ilvl = itemRow.LevelItem.RowId,
                                        materia = materiaArr,
                                        maxMateria = maxSlots
                                    });
                                }
                            }
                        }
                    }
                }

                // Extract Stats
                var s = new Dictionary<string, int>();
                try {
                    var uiState = UIState.Instance();
                    if (uiState != null) {
                        var attrs = uiState->PlayerState.Attributes;
                        s["str"] = attrs[1];
                        s["dex"] = attrs[2];
                        s["vit"] = attrs[3];
                        s["int"] = attrs[4];
                        s["mnd"] = attrs[5];
                        s["piety"] = attrs[6];
                        s["tenacity"] = attrs[19];
                        s["attackPower"] = attrs[20];
                        s["def"] = attrs[21];
                        s["dh"] = attrs[22];
                        s["mdef"] = attrs[24];
                        s["crit"] = attrs[27];
                        s["attackMagicPotency"] = attrs[33];
                        s["healingMagicPotency"] = attrs[34];
                        s["det"] = attrs[44];
                        s["skillSpeed"] = attrs[45];
                        s["spellSpeed"] = attrs[46];
                        s["craft"] = attrs[70];
                        s["control"] = attrs[71];
                        s["gather"] = attrs[72];
                        s["perc"] = attrs[73];
                        s["pie"] = attrs[6];
                        s["hp"] = attrs[7];
                        s["mp"] = attrs[8];
                        s["gp"] = attrs[10];
                        s["cp"] = attrs[11];
                    }
                } catch { }

                // Fetch Collections
                var mounts = _collectionService?.GetItems(Collections.CollectionCategory.Mounts).Where(x => x.IsUnlocked).Select(x => x.Name).ToList() ?? new List<string>();
                var minions = _collectionService?.GetItems(Collections.CollectionCategory.Minions).Where(x => x.IsUnlocked).Select(x => x.Name).ToList() ?? new List<string>();
                var achievements = _collectionService?.GetItems(Collections.CollectionCategory.Achievements).Where(x => x.IsUnlocked).Select(x => x.Name).ToList() ?? new List<string>();
                var caughtFishes = GetCaughtFishes();

                // Basic data
                var data = new
                {
                    name = player.Name.TextValue,
                    world = player.CurrentWorld.IsValid ? player.CurrentWorld.Value.Name.ToString() : "",
                    jobId = player.ClassJob.RowId,
                    level = player.Level,
                    hp = player.CurrentHp,
                    maxHp = player.MaxHp,
                    mp = player.CurrentMp,
                    maxMp = player.MaxMp,
                    gear = gearList,
                    stats = s,
                    gil = gil,
                    inventory = GetMultipleContainers(BagTypes, true),
                    crystals = GetContainerItems(InventoryType.Crystals),
                    saddlebag = GetMultipleContainers(SaddlebagTypes, true),
                    armoury = GetMultipleContainers(ArmouryTypes, false),
                    activeRetainer = GetActiveRetainerInventory(),
                    collections = new {
                        mounts = mounts,
                        minions = minions,
                        achievements = achievements,
                        caughtFishes = caughtFishes,
                        isPrivate = false,
                        lastSynced = DateTime.UtcNow.ToString("o")
                    }
                };

                _sender.SendDataAsync(data);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to build sync data.");
            }
        }
    }
}


