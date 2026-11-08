using AgeOfAgi.Items.Logic.Gpu;
using System;
using System.Collections.Generic;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsEventClients
    {
        internal const string CryptoFanId = ModConstants.ModId + ".crypto_boom_fan";
        internal const string CryptoSecondhandId = ModConstants.ModId + ".crypto_boom_secondhand";
        internal const string CryptoUpperId = ModConstants.ModId + ".crypto_boom_upper";
        internal const string AstarFanId = ModConstants.ModId + ".astar_breakthrough_fan";
        internal const string AstarInvestorId = ModConstants.ModId + ".astar_breakthrough_investor";
        internal const string AstarDesperateId = ModConstants.ModId + ".astar_breakthrough_desperate";
        internal const string ElectronicsEnthusiastId = ModConstants.ModId + ".electronics_surge_enthusiast";
        internal const string ElectronicsRepairOwnerId = ModConstants.ModId + ".electronics_surge_repair_owner";
        internal const string ElectronicsHoarderId = ModConstants.ModId + ".electronics_surge_hoarder";
        internal const string CryptoDesperateSellerId = ModConstants.ModId + ".crypto_crash_desperate";
        internal const string CryptoAngrySellerId = ModConstants.ModId + ".crypto_crash_angry_upper";
        internal const string FearAngryId = ModConstants.ModId + ".ai_fear_angry_lower";
        internal const string FearDataMerchantId = ModConstants.ModId + ".ai_fear_data_merchant";
        internal const string FearResearcherId = ModConstants.ModId + ".ai_fear_astar_researcher";
        internal const string NormalUnfinishedBtcId = ModConstants.ModId + ".btc_normal_unfinish";
        private const float BuyerBudgetMultiplier = 1.75f;
        private static ElectronicsEventStockCatalog stock;

        internal static readonly string[] SellerIds =
        {
            CryptoDesperateSellerId, CryptoAngrySellerId, FearAngryId, FearDataMerchantId, FearResearcherId
        };

        internal static bool Ready => ElectronicsMarket.Ready && stock != null;
        internal static void Configure(ElectronicsEventStockCatalog catalog) => stock = catalog;
        internal static void Shutdown() => stock = null;

        internal static StoreClient CreateClient(string id)
        {
            if (!Ready) return null;
            switch (id)
            {
                case CryptoFanId: return Buyer(id, BudgetTier.Lower, AcceptsCrypto);
                case CryptoSecondhandId: return Buyer(id, BudgetTier.Middle, AcceptsCryptoAndGpu);
                case CryptoUpperId: return Buyer(id, BudgetTier.Upper, AcceptsCryptoAndGpu);
                case AstarFanId:
                case AstarDesperateId: return Buyer(id, BudgetTier.Lower, AcceptsAstarGoods);
                case AstarInvestorId: return Buyer(id, BudgetTier.Upper, AcceptsAstarGoods);
                case ElectronicsEnthusiastId: return Buyer(id, BudgetTier.Lower, AcceptsGeneralElectronics);
                case ElectronicsRepairOwnerId: return Buyer(id, BudgetTier.Middle, AcceptsHardwareAndParts);
                case ElectronicsHoarderId: return Buyer(id, BudgetTier.Upper, AcceptsGeneralElectronics);
                case CryptoDesperateSellerId: return Seller(id, BudgetTier.Lower, true);
                case CryptoAngrySellerId:
                case FearResearcherId: return Seller(id, BudgetTier.Upper, false);
                case FearAngryId:
                case FearDataMerchantId: return Seller(id, BudgetTier.Lower, false);
                default: return null;
            }
        }

        internal static bool AcceptsCrypto(GameItem item) => Resalable(item) &&
            item.GetGameItemType().Contains(ElectronicsEvents.CryptoType);

        internal static bool AcceptsCryptoAndGpu(GameItem item) => Resalable(item) &&
            (item.GetGameItemType().Contains(ElectronicsEvents.CryptoType) || GpuRuntime.IsGpu(item));

        internal static bool AcceptsAstarGoods(GameItem item) => ElectronicsMarket.AcceptsGeneral(item) || DataBuyers.Accepts(item);
        internal static bool AcceptsGeneralElectronics(GameItem item) => ElectronicsMarket.AcceptsGeneral(item);

        internal static bool AcceptsHardwareAndParts(GameItem item)
        {
            if (!Resalable(item)) return false;
            var types = item.GetGameItemType();
            return types.Contains(ElectronicsMarket.HardwareType) || types.Contains(ElectronicsMarket.PartsType);
        }

        private static StoreClient Buyer(string id, BudgetTier tier, Func<GameItem, bool> accepts)
        {
            StoreClient client = NativeClient(tier);
            ConfigureClient(client, id, StoreClient.ClientIntent.BUY);
            client.willAcceptItem = item => Ready && accepts(item);
            RaiseBudget(client);
            ClientDialogueCatalog.ApplyBuyer(client, id);
            return client;
        }

        private static StoreClient Seller(string id, BudgetTier tier, bool buysFoodAfterBtc)
        {
            StoreClient client = NativeClient(tier);
            ConfigureClient(client, id, buysFoodAfterBtc ? StoreClient.ClientIntent.SELLNBUY : StoreClient.ClientIntent.SELL);
            client.isOfferWholesale = true;
            client.wholesaleDiscount = -RNG.GetRandomInt(15, 25);
            if (buysFoodAfterBtc)
            {
                bool foodUnlocked = false;
                client.willAcceptItem = item => Ready && foodUnlocked && item.GetGameItemType().Contains("FOOD") &&
                    !item.IsTag("NOT_FOR_RESALE");
                client.onItemSold += (item, cost) =>
                {
                    if (item.identifier == NormalUnfinishedBtcId) foodUnlocked = true;
                };
                RaiseBudget(client);
            }
            List<MarketOffer> offers = stock.GetOffers(id);
            bool offered = false;
            ClientDialogueCatalog.Apply(client, id).SetEndAction(() =>
            {
                if (!Ready || offered || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                offered = true;
                foreach (MarketOffer offer in offers) ElectronicsStock.TryDeliver(offer, matchActualLabels: true);
            });
            return client;
        }

        private static void ConfigureClient(StoreClient client, string id, StoreClient.ClientIntent intent)
        {
            client.identifier = id;
            client.displayName = Text(id + "_name");
            client.clientIntent = intent;
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            client.willAcceptItem = null;
            ModHelper.SetClientSource(client, ModConstants.ModId);
        }

        private static StoreClient NativeClient(BudgetTier tier)
        {
            switch (tier)
            {
                case BudgetTier.Middle: return StoreClientListTier.CreateClinicNurse();
                case BudgetTier.Upper: return StoreClientListTier.CreateHospitalAdmin();
                default: return StoreClientListTier.CreateInjuredSpacer();
            }
        }

        private static void RaiseBudget(StoreClient client)
        {
            client.OverrideBudget(Math.Max(1, (int)Math.Round(client.GetBudget() * BuyerBudgetMultiplier,
                MidpointRounding.AwayFromZero)));
        }

        private static bool Resalable(GameItem item) => item != null && !item.IsTag("NOT_FOR_RESALE");
        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
        private enum BudgetTier { Lower, Middle, Upper }
    }
}
