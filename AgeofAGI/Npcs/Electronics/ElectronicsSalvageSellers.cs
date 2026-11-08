using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsSalvageSellers
    {
        internal const string ScavengerId = ModConstants.ModId + ".electronics_scavenger";
        internal const string DishonestScavengerId = ModConstants.ModId + ".electronics_scavenger_dishonest";
        internal const string PettyThiefId = ModConstants.ModId + ".electronics_petty_thief";
        internal const string ThiefId = ModConstants.ModId + ".electronics_thief";
        private static ElectronicsSalvageCatalog catalog;

        internal static void Configure(ElectronicsSalvageCatalog value) => catalog = value;
        internal static void Shutdown() => catalog = null;
        internal static StoreClient CreateScavenger() => Create(ElectronicsSalvageKind.Scavenger);
        internal static StoreClient CreateDishonestScavenger() => Create(ElectronicsSalvageKind.Scavenger, true);
        internal static StoreClient CreatePettyThief() => Create(ElectronicsSalvageKind.PettyThief);
        internal static StoreClient CreateThief() => Create(ElectronicsSalvageKind.Thief);

        private static StoreClient Create(ElectronicsSalvageKind kind, bool dishonest = false)
        {
            if (!ElectronicsMarket.Ready || catalog == null) return null;
            StoreClient client;
            string textPrefix;
            switch (kind)
            {
                case ElectronicsSalvageKind.Scavenger:
                    client = StoreClientListSupplier.CreateScavGenericSeller("electronics", ScavengerId);
                    textPrefix = "electronics_scavenger";
                    break;
                case ElectronicsSalvageKind.PettyThief:
                    client = StoreClientList.CreatePettyThief();
                    client.identifier = PettyThiefId;
                    textPrefix = "electronics_petty_thief";
                    break;
                default:
                    client = StoreClientList.CreateThief();
                    client.identifier = ThiefId;
                    textPrefix = "electronics_thief";
                    break;
            }
            // Keep the native portrait, faction, cooldown, discounts and completed deal callbacks.
            if (dishonest) client.identifier = DishonestScavengerId;
            client.displayName = ModHelper.GetLocalized(ModConstants.ModId, textPrefix + "_name");
            ModHelper.SetClientSource(client, ModConstants.ModId);
            List<MarketOffer> offers = catalog.Roll(Guid.NewGuid().GetHashCode(), kind, StoreReputation.GetBMReputation());
            bool offered = false;
            ClientDialogueCatalog.Apply(client, client.identifier).SetEndAction(() =>
            {
                if (!ElectronicsMarket.Ready || catalog == null || offered ||
                    PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                offered = true;
                foreach (MarketOffer offer in offers)
                {
                    ElectronicsStock.TryDeliver(offer, kind != ElectronicsSalvageKind.Scavenger, dishonest);
                }
                client.OnIntroduced();
            });
            return client;
        }
    }

    internal enum ElectronicsSalvageKind { Scavenger, PettyThief, Thief }

    internal sealed class ElectronicsSalvageCatalog
    {
        private readonly ElectronicsDropCatalog stock;
        private readonly ElectronicsVisitRules visitRules;

        private ElectronicsSalvageCatalog(ElectronicsVisitRules rules, ElectronicsDropCatalog stock)
        {
            visitRules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.stock = stock ?? throw new ArgumentNullException(nameof(stock));
        }

        internal static ElectronicsSalvageCatalog Create(ElectronicsVisitRules rules, ElectronicsDropCatalog stock) =>
            new ElectronicsSalvageCatalog(rules, stock);

        internal ElectronicsVisitRule GetRule(ElectronicsSalvageKind kind) => visitRules.Get(
            kind == ElectronicsSalvageKind.Scavenger ? "scavenger" : kind == ElectronicsSalvageKind.PettyThief ? "petty_thief" : "thief");

        internal List<MarketOffer> Roll(int seed, ElectronicsSalvageKind kind, int blackMarketReputation) =>
            stock.Roll(seed, kind == ElectronicsSalvageKind.Scavenger ? "scavenger" :
                kind == ElectronicsSalvageKind.PettyThief ? "petty_thief" : "thief", GetRule(kind),
                kind == ElectronicsSalvageKind.Thief ? blackMarketReputation : 0);
    }
}
