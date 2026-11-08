using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace AgeOfAgi.Npcs
{
    internal static class NightShiftClient
    {
        internal const string ClientId = ModConstants.ModId + ".night_shift";
        internal const string UpgradeId = ModConstants.ModId + ".night_shift_service";
        internal const string SpriteId = ModConstants.ModId + ".night_shift_portrait";
        internal const string StateKey = "night_shift_state";
        internal const int UnlockCost = 1200;
        // Native default tab is the regular-content page; subtitle selects its category card.
        internal const string TabId = "";
        internal const string DebugActionName = "召唤夜班（调试供货）";
        internal const string ClaimActionName = "领取AI服务物品（补领）";
        private static readonly WeeklyElectronicsSupplier supplier = new WeeklyElectronicsSupplier(
            ClientId, UpgradeId, SpriteId, StateKey, "night_shift", "night_shifter");
        private static ModLog log;
        private static bool enabled;

        internal static void Enable(ModLog modLog)
        {
            log = modLog;
            enabled = true;
            ModHook.OnGenerateCustomerEarly -= Generate;
            ModHook.OnGenerateCustomerEarly += Generate;
            ModHook.OnGameLoadedLate -= RunReady;
            ModHook.OnGameLoadedLate += RunReady;
            ModHook.OnNewGameStarted -= NewGame;
            ModHook.OnNewGameStarted += NewGame;
            ModHook.OnShutterOpenedLate -= DeliverGifts;
            ModHook.OnShutterOpenedLate += DeliverGifts;
        }

        internal static void Configure(NightShiftCatalog value) => supplier.Configure(value, log);
        internal static void ConfigureEntry(NightShiftCatalog value) => ElectronicsTrader.Supplier.Configure(value, log);
        internal static void Unlock() => supplier.Unlock();
        private static void NewGame(string _) => RunReady();
        private static void RunReady()
        {
            if (!EnsureServiceRegistered()) return;
            RestoreLegacyRights();
            ElectronicsTrader.Supplier.Unlock();
            supplier.Unlock();
            Generate(PlayerStore.Instance.storeClientManager);
        }

        internal static bool EnsureServiceRegistered()
        {
            // RegisterModUpgrade reads a lazy singleton. Never call it before scene setup.
            if (!enabled || !PlayerStore.HasInstance || PlayerStore.Instance.networkUpgrade == null) return false;
            Register(ElectronicsTrader.UpgradeId, ElectronicsTrader.UnlockCost, "electronics_trader", "E_service", ElectronicsTrader.Supplier.Unlock, null, true);
            Register(UpgradeId, UnlockCost, "night_shift", "E_service_advanced", Unlock, new[] { ElectronicsTrader.UpgradeId }, false);
            return true;
        }

        internal static void RestoreLegacyRights()
        {
            if (!enabled || !PlayerStore.HasInstance || PlayerStore.Instance.networkUpgrade == null) return;
            if (StoreStation.Instance != null && ModHelper.IsNetworkUpgradeUnlocked(UpgradeId) &&
                !ModHelper.IsNetworkUpgradeUnlocked(ElectronicsTrader.UpgradeId))
            {
                // Legacy Night Shift already included entry equipment. Preserve frozen gifts.
                if (string.IsNullOrEmpty(ModHelper.GetModData(ModConstants.ModId, ElectronicsTrader.StateKey)))
                    ModHelper.SetModData(ModConstants.ModId, ElectronicsTrader.StateKey, JsonConvert.SerializeObject(
                        new NightShiftState { Gifts = new List<NightShiftOffer>(), NextVisitDay = StoreStation.Instance.dayCounter + 4 }));
                PlayerStore.Instance.networkUpgrade[ElectronicsTrader.UpgradeId].state = 2;
            }
        }

        private static void Register(string id, int cost, string prefix, string icon, System.Action unlock, string[] prerequisites, bool recommended)
        {
            if (NetworkUpgrade.GetDefinition(id) != null && PlayerStore.Instance.networkUpgrade.ContainsKey(id)) return;
            ModHelper.AddNetworkUpgrade(ModConstants.ModId, id, cost, "AgeOfAgi.Electronics",
                () => Text(prefix + "_service_title"), () => Text(prefix + "_service_description"), unlock,
                tab: TabId, iconKey: ModConstants.ModId + ":" + icon, prerequisites: prerequisites,
                subtitleLabel: () => Text("electronics_industry"), alreadyBought: () => Text(prefix + "_service_owned"),
                recommended: recommended);
            log?.Log("王尔德电子产业服务已注册：" + id + "；基础价格 " + cost + "。");
        }

        internal static void ClaimGifts()
        {
            ElectronicsTrader.Supplier.ClaimGifts();
            supplier.ClaimGifts();
        }
        internal static void DeliverGifts() { ElectronicsTrader.Supplier.DeliverGifts(); supplier.DeliverGifts(); }
        internal static void Generate(StoreClientManager manager) { ElectronicsTrader.Supplier.Generate(manager); supplier.Generate(manager); }
        internal static bool QueueDebug() => supplier.QueueDebug();
        internal static StoreClient Create(int visitDay, IReadOnlyList<NightShiftOffer> offers, bool debug = false) => supplier.Create(visitDay, offers, debug);
        internal static bool TryGetPortrait(out Sprite sprite) => supplier.TryGetPortrait(0, out sprite);

        internal static void Shutdown()
        {
            enabled = false;
            ModHook.OnGenerateCustomerEarly -= Generate;
            ModHook.OnGameLoadedLate -= RunReady;
            ModHook.OnNewGameStarted -= NewGame;
            ModHook.OnShutterOpenedLate -= DeliverGifts;
            ModHelper.RemoveNetworkUpgrades(ModConstants.ModId);
            supplier.Shutdown(); ElectronicsTrader.Supplier.Shutdown();
        }
        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    [HarmonyPatch(typeof(SpriteDict), nameof(SpriteDict.GetSprite))]
    internal static class NightShiftPortraitPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string key, ref Sprite __result)
        {
            if (key == NightShiftClient.SpriteId && NightShiftClient.TryGetPortrait(out var sprite))
            { __result = sprite; return false; }
            for (int i = 0; i < ElectronicsTrader.PortraitCount; i++)
                if (key == ElectronicsTrader.Supplier.PortraitKey(i) && ElectronicsTrader.Supplier.TryGetPortrait(i, out sprite))
                { __result = sprite; return false; }
            return true;
        }
    }

    [HarmonyPatch(typeof(WildNetworkUI), nameof(WildNetworkUI.Open))]
    internal static class NightShiftWildOpenPatch
    {
        [HarmonyPrefix]
        internal static void Prefix()
        {
            if (NightShiftClient.EnsureServiceRegistered()) NightShiftClient.RestoreLegacyRights();
        }
    }
}
