using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Command;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Collections.Generic;

namespace XIVHubCompanion
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "XIV Hub Companion";
        private const string CommandName = "/xivhub";

        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly ICommandManager _commandManager;
        private readonly IObjectTable _objectTable;
        private readonly IFramework _framework;
        private readonly IDataManager _dataManager;
        private readonly IPluginLog _log;
        
        private readonly DataSender _sender;
        private readonly Configuration _configuration;
        private readonly PluginUI _ui;
        
        // State tracking
        private uint _lastJobId = 0;
        private int _lastLevel = 0;
        private DateTime _lastSync = DateTime.MinValue;

        public Plugin(
            IDalamudPluginInterface pluginInterface,
            ICommandManager commandManager,
            IObjectTable objectTable,
            IFramework framework,
            IDataManager dataManager,
            IPluginLog log)
        {
            _pluginInterface = pluginInterface;
            _commandManager = commandManager;
            _objectTable = objectTable;
            _framework = framework;
            _dataManager = dataManager;
            _log = log;

            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            _configuration.Initialize(_pluginInterface);
            
            _sender = new DataSender(_log);
            _ui = new PluginUI(_configuration, _sender);

            _commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open the XIV Hub Companion settings."
            });

            _pluginInterface.UiBuilder.Draw += DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            _pluginInterface.UiBuilder.OpenMainUi += DrawConfigUI;

            _framework.Update += OnFrameworkUpdate;
            _log.Info("XIV Hub Companion initialized.");
        }

        public void Dispose()
        {
            _framework.Update -= OnFrameworkUpdate;
            _pluginInterface.UiBuilder.Draw -= DrawUI;
            _pluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
            _commandManager.RemoveHandler(CommandName);
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

        private unsafe void SyncData(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
        {
            try
            {
                var gearList = new List<object>();
                var invManager = InventoryManager.Instance();
                if (invManager != null)
                {
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
                                                // Assuming Item array holds references
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
                        s["crit"] = attrs[25];
                        s["attackMagicPotency"] = attrs[26];
                        s["healingMagicPotency"] = attrs[27];
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
                        s["ten"] = attrs[19];
                        s["dh"] = attrs[22];
                        s["crit"] = attrs[27];
                        s["det"] = attrs[44];
                        s["sks"] = attrs[45];
                        s["sps"] = attrs[46];
                        s["craft"] = attrs[70];
                        s["control"] = attrs[71];
                        s["gather"] = attrs[72];
                        s["perc"] = attrs[73];
                    }
                } catch { }

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
                    stats = s
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
