using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AgeOfAgi.Items.Logic.ElectronicStorage;
using HarmonyLib;

namespace AgeOfAgi.Npcs
{
    internal static class JacksonCodeStorageStock
    {
        private static bool enabled;
        private static readonly List<(Dialogue Line, Action Original, Action Owned)> bindings =
            new List<(Dialogue, Action, Action)>();

        internal static void Enable()
        {
            enabled = true;
            ModHook.OnPlaceInventorInventoryItemLate -= Offer;
            ModHook.OnPlaceInventorInventoryItemLate += Offer;
        }

        private static bool Ready => enabled && PlayerStore.HasInstance && EmporiumEntry.Instance != null &&
            DirectoryMaster.GetFactory<GameItem>(ElectronicStorageRuntime.CodeContainerId) != null;

        // Native regular, visiting and special inventories all fire this hook.
        internal static void Offer(List<GameItem> ownedItems)
        {
            if (!Ready || !ModHelper.IsNetworkUpgradeUnlocked(NightShiftClient.UpgradeId) ||
                PlayerStore.Instance.FindAllItem().Any(item => item.identifier == ElectronicStorageRuntime.CodeContainerId) ||
                EmporiumEntry.Instance.frontInvinvElement.childItems.Any(item =>
                item.identifier == ElectronicStorageRuntime.CodeContainerId && !GeneralHelper.IsItemOwned(item))) return;
            PlayerStore.Instance.AddDirectSellingItemToTable(DirectoryMaster.Item(ElectronicStorageRuntime.CodeContainerId));
        }

        // Intro/module-intro/storage visits have separate native dialogue stock callbacks.
        internal static void BindVisit(StoreClient client)
        {
            if (!enabled || client?.mainDialogue == null) return;
            Dialogue tail = client.mainDialogue;
            while (tail.nextDialogue != null) tail = tail.nextDialogue;
            if (bindings.Any(binding => ReferenceEquals(binding.Line, tail))) return;
            Action original = tail.endAction;
            bool offered = false;
            Action owned = () =>
            {
                original?.Invoke();
                if (offered || !Ready || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                offered = true;
                Offer(null);
            };
            tail.endAction = owned;
            bindings.Add((tail, original, owned));
        }

        internal static void Shutdown()
        {
            enabled = false;
            ModHook.OnPlaceInventorInventoryItemLate -= Offer;
            foreach (var binding in bindings)
                if (binding.Line.endAction == binding.Owned) binding.Line.endAction = binding.Original;
            bindings.Clear();
        }
    }

    [HarmonyPatch]
    internal static class JacksonCodeStorageVisitPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StoreClientList), nameof(StoreClientList.CreateInventorStorage));
            yield return AccessTools.Method(typeof(StoreClientListStory), nameof(StoreClientListStory.CreateInventorIntro));
            yield return AccessTools.Method(typeof(StoreClientListStory), nameof(StoreClientListStory.CreateInventorModuleIntro));
        }

        [HarmonyPostfix]
        internal static void Postfix(StoreClient __result) => JacksonCodeStorageStock.BindVisit(__result);
    }
}
