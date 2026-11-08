using System;
using System.Collections.Generic;

namespace AgeOfAgi.Npcs
{
    internal static class GpuSupplier
    {
        internal static StoreClient Create(ElectronicsMarketCatalog catalog)
        {
            if (catalog == null) return null;
            StoreClient client = StoreClientListTier.CreateStationEngineer();
            client.identifier = ElectronicsMarket.GpuSupplierId;
            client.spriteName = ElectronicsPortraits.GpuSupplierSpriteId;
            client.displayName = Text("gpu_supplier_name");
            client.clientIntent = StoreClient.ClientIntent.SELL;
            client.useClientBudget = false;
            client.isOfferWholesale = true;
            client.wholesaleDiscount = -RNG.GetRandomInt(15, 25);
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            ModHelper.SetClientSource(client, ModConstants.ModId);
            List<MarketOffer> offers = catalog.RollGpuSupplier(Guid.NewGuid().GetHashCode());
            bool offered = false;
            ClientDialogueCatalog.Apply(client, client.identifier).SetEndAction(() =>
            {
                if (!ElectronicsMarket.Ready || offered ||
                    PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                offered = true;
                foreach (MarketOffer offer in offers)
                    ElectronicsStock.TryDeliver(offer, matchActualLabels: true);
            });
            return client;
        }

        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }
}
