using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items.Helpers;
using HarmonyLib;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsSignAttraction
    {
        private static bool enabled, pendingAdvancedUnlocked;
        private static string pendingSeller;

        internal static void Enable()
        {
            enabled = true;
            ModHook.OnPlaceInventorInventoryItemLate -= OfferSign;
            ModHook.OnPlaceInventorInventoryItemLate += OfferSign;
        }

        internal static void OfferSign(List<GameItem> ownedItems)
        {
            if (!enabled || !ElectronicsMarket.Ready || EmporiumEntry.Instance == null || ownedItems == null) return;
            // The native hook also runs for the special inventory. Avoid duplicate unpurchased signs on the counter.
            if (EmporiumEntry.Instance.frontInvinvElement.childItems.Any(item => item.identifier == ElectronicsSignHelper.ItemId)) return;
            StoreClientList.AddIfNotOwn(ElectronicsSignHelper.ItemId,
                new HashSet<string>(ownedItems.Select(item => item.identifier)));
        }

        internal static void AddPrediction(List<(string, int, string)> predictions)
        {
            if (!enabled || !ElectronicsMarket.Ready || EmporiumEntry.Instance == null || predictions == null ||
                !ShowcaseHelper.IsShowcaseContainItemById(ElectronicsSignHelper.ItemId) ||
                predictions.Any(prediction => prediction.Item3 == ElectronicsSignHelper.ItemId)) return;
            bool advancedUnlocked = ModHelper.IsNetworkUpgradeUnlocked(NightShiftClient.UpgradeId);
            // Match native PickOnce: UI prediction and the actual daily roll use the same supplier pick.
            if (pendingSeller == null || pendingAdvancedUnlocked != advancedUnlocked)
            {
                pendingAdvancedUnlocked = advancedUnlocked;
                pendingSeller = PickSupplier(new Random(Guid.NewGuid().GetHashCode()), StoreReputation.GetLLReputation(),
                    StoreReputation.GetBMReputation(), advancedUnlocked);
            }
            predictions.Add((pendingSeller, 75, ElectronicsSignHelper.ItemId));
        }

        internal static void ClearPrediction() => pendingSeller = null;

        internal static string PickSupplier(Random random, int lowerLevelReputation, int blackMarketReputation, bool advancedUnlocked)
        {
            // Native category signs use max(0, LL reputation + 25) versus max(0, BM reputation).
            long lowerWeight = Math.Max(0L, (long)lowerLevelReputation + 25);
            long blackWeight = Math.Max(0L, (long)blackMarketReputation);
            bool lower = lowerWeight + blackWeight == 0 || random.NextDouble() * (lowerWeight + blackWeight) < lowerWeight;
            if (!lower)
                return random.Next(2) == 0 ? ElectronicsSalvageSellers.PettyThiefId : ElectronicsSalvageSellers.ThiefId;
            if (random.Next(4) == 0) return ElectronicsMarket.GpuSupplierId;
            if (random.Next(2) == 0)
                return random.Next(100) < 25 ? ElectronicsSalvageSellers.DishonestScavengerId : ElectronicsSalvageSellers.ScavengerId;
            if (advancedUnlocked && random.Next(100) < 35) return ElectronicsMarket.AdvancedSellerId;
            return random.Next(100) < 25 ? ElectronicsMarket.DishonestSellerId : ElectronicsMarket.SellerId;
        }

        internal static void Shutdown()
        {
            enabled = false;
            ModHook.OnPlaceInventorInventoryItemLate -= OfferSign;
            ClearPrediction();
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCasePrediction))]
    internal static class ElectronicsSignPredictionPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(List<(string, int, string)> __result) => ElectronicsSignAttraction.AddPrediction(__result);
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.ClearPendingSupplierPicks))]
    internal static class ElectronicsSignPredictionClearPatch
    {
        [HarmonyPostfix]
        internal static void Postfix() => ElectronicsSignAttraction.ClearPrediction();
    }
}
