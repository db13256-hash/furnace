using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FurnaceSpeed", "OxideMod Community", "1.2.0")]
    [Description("Increases the smelting/cooking speed of furnaces, oil refineries, campfires, BBQs, and other cooking containers.")]
    public class FurnaceSpeed : RustPlugin
    {
        #region Constants

        // Default cook interval used by BaseOven (seconds per cook tick)
        private const float DefaultCookInterval = 0.5f;

        // Permission required to run admin chat commands
        private const string AdminPermission = "furnacespeed.admin";

        // Exact short prefab names used to identify each oven type.
        // Matching is done with string.Equals (case-insensitive exact match), so "furnace"
        // will never accidentally match "furnace.large".
        private static readonly Dictionary<OvenType, string[]> OvenPrefabFragments = new Dictionary<OvenType, string[]>
        {
            { OvenType.SmallFurnace,     new[] { "furnace" } },           // exact: "furnace"
            { OvenType.LargeFurnace,     new[] { "furnace.large" } },     // exact: "furnace.large"
            { OvenType.OilRefinery,      new[] { "refinery_small_deployed" } },
            { OvenType.Campfire,         new[] { "campfire" } },
            { OvenType.BBQ,              new[] { "bbq.deployed" } },
            { OvenType.ElectricFurnace,  new[] { "electricfurnace.deployed" } },
            { OvenType.MixingTable,      new[] { "mixingtable.deployed" } },
        };

        #endregion

        #region Enums

        private enum OvenType
        {
            SmallFurnace,
            LargeFurnace,
            OilRefinery,
            Campfire,
            BBQ,
            ElectricFurnace,
            MixingTable,
        }

        #endregion

        #region Configuration

        private PluginConfig _config;

        private class OvenSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Speed multiplier")]
            public float SpeedMultiplier = 3.0f;
        }

        private class PluginConfig
        {
            [JsonProperty("Small furnace")]
            public OvenSettings SmallFurnace = new OvenSettings();

            [JsonProperty("Large furnace")]
            public OvenSettings LargeFurnace = new OvenSettings();

            [JsonProperty("Oil refinery")]
            public OvenSettings OilRefinery = new OvenSettings();

            [JsonProperty("Campfire")]
            public OvenSettings Campfire = new OvenSettings();

            [JsonProperty("BBQ")]
            public OvenSettings BBQ = new OvenSettings();

            [JsonProperty("Electric furnace")]
            public OvenSettings ElectricFurnace = new OvenSettings();

            [JsonProperty("Mixing table")]
            public OvenSettings MixingTable = new OvenSettings();
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null)
                    throw new Exception("Null config");
            }
            catch
            {
                PrintWarning("Config is invalid or missing — loading defaults.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(AdminPermission, this);
        }

        private void OnServerInitialized()
        {
            // Apply speed boost to all ovens already running when the plugin loads.
            foreach (var oven in UnityEngine.Object.FindObjectsOfType<BaseOven>())
            {
                if (oven == null || oven.IsDestroyed) continue;
                if (oven.IsOn())
                    TryApplySpeed(oven);
            }
        }

        private void Unload()
        {
            // Restore the default cook interval on all running ovens.
            foreach (var oven in UnityEngine.Object.FindObjectsOfType<BaseOven>())
            {
                if (oven == null || oven.IsDestroyed) continue;
                if (oven.IsOn())
                    RestoreDefaultSpeed(oven);
            }
        }

        // Called when any oven is toggled on or off by a player.
        private void OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (oven == null) return;

            // Use NextTick so the oven's own StartCooking/StopCooking runs first.
            NextTick(() =>
            {
                if (oven == null || oven.IsDestroyed) return;
                if (oven.IsOn())
                    TryApplySpeed(oven);
            });
        }

        // Called when any network entity is spawned (e.g., player deploys a furnace).
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var oven = entity as BaseOven;
            if (oven == null) return;

            // Wait a tick to let the entity finish initialising.
            NextTick(() =>
            {
                if (oven == null || oven.IsDestroyed) return;
                if (oven.IsOn())
                    TryApplySpeed(oven);
            });
        }

        #endregion

        #region Chat Commands

        [ChatCommand("furnacespeed")]
        private void CmdFurnaceSpeed(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, AdminPermission))
            {
                SendReply(player, Lang("NoPermission", player.UserIDString));
                return;
            }

            if (args.Length < 2)
            {
                SendReply(player, Lang("Usage", player.UserIDString));
                return;
            }

            OvenType ovenType;
            if (!TryParseOvenType(args[0], out ovenType))
            {
                SendReply(player, Lang("InvalidOvenType", player.UserIDString, args[0]));
                return;
            }

            float multiplier;
            if (!float.TryParse(args[1], out multiplier) || multiplier <= 0f)
            {
                SendReply(player, Lang("InvalidMultiplier", player.UserIDString));
                return;
            }

            GetOvenSettings(ovenType).SpeedMultiplier = multiplier;
            SaveConfig();

            // Re-apply the new speed to all running ovens of this type.
            foreach (var oven in UnityEngine.Object.FindObjectsOfType<BaseOven>())
            {
                if (oven == null || oven.IsDestroyed || !oven.IsOn()) continue;
                if (GetOvenType(oven) == ovenType)
                    ApplySpeed(oven, multiplier);
            }

            SendReply(player, Lang("SpeedSet", player.UserIDString, ovenType.ToString(), multiplier));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Determines the <see cref="OvenType"/> for a given <see cref="BaseOven"/> and, if the
        /// corresponding type is enabled in the config, re-schedules its Cook tick at the
        /// configured speed.
        /// </summary>
        private void TryApplySpeed(BaseOven oven)
        {
            OvenType? ovenType = GetOvenType(oven);
            if (ovenType == null) return;

            var settings = GetOvenSettings(ovenType.Value);
            if (settings == null || !settings.Enabled) return;

            ApplySpeed(oven, settings.SpeedMultiplier);
        }

        /// <summary>
        /// Re-schedules the oven's Cook invocation at <c>DefaultCookInterval / multiplier</c>
        /// seconds, effectively making it run <paramref name="multiplier"/> times faster than
        /// vanilla.
        /// </summary>
        private void ApplySpeed(BaseOven oven, float multiplier)
        {
            if (oven == null || oven.IsDestroyed) return;

            float interval = Mathf.Max(0.01f, DefaultCookInterval / multiplier);
            oven.CancelInvoke(oven.Cook);
            oven.InvokeRepeating(oven.Cook, interval, interval);
        }

        /// <summary>
        /// Restores the oven's Cook invocation to the vanilla interval.
        /// </summary>
        private void RestoreDefaultSpeed(BaseOven oven)
        {
            if (oven == null || oven.IsDestroyed) return;

            oven.CancelInvoke(oven.Cook);
            oven.InvokeRepeating(oven.Cook, DefaultCookInterval, DefaultCookInterval);
        }

        /// <summary>Returns the <see cref="OvenType"/> for <paramref name="oven"/>, or
        /// <c>null</c> if the oven type is unrecognised.</summary>
        private OvenType? GetOvenType(BaseOven oven)
        {
            var shortName = oven?.ShortPrefabName;
            if (string.IsNullOrEmpty(shortName)) return null;

            foreach (var kv in OvenPrefabFragments)
            {
                foreach (var fragment in kv.Value)
                {
                    if (shortName.Equals(fragment, StringComparison.OrdinalIgnoreCase))
                        return kv.Key;
                }
            }
            return null;
        }

        /// <summary>Returns the <see cref="OvenSettings"/> that corresponds to
        /// <paramref name="ovenType"/>.</summary>
        private OvenSettings GetOvenSettings(OvenType ovenType)
        {
            switch (ovenType)
            {
                case OvenType.SmallFurnace:    return _config.SmallFurnace;
                case OvenType.LargeFurnace:    return _config.LargeFurnace;
                case OvenType.OilRefinery:     return _config.OilRefinery;
                case OvenType.Campfire:        return _config.Campfire;
                case OvenType.BBQ:             return _config.BBQ;
                case OvenType.ElectricFurnace: return _config.ElectricFurnace;
                case OvenType.MixingTable:     return _config.MixingTable;
                default:                       return null;
            }
        }

        private bool TryParseOvenType(string input, out OvenType result)
        {
            return Enum.TryParse(input, true, out result);
        }

        #endregion

        #region Localisation

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"]    = "You do not have permission to use this command.",
                ["Usage"]           = "Usage: /furnacespeed <type> <multiplier>\n" +
                                      "Types: SmallFurnace, LargeFurnace, OilRefinery, Campfire, BBQ, ElectricFurnace, MixingTable\n" +
                                      "Example: /furnacespeed SmallFurnace 5",
                ["InvalidOvenType"] = "Unknown oven type: '{0}'. See /furnacespeed for valid types.",
                ["InvalidMultiplier"] = "Multiplier must be a positive number (e.g. 3, 2.5).",
                ["SpeedSet"]        = "{0} speed multiplier set to {1}×.",
            }, this);
        }

        private string Lang(string key, string userId, params object[] args)
            => string.Format(lang.GetMessage(key, this, userId), args);

        #endregion
    }
}
