﻿using PlayerInfoLibrary;
using Rocket.API;
using Rocket.API.Collections;
using Rocket.API.Extensions;
using Rocket.Core.Commands;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using Logger = Rocket.Core.Logging.Logger;

namespace ApokPT.RocketPlugins
{
    public class WreckingBall : RocketPlugin<WreckingBallConfiguration>
    {
        // Singleton
        public static WreckingBall Instance;
        public static ElementDataManager ElementData;

        protected override void Load()
        {
            Instance = this;
            Instance.Configuration.Instance.LoadDefaults();
            ElementData = new ElementDataManager();
            if (Instance.Configuration.Instance.DestructionRate <= 0)
            {
                Instance.Configuration.Instance.DestructionRate = 1;
                Logger.LogWarning("Error: DestructionRate config value must be above 0.");
            }
            if (Instance.Configuration.Instance.DestructionsPerInterval < 1)
            {
                Instance.Configuration.Instance.DestructionsPerInterval = 1;
                Logger.LogWarning("Error: DestructionsPerInterval config value must be at or above 1.");
            }
            if (Instance.Configuration.Instance.EnablePlayerInfo || Instance.Configuration.Instance.EnableCleanup)
            {
                // Check to see whether the PlayerInfoLib plugin is present on this server.
                if (!CheckPlayerInfoLib())
                {
                    Logger.LogWarning("The Player Info Library plugin isn't loaded on this server, setting related options to false.");
                    Instance.Configuration.Instance.EnablePlayerInfo = false;
                    Instance.Configuration.Instance.EnableCleanup = false;
                }
                else
                {
                    CheckCleanup();
                }
            }
            Instance.Configuration.Save();
        }

        protected override void Unload()
        {
            if (DestructionProcessing.processing)
            {
                if (DestructionProcessing.originalCaller != null)
                    UnturnedChat.Say(DestructionProcessing.originalCaller, Translate("wreckingball_reload_abort"), Color.yellow);
                Logger.LogWarning(Translate("wreckingball_reload_abort"));
                DestructionProcessing.Abort(WreckType.Wreck);
            }
            if (DestructionProcessing.cleanupProcessingBuildables || DestructionProcessing.cleanupProcessingFiles)
            {
                DestructionProcessing.Abort(WreckType.Cleanup);
            }
            ElementData = null;
        }

        private static void CheckCleanup()
        {
            if (Instance.Configuration.Instance.EnableCleanup && DatabaseManager.DatabaseInterfaceVersion < 2)
            {
                Logger.LogWarning("The Player Info Library is outdated, the WreckingBall cleanup feature in this plugin will be disabled.");
                Instance.Configuration.Instance.EnableCleanup = false;
            }
            else if (Instance.Configuration.Instance.EnableCleanup)
            {
                if (Instance.Configuration.Instance.BuildableWaitTime < 1)
                {
                    Instance.Configuration.Instance.BuildableWaitTime = 30;
                }
                if (Instance.Configuration.Instance.PlayerDataWaitTime < 1)
                {
                    Instance.Configuration.Instance.PlayerDataWaitTime = 45;
                }
                if (Instance.Configuration.Instance.CleanupIntervalTime < 1)
                {
                    Instance.Configuration.Instance.CleanupIntervalTime = 5;
                }
                if (Instance.Configuration.Instance.CleanupPerInterval < 1)
                {
                    Instance.Configuration.Instance.CleanupPerInterval = 10;
                }
            }
        }

