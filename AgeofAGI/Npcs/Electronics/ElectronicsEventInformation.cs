using HarmonyLib;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsEventInformation
    {
        internal static bool TryApply(StoreClient client)
        {
            if (client == null) return false;
            ElectronicsEvents.EventSpec[] available = ElectronicsEvents.GetAvailableInformation();
            if (available.Length == 0 || !RNG.Roll(20)) return false;
            ElectronicsEvents.EventSpec selected = available[RNG.GetRandomInt(0, available.Length - 1)];
            StoreEvent storeEvent = ElectronicsEvents.CreateEvent(selected);
            int daysUntil = RNG.GetRandomInt(2, 3);
            Dialogue tail = ClientDialogueText.SetText(client.acceptDealDialogue, client.displayName,
                ModHelper.GetLocalized(ModConstants.ModId, selected.TextPrefix + "_information",
                    daysUntil, storeEvent.duration, storeEvent.newsDescription));
            // CompleteClientCreation binds native OnDealAccepted to this final node.
            client.acceptDealDialogueLastLine = tail;
            tail.SetEndAction(() =>
            {
                if (!ElectronicsEvents.Ready || !PlayerStore.HasInstance) return;
                StoreStation.Instance.storeEventManager.QueueFuturEvent(storeEvent.CreateAndAddSlip(daysUntil),
                    daysUntil, IsKnown: true);
            });
            return true;
        }
    }

    [HarmonyPatch(typeof(StoreClientListInformation), nameof(StoreClientListInformation.HandleInformationDialog))]
    internal static class ElectronicsInformationSelectionPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(StoreClient storeClient) => !ElectronicsEventInformation.TryApply(storeClient);
    }

    [HarmonyPatch(typeof(StoreClientListInformation), nameof(StoreClientListInformation.HasAvailableInformation))]
    internal static class ElectronicsInformationAvailabilityPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(ref bool __result)
        {
            if (!__result) __result = ElectronicsEvents.GetAvailableInformation().Length > 0;
        }
    }
}
