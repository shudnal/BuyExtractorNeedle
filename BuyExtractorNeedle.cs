using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using ConditionalConfigSync;
using HarmonyLib;
using UnityEngine;

namespace BuyExtractorNeedle
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("_shudnal.ConditionalConfigSync", "1.0.5")]
    [BepInDependency(currencyPocketID, BepInDependency.DependencyFlags.SoftDependency)]
    public class BuyExtractorNeedle : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.BuyExtractorNeedle";
        public const string pluginName = "Buy Dvergr Extractor Needle";
        public const string pluginVersion = "1.1.2";

        internal const string currencyPocketID = "Azumatt.CurrencyPocket";
        internal static bool currencyPocketInstalled;

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID)
        {
            DisplayName = pluginName,
            CurrentVersion = pluginVersion,
            MinimumRequiredVersion = pluginVersion,
            ModRequired = true
        };

        public static BuyExtractorNeedle instance;

        public static ConfigEntry<bool> configLocked;
        public static ConfigEntry<string> requirements;
        public static ConfigEntry<string> targetPrefabs;
        public static ConfigEntry<bool> loggingEnabled;

        private static readonly Dictionary<string, string> managedPrefabTexts = new Dictionary<string, string>(StringComparer.Ordinal);

        private void Awake()
        {
            instance = this;
            currencyPocketInstalled = Chainloader.PluginInfos.ContainsKey(currencyPocketID);

            ConfigInit();
            configSync.AddLockingConfigEntry(configLocked);

            harmony.PatchAll();
            Game.isModded = true;
        }

        private void ConfigInit()
        {
            configLocked = BindConfig("General", "Lock Configuration", true, "Configuration is locked and can be changed by server admins only.");
            requirements = BindConfig("General", "Required items", "Coins:4000", "Items required to buy a configured crate. Use prefab names separated by commas, for example Coins:4000 or Coins:2000,BlackCore:1.");
            targetPrefabs = BindConfig("General", "Target prefabs", "dvergrprops_crate_long", "Prefab names that should become purchasable, separated by commas.");
            loggingEnabled = BindConfig("General", "Enable logging", defaultValue: false, "Enable logging for interaction events.", false);

            requirements.SettingChanged += delegate { BuyAndDestroy.UpdateDescription(); };
            targetPrefabs.SettingChanged += delegate { ApplyConfiguredPrefabs(ZNetScene.instance); };
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony.UnpatchSelf();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        public static void LogWarning(object data)
        {
            instance.Logger.LogWarning(data);
        }

        private ConfigEntry<T> BindConfig<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);
            configSync.AddConfigEntry(configEntry, ConfigSyncMode.Conditional, serverControlledByDefault: synchronizedSetting);
            return configEntry;
        }

        private ConfigEntry<T> BindConfig<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true)
        {
            return BindConfig(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);
        }

        private static IEnumerable<string> GetTargetPrefabNames()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            string configuredPrefabs = targetPrefabs != null ? targetPrefabs.Value : "dvergrprops_crate_long";
            foreach (string entry in configuredPrefabs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string prefabName = entry.Trim();
                if (!string.IsNullOrEmpty(prefabName))
                    names.Add(prefabName);
            }

            return names;
        }

        internal static void ApplyConfiguredPrefabs(ZNetScene scene)
        {
            if (scene == null)
                return;

            HashSet<string> configuredNames = new HashSet<string>(GetTargetPrefabNames(), StringComparer.Ordinal);
            foreach (string managedName in new List<string>(managedPrefabTexts.Keys))
            {
                if (configuredNames.Contains(managedName))
                    continue;

                GameObject managedPrefab = scene.GetPrefab(managedName);
                BuyAndDestroy managedComponent = managedPrefab != null ? managedPrefab.GetComponent<BuyAndDestroy>() : null;
                if (managedComponent != null)
                {
                    HoverText restoredHoverText = managedPrefab.GetComponent<HoverText>() ?? managedPrefab.AddComponent<HoverText>();
                    restoredHoverText.m_text = managedPrefabTexts[managedName];
                    UnityEngine.Object.Destroy(managedComponent);
                }

                managedPrefabTexts.Remove(managedName);
            }

            foreach (string prefabName in configuredNames)
            {
                GameObject prefab = scene.GetPrefab(prefabName);
                if (prefab == null)
                {
                    if (instance != null)
                        LogWarning("Target prefab was not found: " + prefabName);
                    continue;
                }

                BuyAndDestroy existingComponent = prefab.GetComponent<BuyAndDestroy>();
                if (existingComponent != null)
                {
                    managedPrefabTexts[prefabName] = existingComponent.m_text;
                    continue;
                }

                HoverText hoverText = prefab.GetComponent<HoverText>();
                if (hoverText == null)
                {
                    if (instance != null)
                        LogWarning("Target prefab does not have HoverText and cannot be made purchasable: " + prefabName);
                    continue;
                }

                managedPrefabTexts[prefabName] = hoverText.m_text;
                BuyAndDestroy component = prefab.AddComponent<BuyAndDestroy>();
                component.m_text = hoverText.m_text;
                UnityEngine.Object.Destroy(hoverText);
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static class ZNetSceneAwakePatch
        {
            private static void Postfix(ZNetScene __instance)
            {
                ApplyConfiguredPrefabs(__instance);
            }
        }
    }
}
