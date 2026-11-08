using AgeOfAgi.Items.Logic.DataContent;
using AgeOfAgi.Items.Logic.AiModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Catalogs;
using AgeOfAgi.Items.Logic.Gpu;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;
using Newtonsoft.Json;
using UnityEngine;

internal static class ElectronicsIndustryChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        CheckCatalog(items);
        var loaded = Program.PrepareLoadedMod(items);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        foreach (string locale in new[] { "en", "zh" }) ModHelper.Translations.Add(locale, Program.ReadLocale(locale));
        var native = new ModItemDirectory();
        native.Add("energy_credit", () =>
        {
            var battery = new GameItem { Id = "energy_credit" };
            battery.EnableTag("power_source_item"); battery.ModifyTag("power_source_item_max_energy", tag => tag.Enable().SetInt(100)); return battery;
        });
        PlayerStore.Instance = new PlayerStore(); EmporiumEntry.Instance = new EmporiumEntry();
        StoreStation.Instance.dayCounter = 10;
        SpriteDict.Instance = new SpriteDict();
        SpriteDict.Instance.spriteDictionary.Add("native", new Sprite { pixelsPerUnit = 80 });
        ModHelper.Locale = "zh";
        try
        {
            // Make ordinary cards deterministically faulty. Gift creation must
            // ignore these weights, while weekly sale cards must still use them.
            var positions = CsvFixture.Parse(ModHelper.ReadModFileText(ModConstants.ModId, GpuInspectionCatalog.CatalogPath));
            foreach (var slot in positions.Rows.Where(slot => slot["slot"] != "board" && slot["slot"] != "core"))
            {
                slot["normal_weight"] = "0"; slot["lowfrequency_weight"] = "0";
                slot["mismatch_weight"] = "0"; slot["empty_weight"] = "1";
            }
            try
            {
                ModHelper.FileOverrides[GpuInspectionCatalog.CatalogPath] = positions.ToString();
                ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
            }
            finally { ModHelper.FileOverrides.Remove(GpuInspectionCatalog.CatalogPath); }
            NightShiftWildOpenPatch.Prefix();
            var entryService = ModHelper.Services[ElectronicsTrader.UpgradeId];
            Assert(entryService.Cost == ElectronicsTrader.UnlockCost && entryService.Prerequisites == null &&
                entryService.Title() == "电子产业 I" && entryService.Definition.tab == "" &&
                entryService.Definition.subtitle == "AgeOfAgi.Electronics" && ModHelper.TabLabels.Count == 0,
                "Entry industry uses a category card on the native regular page without registering a tab label.");
            ElectronicsTrader.Supplier.Unlock();
            Assert(State() == null, "Calling the unlock callback cannot grant an unpurchased service.");
            foreach (var inventory in Inventories()) inventory.RejectTransfer = true;
            PlayerStore.Instance.networkUpgrade[ElectronicsTrader.UpgradeId].state = 2;
            entryService.Unlock();
            Assert(State().GiftIndex == 0, "Full stock retains all entry gifts.");
            EmporiumEntry.Instance.backInvinvElement.RejectTransfer = false;
            ElectronicsTrader.Supplier.ClaimGifts();
            var gifts = EmporiumEntry.Instance.GetAllItems();
            Assert(gifts.Count == 7 && State().GiftIndex == 7 && gifts.All(GeneralHelper.IsItemOwned),
                "Entry package contains seven top-level gifts.");
            GameItem giftGpu = gifts.Single(item => item.identifier == "age_of_agi.gpu0");
            var gpuDefinition = GpuCatalog.Load().Get(giftGpu.identifier);
            Assert(GpuInspectionState.Read(giftGpu).Parts.All(part => part.Condition == "normal" && part.Sprite.Length > 0) &&
                GpuRuntime.TryGetRealPerformance(giftGpu, out var performance) && performance.PowerSupplyWorking &&
                performance.ComputePower == gpuDefinition.ComputePower && performance.PowerPerCycle == gpuDefinition.PowerPerCycle,
                "Deferred entry GPU gifts use intact standard assemblies even when ordinary spawn weights guarantee faults.");
            foreach (var label in new[] { (GpuLabels.ComputeFeature, (double)gpuDefinition.ComputePower),
                (GpuLabels.PowerFeature, (double)gpuDefinition.PowerPerCycle), (GpuLabels.SupplyFeature, 1d), (GpuLabels.GoldfingerFeature, 1d) })
            {
                var feature = giftGpu.FindItemFeatureByID(label.Item1);
                Assert(GpuLabels.ReadValue(feature.realCondition, -1) == label.Item2 &&
                    GpuLabels.ReadValue(feature.fakeCondition, -1) == label.Item2,
                    "Gift labels must start from standard hardware rather than a discarded random assembly.");
            }
            string gpuSnapshot = giftGpu.GetTagReadonly(GpuInspectionState.StateTag).GetString();
            giftGpu.onLoaded(giftGpu);
            Assert(giftGpu.GetTagReadonly(GpuInspectionState.StateTag).GetString() == gpuSnapshot,
                "Loading a standard gift retains its saved assembly and real performance.");
            StoreClient sale = ElectronicsTrader.Supplier.Create(10,
                new[] { new NightShiftOffer(giftGpu.identifier, -1) }, debug: true);
            PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = sale };
            EmporiumEntry.Instance.frontInvinvElement.RejectTransfer = false;
            sale.mainDialogue.endAction();
            GameItem saleGpu = EmporiumEntry.Instance.frontInvinvElement.childItems.Single();
            Assert(GpuRuntime.TryGetRealPerformance(saleGpu, out var salePerformance) && !salePerformance.PowerSupplyWorking &&
                GpuInspectionState.Read(saleGpu).Parts.Skip(2).All(part => part.Condition == "empty") &&
                saleGpu.itemFeatures.Where(GpuLabels.IsFeature).All(feature => feature.IsFeatureMatch()) &&
                GpuLabels.ReadValue(saleGpu.FindItemFeatureByID(GpuLabels.GoldfingerFeature).fakeCondition, -1) == 0,
                "Weekly sale GPUs retain random defects and disclose all four labels honestly, including missing contacts.");
            EmporiumEntry.Instance.frontInvinvElement.childItems.Clear(); saleGpu.Destroy();
            EmporiumEntry.Instance.frontInvinvElement.RejectTransfer = true;
            PlayerStore.Instance.currentClientInstance = null;
            var server = gifts.Single(item => item.identifier == "age_of_agi.serverbox");
            var row = (MachineryPanelRow)server.contentWindow.childElement;
            var battery = ((GameSlotInventory)row.GetStorage(0)).childItem;
            var container = ((GameSlotInventory)row.GetStorage(8)).childItem;
            Assert(PowerHelper.GetAvailableEnergyFromItem(battery) == 100 &&
                DataContentState.Amount(container) == 60 && GeneralHelper.IsItemOwned(container),
                "The starter server owns its installed battery and partially filled data container.");
            var models = gifts.Where(AiModelState.IsTrained).ToArray();
            Assert(models.Length == 3 && models.All(AiModelState.CanProduce) &&
                models.All(model => AiModelState.GetTaskType(model) == "computing" && AiModelState.GetMemoryGb(model) <= 8) &&
                models.Select(AiModelState.GetCurrentDurability).SequenceEqual(new float[] { 3, 4, 5 }),
                "Three starter models are immediately usable on 8GB and retain different low durability.");
            PowerHelper.TryRemoveEnergy(battery, 7);
            ModHook.RaiseLoaded(); ModHook.RaiseOpened(); entryService.Unlock();
            Assert(EmporiumEntry.Instance.GetAllItems().Count == 7 && PowerHelper.GetAvailableEnergyFromItem(battery) == 93,
                "Reloading and repeated unlock callbacks neither duplicate nor recharge entry gifts.");
            Assert(ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey) == null,
                "Entry purchase does not grant the second-tier gift.");
            PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId].state = 2;
            ModHelper.Services[NightShiftClient.UpgradeId].Unlock();
            var second = JsonConvert.DeserializeObject<NightShiftState>(ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey));
            Assert(second.NextVisitDay == 14 && State().NextVisitDay == 11, "Simultaneous purchases stagger weekly supplier visits.");
            Assert(!second.Gifts.Any(offer => new[] { "age_of_agi.cpu0", "age_of_agi.gpu0", "age_of_agi.mem0", "age_of_agi.serverbox" }.Contains(offer.ItemId)),
                "Second-tier gifts focus on manufacturing rather than duplicating entry hardware.");
            StoreStation.Instance.dayCounter = 11;
            ModHook.RaiseGenerate(PlayerStore.Instance.storeClientManager);
            var client = PlayerStore.Instance.storeClientManager.clientStack.Single(c => c.identifier == ElectronicsTrader.ClientId);
            string frozen = JsonConvert.SerializeObject(State().PendingOffers), spriteKey = client.spriteName;
            PlayerStore.Instance.storeClientManager.clientStack.Clear();
            mod.OnDisable(); mod.OnEnable(); ModHook.RaiseItemDirectoryInit(new ModItemDirectory()); ModHook.RaiseLoaded();
            client = PlayerStore.Instance.storeClientManager.clientStack.Single(c => c.identifier == ElectronicsTrader.ClientId);
            Assert(client.spriteName == spriteKey && JsonConvert.SerializeObject(State().PendingOffers) == frozen,
                "A pending visit keeps its portrait and complete model attributes across reload.");
            PlayerStore.Instance.storeClientManager.clientStack.Clear();
            PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
            EmporiumEntry.Instance.frontInvinvElement.RejectTransfer = false;
            client.mainDialogue.endAction();
            var stock = EmporiumEntry.Instance.frontInvinvElement.childItems;
            Assert(stock.Count(item => item.identifier == "age_of_agi.btc_normal_unfinish") == 3 &&
                stock.Any(item => item.identifier == "age_of_agi.cpu0") && stock.All(item => !GeneralHelper.IsItemOwned(item)),
                "Weekly entry goods remain paid stock, including the standard CPU and three BTC inputs.");
            var ready = stock.Single(AiModelState.IsTrained);
            var unfinished = stock.Single(AiModelState.IsUntrained);
            Assert(AiModelState.CanProduce(ready) && AiModelState.CanTrain(unfinished) &&
                AiModelState.GetPredictedComputeBonus(unfinished) > AiModelState.GetComputeBonus(ready) &&
                AiModelState.GetPredictedDurability(unfinished) > AiModelState.GetCurrentDurability(ready) &&
                AiModelState.GetTrainingData(unfinished) == 30 && ready.GetBaseValue() == 96 && unfinished.GetBaseValue() == 118,
                "Sale models have complete frozen attributes and nonzero CSV prices.");
            Assert(DataContentState.Amount(stock.Single(DataContentState.IsContainer)) == 60,
                "Entry trader sells a partial data container for training the better unfinished model.");
            int stockCount = stock.Count; client.mainDialogue.endAction();
            Assert(stock.Count == stockCount && State().NextVisitDay == 18, "A visit commits once and schedules seven days later.");
            CheckEntryTraining(server, gifts, unfinished, container);
            for (int i = 0; i < ElectronicsTrader.PortraitCount; i++)
            {
                Sprite portrait = null;
                Assert(!NightShiftPortraitPatch.Prefix(ElectronicsTrader.Supplier.PortraitKey(i), ref portrait) &&
                    portrait != null && portrait.pixelsPerUnit == 80, "Every uploaded portrait resolves at native scale.");
            }
            Assert(ElectronicsTrader.Supplier.QueueDebug(), "Entry debug visit is available.");
            string debugPortrait = PlayerStore.Instance.storeClientManager.clientStack[0].spriteName;
            Assert(Enumerable.Range(0, ElectronicsTrader.PortraitCount).Select(ElectronicsTrader.Supplier.PortraitKey).Contains(debugPortrait) &&
                State().NextVisitDay == 18, "Debug visits use a configured portrait without consuming the weekly schedule.");
            CheckLegacyMigration();
            mod.OnDisable();
            Assert(ModHelper.TabLabels.Count == 0, "The industry lifecycle leaves native tab labels untouched.");
        }
        finally
        {
            mod.OnDisable(); PlayerStore.Instance = null; EmporiumEntry.Instance = null; SpriteDict.Instance = null;
            native.Remove("energy_credit"); ModHelper.Translations.Clear(); ModHelper.Locale = "en";
        }
        Console.WriteLine("PASS: Two-tier electronics services, standard intact GPU gifts and labels, random weekly GPUs, installed contents, replenishable models, saved random portraits, staggered visits and legacy rights.");
    }

    private static void CheckLegacyMigration()
    {
        PlayerStore.Instance = new PlayerStore();
        var legacy = new NightShiftState
        {
            Version = 1, GiftIndex = 1, NextVisitDay = 30,
            Gifts = new List<NightShiftOffer> { new NightShiftOffer("age_of_agi.cpu0", -1), new NightShiftOffer("age_of_agi.serverbox", -1) },
            PendingVisitDay = 23,
            PendingOffers = new List<NightShiftOffer> { new NightShiftOffer("age_of_agi.btc_normal_unfinish", -1) }
        };
        string frozen = JsonConvert.SerializeObject(legacy);
        ModHelper.SetModData(ModConstants.ModId, NightShiftClient.StateKey, frozen);
        PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId] = new NetworkUpgrade { state = 2 };
        var purchased = PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId];
        foreach (var inventory in Inventories()) inventory.RejectTransfer = true;
        NightShiftClient.ClaimGifts();
        Assert(PlayerStore.Instance.networkUpgrade[ElectronicsTrader.UpgradeId].state == 1 &&
            ModHelper.GetModData(ModConstants.ModId, ElectronicsTrader.StateKey) == null,
            "The F9 claim action never grants an unpurchased prerequisite, even on a legacy save.");
        NightShiftWildOpenPatch.Prefix();
        Assert(PlayerStore.Instance.networkUpgrade[ElectronicsTrader.UpgradeId].state == 2 && State().Gifts.Count == 0 &&
            ReferenceEquals(purchased, PlayerStore.Instance.networkUpgrade[NightShiftClient.UpgradeId]) &&
            ModHelper.GetModData(ModConstants.ModId, NightShiftClient.StateKey) == frozen,
            "Legacy purchase receives its prerequisite without resetting pending stock, gifts or granting a duplicate entry package.");
    }

    private static void CheckCatalog(IReadOnlyList<ItemDefinition> items)
    {
        var catalog = NightShiftCatalog.Load(items, ElectronicsTrader.CatalogPath);
        var nightly = NightShiftCatalog.Load(items);
        for (int seed = 0; seed < 100; seed++)
        {
            var stock = catalog.Roll(seed, 0);
            Assert(stock.Count(offer => offer.ItemId == "age_of_agi.btc_normal_unfinish") == 3 &&
                !nightly.Roll(seed, 300).Any(offer => offer.ItemId == "age_of_agi.btc_normal_unfinish"),
                "Normal BTC has one weekly three-item source shared across the industry.");
        }
        string csv = File.ReadAllText(Path.Combine(ModHelper.Root, ElectronicsTrader.CatalogPath));
        var configurable = CsvFixture.Parse(csv);
        foreach (var row in configurable.Rows.Where(row => row["itemIds"] == AiModelState.TrainedId || row["itemIds"] == AiModelState.UntrainedId))
        {
            row["memoryGb"] = "32";
            row["durability"] = "250";
        }
        var customModels = NightShiftCatalog.Parse(configurable.ToString(), items, ElectronicsTrader.CatalogPath);
        Assert(customModels.GiftItems().Concat(customModels.Roll(19, 0)).Where(offer => offer.MemoryGb > 0)
            .All(offer => offer.MemoryGb == 32 && offer.Durability == 250),
            "Independent supplier examples retain configured model memory and durability without starter-hardware or balance caps.");
        foreach (var sample in new[] { ("memoryGb", "0"), ("memoryGb", "-1"), ("computeBonus", "0"), ("durability", "0"), ("trainingData", "0"), ("trainingWork", "0") })
        {
            var fixture = CsvFixture.Parse(csv);
            fixture.Rows.Single(row => row["itemIds"] == AiModelState.UntrainedId)[sample.Item1] = sample.Item2;
            try { NightShiftCatalog.Parse(fixture.ToString(), items, ElectronicsTrader.CatalogPath); }
            catch (InvalidOperationException) { continue; }
            throw new Exception("Invalid supplier model accepted: " + sample.Item1);
        }
    }

    private static void CheckEntryTraining(GameItem server, List<GameItem> gifts, GameItem unfinished, GameItem container)
    {
        var row = (MachineryPanelRow)server.contentWindow.childElement;
        foreach (var hardware in new[] { ("age_of_agi.cpu0", 1), ("age_of_agi.gpu0", 2), ("age_of_agi.mem0", 4) })
        {
            var inventory = (GameInventory)row.GetStorage(hardware.Item2);
            var item = gifts.Single(gift => gift.identifier == hardware.Item1);
            if (!GraphUtils.CanAccept(inventory, item))
                item.SetShape(new GridShapeBuilder(item.modifiedShape).SetOrientation(1));
            Assert(GraphUtils.TryAcceptAll(inventory, item) == 0, "Starter hardware fits the unexpanded server.");
        }
        GeneralHelper.SetItemOwned(unfinished);
        Assert(GraphUtils.TryAcceptAll((GameInventory)row.GetStorage(5), unfinished) == 0, "Purchased unfinished model fits the server.");
        int originalDay = StoreStation.Instance.dayCounter;
        for (int day = 50; day < 56; day++)
        {
            StoreStation.Instance.dayCounter = day;
            server.onCycleEndSlotItemFunc(server, null, null);
        }
        StoreStation.Instance.dayCounter = originalDay;
        var output = (GameGridInventory)row.GetStorage(7);
        var trained = output.childItems.Single();
        Assert(AiModelState.CanProduce(trained) && AiModelState.GetCurrentDurability(trained) == 7 &&
            AiModelState.GetComputeBonus(trained) == 40 && AiModelState.GetMemoryGb(trained) == 6 &&
            DataContentState.Amount(container) == 30,
            "Entry hardware trains the better weekly model without second-tier equipment and spends exactly its 30-data budget.");
    }

    private static NightShiftState State() => JsonConvert.DeserializeObject<NightShiftState>(
        ModHelper.GetModData(ModConstants.ModId, ElectronicsTrader.StateKey) ?? "null");
    private static IEnumerable<GameGridInventory> Inventories() => new[] { EmporiumEntry.Instance.backInvinvElement,
        EmporiumEntry.Instance.invElement, EmporiumEntry.Instance.showcaseElement, EmporiumEntry.Instance.frontInvinvElement };
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