        private static bool CheckPlayerInfoLib()
        {
            try
            {
                return Type.GetType("PlayerInfoLibrary.DatabaseManager,PlayerInfoLib") != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsPInfoLibLoaded()
        {
            return (PlayerInfoLib.Instance.State == PluginState.Loaded && PlayerInfoLib.Database.Initialized);
        }

        [RocketCommand("wreck", "Destroy everything in a specific radius!", ".",AllowedCaller.Both)]
        [RocketCommandPermission("wreck")]
        public void WreckExecute(IRocketPlayer caller, string[] cmd)
        {
            WreckingBallCommand.Execute(caller, cmd);
        }

        [RocketCommand("w", "Destroy everything in a specific radius!", ".", AllowedCaller.Both)]
        [RocketCommandPermission("wreck")]
        public void WExecute(IRocketPlayer caller, string[] cmd)
        {
            WreckingBallCommand.Execute(caller, cmd);
        }

        [RocketCommand("listvehicles", "lists positions and barricade counts on cars on a map.", "<radius>", AllowedCaller.Both)]
        [RocketCommandPermission("listvehicles")]
        public void LVExecute(IRocketPlayer caller, string[] cmd)
        {
            float radius = 0;
            UnturnedPlayer player = null;
            if (!(caller is ConsolePlayer))
            {
                if (cmd.GetFloatParameter(0) == null)
                {
                    UnturnedChat.Say(caller, Translate("wreckingball_lv_help"));
                    return;
                }
                player = (UnturnedPlayer)caller;
                radius = (float)cmd.GetFloatParameter(0);
            }
            foreach (InteractableVehicle vehicle in VehicleManager.vehicles)
            {
                byte x = 0;
                byte y = 0;
                ushort plant = 0;
                int count = 0;
                BarricadeRegion barricadeRegion;
                if (caller is ConsolePlayer || Vector3.Distance(vehicle.transform.position, player.Position) < radius)
                {
                    bool getPInfo = false;
                    if (Instance.Configuration.Instance.EnablePlayerInfo)
                        getPInfo = IsPInfoLibLoaded();
                    string locked = getPInfo ? PInfoGenerateMessage((ulong)vehicle.lockedOwner) : vehicle.lockedOwner.ToString();
                    string msg = string.Empty;
                    if (vehicle)
                    if (BarricadeManager.tryGetPlant(vehicle.transform, out x, out y, out plant, out barricadeRegion))
                        count = barricadeRegion.drops.Count;
                    if (!vehicle.isLocked)
                        msg = Translate("wreckingball_lv_vehicle", vehicle.transform.position.ToString(), vehicle.instanceID, count);
                    else
                        msg = Translate("wreckingball_lv_vehicle_locked", vehicle.transform.position.ToString(), vehicle.instanceID, count, locked);
                    if (!(caller is ConsolePlayer))
                        UnturnedChat.Say(caller, msg, Color.yellow);
                    Logger.Log(msg, ConsoleColor.Yellow);
                }
            }
        }


        [RocketCommand("listtopplayers", "Gets the elements counts for players on the server, displays the top counts.", "", AllowedCaller.Both)]
        [RocketCommandPermission("listtopplayers")]
        public void LTPExecute(IRocketPlayer caller, string[] cmd)
        {
            // Get player elements list.
            DestructionProcessing.Wreck(caller, "", 0, Vector3.zero, WreckType.Counts, FlagType.SteamID, 0, 0);
            // Grab what we need from the list.
            Dictionary<ulong, int> shortenedList = DestructionProcessing.pElementCounts.Where(r => r.Value >= WreckingBall.Instance.Configuration.Instance.PlayerElementListCutoff).OrderBy(v => v.Value).ToDictionary(k => k.Key, v => v.Value);
            DestructionProcessing.pElementCounts.Clear();

            bool getPInfo = false;
            if (Instance.Configuration.Instance.EnablePlayerInfo)
                getPInfo = IsPInfoLibLoaded();

            foreach (KeyValuePair<ulong, int> value in shortenedList)
            {
                string msg = string.Format("Element count: {0}, Player: {1}", value.Value, !getPInfo || value.Key == 0 ? value.Key.ToString() : PInfoGenerateMessage(value.Key));
                if (caller is ConsolePlayer)
                    Logger.Log(msg, ConsoleColor.Yellow);
                else
                    UnturnedChat.Say(caller, msg, Color.yellow);
            }
        }

        public string PInfoGenerateMessage(ulong owner)
        {
            PlayerData pData = PlayerInfoLib.Database.QueryById((CSteamID)owner);
            string msg = string.Empty;
            if (pData.IsValid())
                msg = string.Format("{0} {1} [{2}], Seen: {3}:{4}", owner, pData.CharacterName, pData.SteamName, pData.IsLocal() ? "L" : "G", pData.IsLocal() ? pData.LastLoginLocal : pData.LastLoginGlobal);
            else
                msg = string.Format("{0}, No Player Info.", owner);
            return msg;
        }

        [RocketCommand("disablecleanup", "disables cleanup on a player", "<\"playername\" | SteamID>", AllowedCaller.Both)]
        [RocketCommandPermission("disablecleanup")]
        public void DCUExecute(IRocketPlayer caller, string[] cmd)
        {
            if (!Instance.Configuration.Instance.EnableCleanup)
            {
                UnturnedChat.Say(caller, Translate("werckingball_dcu_not_enabled"), Color.red);
                return;
            }
            else
            {
                DCUSet(caller, cmd);
            }
        }

        private void DCUSet(IRocketPlayer caller, string[] cmd)
        {
            if (cmd.Length == 0 || cmd.Length > 1)
            {
                UnturnedChat.Say(caller, Translate("werckingball_dcu_help"));
                return;
            }
            else
            {
                ulong steamID = 0;
                UnturnedPlayer player = null;
                if (!cmd[0].isCSteamID(out steamID))
                {
                    player = UnturnedPlayer.FromName(cmd[0]);
                    if (player == null)
                    {
                        UnturnedChat.Say(caller, Translate("wreckingball_dcu_player_not_found"), Color.red);
                        return;
                    }
                    steamID = (ulong)player.CSteamID;
                }
                if (IsPInfoLibLoaded())
                {
                    PlayerData pData = PlayerInfoLib.Database.QueryById((CSteamID)steamID, false);
                    if (!pData.IsLocal())
                    {
                        UnturnedChat.Say(caller, Translate("wreckingball_dcu_hasnt_played"), Color.red);
                        return;
                    }
                    if (pData.CleanedBuildables && pData.CleanedPlayerData)
                    {
                        PlayerInfoLib.Database.SetOption(pData.SteamID, OptionType.Buildables, false);
                        PlayerInfoLib.Database.SetOption(pData.SteamID, OptionType.PlayerFiles, false);
                        UnturnedChat.Say(caller, Translate("wreckingball_dcu_cleanup_enabled", pData.CharacterName, pData.SteamName, pData.SteamID));

                    }
                    else
                    {
                        PlayerInfoLib.Database.SetOption(pData.SteamID, OptionType.Buildables, true);
                        PlayerInfoLib.Database.SetOption(pData.SteamID, OptionType.PlayerFiles, true);
                        UnturnedChat.Say(caller, Translate("wreckingball_dcu_cleanup_disabled", pData.CharacterName, pData.SteamName, pData.SteamID));
                    }
                }
                else
                {
                    UnturnedChat.Say(caller, Translate("werckingball_dcu_not_enabled"), Color.red);
                }
            }
        }

        internal void Scan(IRocketPlayer caller, string filter, uint radius, Vector3 position, FlagType flagType, ulong steamID, ushort itemID)
        {
            DestructionProcessing.Wreck(caller, filter, radius, position, WreckType.Scan, flagType, steamID, itemID);
            if (ElementData.reportLists[BuildableType.Element].Count > 0 || ElementData.reportLists[BuildableType.VehicleElement].Count > 0)
            {
                foreach (KeyValuePair<BuildableType, Dictionary<char, uint>> reportDictionary in ElementData.reportLists)
                {
                    if (reportDictionary.Value.Count == 0)
                        continue;
                    string report = "";
                    uint totalCount = 0;
                    foreach (KeyValuePair<char, uint> reportFilter in reportDictionary.Value)
                    {
                        report += " " + ElementData.categorys[reportFilter.Key].Name + ": " + reportFilter.Value + ",";
                        totalCount += reportFilter.Value;
                    }
                    if (report != "") report = report.Remove(report.Length - 1);
                    string type = reportDictionary.Key == BuildableType.VehicleElement ? "Vehicle Element" : "Element";
                    UnturnedChat.Say(caller, Translate("wreckingball_scan", totalCount, type, radius, report));
                    if (Instance.Configuration.Instance.LogScans && !(caller is ConsolePlayer))
                        Logger.Log(Translate("wreckingball_scan", totalCount, type, radius, report));
                }
            }
            else
            {
                UnturnedChat.Say(caller, Translate("wreckingball_not_found", radius));
            }



        }

        internal void Teleport(IRocketPlayer caller, TeleportType teleportType)
        {

            if (StructureManager.regions.LongLength == 0 && BarricadeManager.BarricadeRegions.LongLength == 0)
            {
                UnturnedChat.Say(caller, Translate("wreckingball_map_clear"));
                return;
            }

            UnturnedPlayer player = (UnturnedPlayer)caller;

            Vector3 tpVector;
            bool match = false;
            int tries = 0;

            Transform current = null;

            while (tries < 2000 && !match)
            {
                tries++;
                int x = 0;
                int xCount = 0;
                int z = 0;
                int zCount = 0;
                int idx = 0;
                int idxCount = 0;
                switch (teleportType)
                {
                    case TeleportType.Structures:
                        xCount = StructureManager.regions.GetLength(0);
                        zCount = StructureManager.regions.GetLength(1);
                        if (xCount == 0)
                            continue;
                        x = UnityEngine.Random.Range(0, xCount - 1);
                        if (zCount == 0)
                            continue;
                        z = UnityEngine.Random.Range(0, zCount - 1);
                        idxCount = StructureManager.regions[x, z].structures.Count;
                        if (idxCount == 0)
                            continue;
                        idx = UnityEngine.Random.Range(0, idxCount - 1);

                        try
                        {
                            current = StructureManager.regions[x, z].models[idx];
                        }
                        catch
                        {
                            continue;
                        }

                        if (Vector3.Distance(current.position, player.Position) > 20)
                            match = true;
                        break;
                    case TeleportType.Barricades:
                        xCount = BarricadeManager.BarricadeRegions.GetLength(0);
                        zCount = BarricadeManager.BarricadeRegions.GetLength(1);
                        if (xCount == 0)
                            continue;
                        x = UnityEngine.Random.Range(0, xCount - 1);
                        if (zCount == 0)
                            continue;
                        z = UnityEngine.Random.Range(0, zCount - 1);
                        idxCount = BarricadeManager.BarricadeRegions[x, z].drops.Count;
                        if (idxCount == 0)
                            continue;
                        idx = UnityEngine.Random.Range(0, idxCount - 1);

                        try
                        {
                            current = BarricadeManager.BarricadeRegions[x, z].drops[idx].model;
                        }
                        catch
                        {
                            continue;
                        }

                        if (Vector3.Distance(current.position, player.Position) > 20)
                            match = true;
                        break;
                    case TeleportType.Vehicles:
                        int vCount = VehicleManager.vehicles.Count;
                        int vRand = UnityEngine.Random.Range(0, vCount - 1);
                        try
                        {
                            current = VehicleManager.vehicles[vRand].transform;
                        }
                        catch
                        {
                            continue;
                        }
                        if (Vector3.Distance(current.position, player.Position) > 20)
                            match = true;
                        break;
                    default:
                        return;
                }
            }
            if(match)
            {
                tpVector = new Vector3(current.position.x, teleportType == TeleportType.Vehicles ? current.position.y + 4 : current.position.y + 2, current.position.z);
                player.Teleport(tpVector, player.Rotation);
                return;
            }
            UnturnedChat.Say(caller, Translate("wreckingball_teleport_not_found"));
        }

        internal void Instruct(IRocketPlayer caller)
        {
            UnturnedChat.Say(caller, Translate("wreckingball_queued", DestructionProcessing.dIdxCount, DestructionProcessing.CalcProcessTime()));
            if (DestructionProcessing.syncError)
                UnturnedChat.Say(caller, Translate("wreckingball_sync_error"));
            UnturnedChat.Say(caller, Translate("wreckingball_prompt"));
        }

        internal void Confirm(IRocketPlayer caller)
        {
            if (DestructionProcessing.destroyList.Count <= 0)
            {
                UnturnedChat.Say(caller, Instance.Translate("wreckingball_help"));
            }
            else
            {
                DestructionProcessing.processing = true;
                if (!(caller is ConsolePlayer))
                    DestructionProcessing.originalCaller = (UnturnedPlayer)caller;
                UnturnedChat.Say(caller, Translate("wreckingball_initiated", DestructionProcessing.CalcProcessTime()));
                DestructionProcessing.dIdxCount = DestructionProcessing.destroyList.Count;
                DestructionProcessing.dIdx = 0;
            }
        }

        // Changed timer to Update(), to attempt to fix ghost objects bug by syncing the destructions to the game frame/tic.
        public void Update()
        {
            if (Instance.State == PluginState.Loaded)
            {
                if ((DateTime.Now - DestructionProcessing.lastRunTimeWreck).TotalSeconds > (1 / Instance.Configuration.Instance.DestructionRate))
                {
                    DestructionProcessing.lastRunTimeWreck = DateTime.Now;
                    if (DestructionProcessing.processing)
                        DestructionProcessing.DestructionLoop(WreckType.Wreck);
                    if (DestructionProcessing.cleanupProcessingBuildables)
                        DestructionProcessing.DestructionLoop(WreckType.Cleanup);
                }
                if (Instance.Configuration.Instance.EnableCleanup)
                    DestructionProcessing.HandleCleanup();
                if (Instance.Configuration.Instance.EnableVehicleCap)
                    DestructionProcessing.HandleVehicleCap();
            }
        }

        // Translations
        public override TranslationList DefaultTranslations
        {
            get
            {
                return new TranslationList
                {
                    { "wreckingball_lv_help", "<radius> - distance to scan cars." },
                    { "wreckingball_lv_vehicle", "Vehicle position: {0}, with InstanceID: {1}, Barricade count on car: {2}." },
                    { "wreckingball_lv_vehicle_locked", "Vehicle position: {0}, with InstanceID: {1}, Barricade count on car: {2}, Locked By: {3}." },
                    { "werckingball_dcu_help", "<\"playername\" | SteamID> - disables cleanup on a player." },
                    { "werckingball_dcu_not_enabled", "This command can only be used if the cleanup feature is enabled on the server." },
                    { "wreckingball_dcu_player_not_found", "Couldn't find a player by that name on the server." },
                    { "wreckingball_dcu_hasnt_played", "Player hasn't played on this server yet." },
                    { "wreckingball_dcu_cleanup_disabled", "Auto Cleanup has been disabled for player {0} [{1}] ({2})" },
                    { "wreckingball_dcu_cleanup_enabled", "Auto Cleanup has been enabled for player {0} [{1}] ({2})" },
                    { "wreckingball_scan", "Found {0} elements of type: {1}, @ {2}m:{3}" },
                    { "wreckingball_map_clear", "Map has no elements!" },
                    { "wreckingball_not_found", "No elements found in a {0} radius!" },
                    { "wreckingball_complete", "Wrecking Ball complete! {0} elements(s) Destroyed!" },
                    { "wreckingball_initiated", "Wrecking Ball initiated: ~{0} sec(s) left." },
                    { "wreckingball_processing", "Wrecking Ball started by: {0}, {1} element(s) left to destroy, ~{2} sec(s) left." },
                    { "wreckingball_aborted", "Wrecking Ball Aborted! Destruction queue cleared!" },
                    { "wreckingball_help", "Please define filter and radius: /wreck <filter> <radius> or /wreck teleport b|s" },
                    { "wreckingball_help_console", "Please define filter, radius and position: /wreck <filter> <radius> <x> <y> <z>" },
                    { "wreckingball_help_teleport", "Please define type for teleport: /wreck teleport s|b|v" },
                    { "wreckingball_help_scan", "Please define a scan filter and radius: /wreck scan <filter> <radius>" },
                    { "wreckingball_help_scan_console", "Please define a scan filter, radius and position: /wreck scan <filter> <radius> <x> <y> <z>" },
                    { "wreckingball_queued", "{0} elements(s) found, ~{1} sec(s) to complete run." },
                    { "wreckingball_prompt", "Type '/wreck confirm' or '/wreck abort'" },
                    { "wreckingball_structure_array_sync_error", "Warning: Structure arrays out of sync, need to restart server." },
                    { "wreckingball_barricade_array_sync_error", "Warning: Barricade arrays out of sync, need to restart server." },
                    { "wreckingball_sync_error", "Warning: Element array sync error, not all elements will be cleaned up in range, server should be restarted." },
                    { "wreckingball_teleport_not_found", "Couldn't find any elements to teleport to, try to run the command again." },
                    { "wreckingball_teleport_not_allowed", "Not allowed to use wreck teleport from the console." },
                    { "wreckingball_reload_abort", "Warning: Current wreck job in progress has been aborted from a plugin reload." },
                    { "wreckingball_wreck_permission", "You need to have the permissions wreck.wreck, or wreck.* to be able to run a wreck." },
                    { "wreckingball_scan_permission", "You need to have the permissions wreck.scan, or wreck.* to be able to run a scan." },
                    { "wreckingball_teleport_permission", "You need to have the permissions wreck.teleport, or wreck.* to be able to run a teleport." },
                };
            }
        }
    }
}
