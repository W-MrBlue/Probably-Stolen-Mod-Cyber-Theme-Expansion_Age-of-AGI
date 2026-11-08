using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Items.Logic.Gpu;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgeOfAgi.Npcs
{
    internal static class ElectronicsMarket
    {
        internal const string ScavengerBuyerId = ModConstants.ModId + ".electronics_scavenger_buyer";
        internal const string RepairBuyerId = ModConstants.ModId + ".repair_shop_buyer";
        internal const string UpperBuyerId = ModConstants.ModId + ".upper_electronics_buyer";
        internal const string SecondhandBuyerId = ModConstants.ModId + ".secondhand_equipment_buyer";
        internal const string SellerId = ModConstants.ModId + ".electronics_seller";
        internal const string AdvancedSellerId = ModConstants.ModId + ".electronics_seller_advanced";
        internal const string DishonestSellerId = ModConstants.ModId + ".electronics_seller_dishonest";
        internal const string GpuSupplierId = ModConstants.ModId + ".gpu_supplier";
        internal const string DishonestSellerGpuId = ModConstants.ModId + ".gpu1";
        internal const string MainType = "electronics";
        internal const string HardwareType = "electronic_hardware";
        internal const string PartsType = "electronic_parts";
        internal const string CarrierType = "data_carrier";
        internal const string ContainerType = "data_container";
        internal const string EncryptedType = "encrypted_carrier";
        private static ElectronicsMarketCatalog catalog;
        private static bool enabled, ownsMajorType, ownsHardwareType;
        private static readonly Dictionary<string, Func<StoreClient>> factories = new Dictionary<string, Func<StoreClient>>
        {
            { ScavengerBuyerId, CreateScavengerBuyer }, { RepairBuyerId, CreateRepairBuyer },
            { UpperBuyerId, CreateUpperBuyer }, { SecondhandBuyerId, CreateSecondhandBuyer },
            { SellerId, CreateSeller }, { AdvancedSellerId, CreateAdvancedSeller },
            { DishonestSellerId, CreateDishonestSeller }, { GpuSupplierId, CreateGpuSupplier },
            { ElectronicsSalvageSellers.ScavengerId, ElectronicsSalvageSellers.CreateScavenger },
            { ElectronicsSalvageSellers.DishonestScavengerId, ElectronicsSalvageSellers.CreateDishonestScavenger },
            { ElectronicsSalvageSellers.PettyThiefId, ElectronicsSalvageSellers.CreatePettyThief },
            { ElectronicsSalvageSellers.ThiefId, ElectronicsSalvageSellers.CreateThief }
        };

        internal static void Enable() => enabled = true;

        internal static void Configure(ElectronicsMarketCatalog value, ElectronicsSalvageCatalog salvage)
        {
            catalog = value;
            ElectronicsSalvageSellers.Configure(salvage);
            if (!enabled) return;
            foreach (var factory in factories)
                if (StoreClientListDict.storeClientDict.TryGetValue(factory.Key, out var existing) && existing != factory.Value)
                    throw new InvalidOperationException("Client ID already registered: " + factory.Key);
            RemovePoolEntries();
            foreach (var factory in factories) StoreClientListDict.storeClientDict[factory.Key] = factory.Value;
            StoreClientList.generalPool.Add(CreateRandomSeller, 40).Add(CreateAdvancedSeller, 10, 20, 30)
                .Add(CreateGpuSupplier, 20);
            AddMajorType(MainType, ref ownsMajorType);
            AddMajorType(HardwareType, ref ownsHardwareType);
        }

        internal static bool Ready => enabled && catalog != null && ClientDialogueCatalog.Current != null &&
            PlayerStore.HasInstance && StoreStation.Instance != null;
        internal static StoreClient CreateScavengerBuyer() => Ready ?
            GeneralBuyer(ScavengerBuyerId, "electronics_scavenger_buyer_name", StoreClientListTier.CreateInjuredSpacer()) : null;
        internal static StoreClient CreateRepairBuyer() => Ready ?
            GeneralBuyer(RepairBuyerId, "repair_shop_buyer_name", StoreClientListTier.CreateClinicNurse()) : null;
        internal static StoreClient CreateUpperBuyer() => Ready ?
            GeneralBuyer(UpperBuyerId, "upper_electronics_buyer_name", StoreClientListTier.CreateHospitalAdmin()) : null;
        internal static StoreClient CreateSecondhandBuyer()
        {
            if (!Ready) return null;
            StoreClient client = HardwareBuyer(SecondhandBuyerId, "secondhand_equipment_buyer_name", StoreClientListTier.CreateStationEngineer());
            client.spriteName = ElectronicsPortraits.SecondhandBuyerSpriteId;
            return client;
        }
        internal static StoreClient CreateSeller() => Ready ? Seller(false) : null;
        internal static StoreClient CreateDishonestSeller() => Ready ? Seller(false, true) : null;
        internal static StoreClient CreateAdvancedSeller() => Ready && ModHelper.IsNetworkUpgradeUnlocked(NightShiftClient.UpgradeId) ? Seller(true) : null;
        internal static StoreClient CreateGpuSupplier() => Ready ? GpuSupplier.Create(catalog) : null;
        private static StoreClient CreateRandomSeller() => RNG.Roll(25) ? CreateDishonestSeller() : CreateSeller();

        private static StoreClient GeneralBuyer(string id, string nameKey, StoreClient client)
        {
            ConfigureBuyer(client, id, nameKey, AcceptsGeneral);
            client.onItemAddedToWeighted += ExposeInflatedGpuLabels;
            if (RNG.GetRandomInt(0, 2) == 0) client.SetVariety();
            return client;
        }

        private static StoreClient HardwareBuyer(string id, string nameKey, StoreClient client)
        {
            ConfigureBuyer(client, id, nameKey, AcceptsHardware);
            return client;
        }

        private static void ConfigureBuyer(StoreClient client, string id, string nameKey, Func<GameItem, bool> accepts)
        {
            client.identifier = id;
            client.displayName = Text(nameKey);
            client.clientIntent = StoreClient.ClientIntent.BUY;
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            client.willAcceptItem = item => Ready && accepts(item);
            ClientDialogueCatalog.ApplyBuyer(client, id);
            ModHelper.SetClientSource(client, ModConstants.ModId);
        }

        internal static bool AcceptsGeneral(GameItem item)
        {
            if (item == null || item.IsTag("NOT_FOR_RESALE")) return false;
            var types = item.GetGameItemType();
            if (types.Contains(ContainerType) || types.Contains("ai_models") || types.Contains("codeparts")) return false;
            return types.Contains(MainType) || types.Contains(PartsType) || types.Contains(HardwareType) ||
                types.Contains(CarrierType) || types.Contains(EncryptedType);
        }

        internal static bool AcceptsHardware(GameItem item) => item != null && !item.IsTag("NOT_FOR_RESALE") &&
            item.GetGameItemType().Contains(HardwareType);

        private static void ExposeInflatedGpuLabels(GameItem item)
        {
            if (!Ready || !GpuRuntime.IsGpu(item)) return;
            GpuLabels.Ensure(item);
            foreach (ItemFeature feature in item.itemFeatures.Where(GpuLabels.IsFeature))
            {
                if (feature.IsExposed() || feature.IsFeatureMatch()) continue;
                double actual = GpuLabels.ReadValue(feature.realCondition, 0);
                double claim = GpuLabels.ReadValue(feature.fakeCondition, actual);
                bool inflated = feature.identifier == GpuLabels.PowerFeature
                    ? claim < actual - 0.000001
                    : claim > actual + 0.000001;
                if (inflated) feature.ExposeFeature();
            }
        }

        private static StoreClient Seller(bool advanced, bool dishonest = false)
        {
            StoreClient client = StoreClientListTier.CreateStationEngineer();
            client.identifier = dishonest ? DishonestSellerId : advanced ? AdvancedSellerId : SellerId;
            client.displayName = Text(advanced ? "advanced_hardware_supplier_name" : "electronics_seller_name");
            client.clientIntent = StoreClient.ClientIntent.SELL;
            client.useClientBudget = false;
            client.isOfferWholesale = true;
            client.wholesaleDiscount = -RNG.GetRandomInt(15, 25);
            client.clientBuyingTagList.Clear();
            client.clientBuyingIdList.Clear();
            ModHelper.SetClientSource(client, ModConstants.ModId);
            List<MarketOffer> offers = catalog.Roll(Guid.NewGuid().GetHashCode(), advanced);
            if (dishonest) offers[0] = new MarketOffer { Id = DishonestSellerGpuId, State = "factory" };
            bool offered = false;
            ClientDialogueCatalog.Apply(client, client.identifier).SetEndAction(() =>
            {
                if (!Ready || offered || PlayerStore.Instance.currentClientInstance?.GetClientBlueprint() != client) return;
                offered = true;
                foreach (MarketOffer offer in offers) ElectronicsStock.TryDeliver(offer, dishonest: dishonest);
            });
            return client;
        }

        internal static string ShowcaseBuyer(int value, int day, int attractiveness)
        {
            if (value >= 350 && day >= 14) return UpperBuyerId;
            if (value >= 145 || attractiveness >= 400) return RepairBuyerId;
            return ScavengerBuyerId;
        }

        internal static bool TryShowcaseBuyer(string type, out string id)
        {
            id = null;
            if (!Ready) return false;
            if (type == HardwareType)
            {
                id = SecondhandBuyerId;
                return true;
            }
            if (type != MainType) return false;
            id = ShowcaseBuyer(ShowcaseHelper.GetTypeTotalValue(MainType), StoreStation.Instance.dayCounter,
                PlayerStore.Instance.GetCurrentStoreAttractiveness());
            return true;
        }

        internal static bool QueueDebug(string id)
        {
            if (!Ready || !factories.TryGetValue(id, out var factory)) return false;
            StoreClient client = factory();
            if (client == null) return false;
            PlayerStore.Instance.storeClientManager.AddNextClient(client);
            return true;
        }

        internal static void Shutdown()
        {
            enabled = false;
            RemovePoolEntries();
            foreach (var factory in factories)
                if (StoreClientListDict.storeClientDict.TryGetValue(factory.Key, out var current) && current == factory.Value)
                    StoreClientListDict.storeClientDict.Remove(factory.Key);
            RemoveMajorType(MainType, ref ownsMajorType);
            RemoveMajorType(HardwareType, ref ownsHardwareType);
            if (PlayerStore.HasInstance)
                PlayerStore.Instance.storeClientManager?.clientStack?.RemoveAll(client => client != null &&
                    client.sourceModId == ModConstants.ModId && factories.ContainsKey(client.identifier));
            catalog = null;
            ElectronicsSalvageSellers.Shutdown();
            ElectronicsPortraits.Shutdown();
        }

        private static void AddMajorType(string type, ref bool owned)
        {
            if (TypeHelper.MAJOR_TYPES_LIST.Contains(type)) return;
            TypeHelper.MAJOR_TYPES_LIST.Add(type);
            owned = true;
        }

        private static void RemoveMajorType(string type, ref bool owned)
        {
            if (owned) TypeHelper.MAJOR_TYPES_LIST.Remove(type);
            owned = false;
        }

        private static void RemovePoolEntries() => StoreClientList.generalPool.entries.RemoveAll(entry =>
            factories.Values.Contains(entry.create) || entry.create == CreateRandomSeller || entry.create == CreateGpuSupplier);
        private static string Text(string key) => ModHelper.GetLocalized(ModConstants.ModId, key);
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType))]
    internal static class ElectronicsShowcasePatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(string type, ref string __result)
        {
            if (!ElectronicsMarket.TryShowcaseBuyer(type, out string id)) return true;
            __result = id;
            return false;
        }
    }

    internal sealed class ElectronicsMarketCatalog
    {
        private readonly ElectronicsDropCatalog stock;
        private readonly ElectronicsVisitRules visitRules;

        private ElectronicsMarketCatalog(ElectronicsVisitRules rules, ElectronicsDropCatalog stock)
        {
            visitRules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.stock = stock ?? throw new ArgumentNullException(nameof(stock));
        }

        internal static ElectronicsMarketCatalog Create(IReadOnlyList<ItemDefinition> items, ElectronicsVisitRules rules,
            ElectronicsDropCatalog stock) => new ElectronicsMarketCatalog(rules, stock);

        internal ElectronicsVisitRule GetRule(bool advanced) => visitRules.Get(advanced ? "advanced_dealer" : "dealer");

        internal List<MarketOffer> Roll(int seed, bool advanced) =>
            stock.Roll(seed, advanced ? "advanced_dealer" : "dealer", GetRule(advanced));

        internal List<MarketOffer> RollGpuSupplier(int seed) => stock.Roll(seed, "gpu_supplier", visitRules.Get("gpu_supplier"));
    }
}
