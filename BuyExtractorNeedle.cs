using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace BuyExtractorNeedle
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class BuyExtractorNeedle : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.BuyExtractorNeedle";
        public const string pluginName = "Buy Dvergr Extractor Needle";
        public const string pluginVersion = "1.0.2";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        internal static BuyExtractorNeedle instance;

        internal static ConfigEntry<bool> configLocked;
        internal static ConfigEntry<string> requirements;

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            Game.isModded = true;
        }

        public void ConfigInit()
        {
            config("General", "NexusID", 2850, "Nexus mod ID for updates", false);

            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            requirements = config("General", "Required items", defaultValue: "Coins:4000", "Items required to buy extractor");

            requirements.SettingChanged += (sender, args) => BuyAndDestroy.UpdateDescription();
        }

        private void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        public static class ZNetScene_Awake_PatchCrateLong
        {
            private static void Postfix(ZNetScene __instance)
            {
                GameObject prefab = __instance.GetPrefab("dvergrprops_crate_long");
                if (prefab == null)
                    return;

                if (prefab.GetComponent<BuyAndDestroy>())
                    return;

                HoverText hoverText = prefab.GetComponent<HoverText>();
                if (hoverText == null)
                    return;

                BuyAndDestroy component = prefab.AddComponent<BuyAndDestroy>();
                component.m_text = hoverText.m_text;

                UnityEngine.Object.Destroy(hoverText);
            }
        }
    }
}
