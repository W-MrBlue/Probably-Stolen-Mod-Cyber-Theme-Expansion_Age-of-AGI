using AgeOfAgi.Items.Logic.DataContent;
using HarmonyLib;
using System;
using System.Collections.Generic;

namespace AgeOfAgi.Npcs
{
    internal static class DataBuyers
    {
        internal const string EnterpriseId = ModConstants.ModId + ".enterprise_computing_service";
        internal const string MainType = "data_container";
        private static bool enabled, configured, ownsMajorType;

        internal static void Enable() => enabled = true;
        internal static bool Ready => enabled && configured && ClientDialogueCatalog.Current != null &&
            PlayerStore.HasInstance && StoreStation.Instance != null;

        internal static void Configure()
        {
            if (!enabled) return;
            if (StoreClientListDict.storeClientDict.TryGetValue(EnterpriseId, out var existing) && existing != CreateEnterprise)
                throw new InvalidOperationException("Client ID already registered: " + EnterpriseId);
            StoreClientListDict.storeClientDict[EnterpriseId] = CreateEnterprise;
            if (!TypeHelper.MAJOR_TYPES_LIST.Contains(MainType))
            {
                TypeHelper.MAJOR_TYPES_LIST.Add(MainType);
                ownsMajorType = true;
            }
            configured = true;
        }

        internal static StoreClient CreateEnterprise()
        {
            if (!Ready) return null;
            StoreClient client = StoreClientListTier.CreateHospitalAdmin();
            client.identifier = EnterpriseId;
            client.displayName = Text("enterprise_computing_service_name");
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            client.willAcceptItem = item => Ready && Accepts(item);
            ClientDialogueCatalog.ApplyBuyer(client, EnterpriseId);
            ModHelper.SetClientSource(client, ModConstants.ModId);
            return client;
        }

        internal static bool Accepts(GameItem item) => DataContentState.IsContainer(item) &&
            DataContentState.Valid(item) && !item.IsTag("NOT_FOR_RESALE");

        internal static bool TryShowcaseBuyer(string type, out string id)
        {
            id = null;
            if (type != MainType || !Ready || EmporiumEntry.Instance?.showcaseElement == null) return false;
            foreach (GameItem item in EmporiumEntry.Instance.showcaseElement.childItems)
            {
                if (!Accepts(item) || !item.GetGameItemType().Contains(MainType)) continue;
                id = EnterpriseId;
                break;
            }
            return true;
        }

        internal static bool QueueDebug(string id)
        {
            if (!Ready || id != EnterpriseId) return false;
            StoreClient client = CreateEnterprise();
            if (client == null) return false;
            PlayerStore.Instance.storeClientManager.AddNextClient(client);
            return true;
        }

        internal static void Shutdown()
        {
            enabled = false;
            configured = false;
            if (StoreClientListDict.storeClientDict.TryGetValue(EnterpriseId, out var current) && current == CreateEnterprise)
                StoreClientListDict.storeClientDict.Remove(EnterpriseId);
            if (ownsMajorType) TypeHelper.MAJOR_TYPES_LIST.Remove(MainType);
            ownsMajorType = false;
            if (PlayerStore.HasInstance)
                PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(client => client != null &&
                    client.sourceModId == ModConstants.ModId && client.identifier == EnterpriseId);
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType))]
    internal static class DataShowcasePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string type, ref string __result)
        {
            if (!DataBuyers.TryShowcaseBuyer(type, out string id)) return true;
            __result = id;
            return false;
        }
    }
}
