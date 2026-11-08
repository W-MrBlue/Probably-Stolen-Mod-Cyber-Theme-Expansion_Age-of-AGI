using AgeOfAgi.Items.Logic.DataContent;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;

internal static class ElectronicsSalvageChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        var catalog = DropSlotCsvFixture.LoadSalvage(items, ElectronicsVisitRules.Load());
        CheckCatalogAndRolls(items, catalog);
        CheckSignWeights();
        var loaded = Program.PrepareLoadedMod(items);
        Func<StoreClient> ordinaryThief = StoreClientList.CreateThief;
        StoreClientListDict.storeClientDict.Add("thief", ordinaryThief);
        var mod = new AgeOfAgiMod(); mod.Init(loaded.Manifest); mod.OnEnable();
        ModHook.RaiseItemDirectoryInit(new ModItemDirectory());
        PlayerStore.Instance = new PlayerStore { isStolenItemIntroduced = true };
        EmporiumEntry.Instance = new EmporiumEntry(); StoreReputation.BlackMarket = 100;
        try
        {
            Assert(StoreClientListDict.storeClientDict["thief"] == ordinaryThief && ordinaryThief().identifier == "thief" &&
                StoreClientList.generalPool.entries.Count == 3,
                "Keep the ordinary native thief and all original random pools unchanged; register themed sellers for sign dispatch.");
            CheckClient(ElectronicsSalvageSellers.CreateScavenger, "scavenger", false);
            CheckClient(ElectronicsSalvageSellers.CreatePettyThief, "petty_thief", true);
            CheckClient(ElectronicsSalvageSellers.CreateThief, "thief", true);
            PlayerStore.Instance.isStolenItemIntroduced = false;
            var tutorial = ElectronicsSalvageSellers.CreatePettyThief();
            EmporiumEntry.Instance.frontInvinvElement.childItems.ToList().ForEach(item => item.Destroy());
            PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = tutorial };
            tutorial.mainDialogue.endAction();
            Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.All(item => !item.IsTag("STOLEN_TAG") &&
                PlayerStore.Instance.StolenRequests[item]), "Pass the native stolen flag without bypassing the game's stolen-item introduction guard.");

            foreach (var target in new[] { ("dc_eyeball", 40), ("dc_idcard", 20), ("dc_suspeciousdisk", 200) })
            {
                var item = DirectoryMaster.Item(ModConstants.ModId + "." + target.Item1);
                Assert(DataContentState.Capacity(item) == target.Item2 && DataContentState.Amount(item) == target.Item2,
                    "Apply confirmed raw-carrier capacity targets to newly generated items.");
                item.ModifyTag("AGE_OF_AGI_DATA_CAPACITY", tag => tag.SetInt(1000));
                item.ModifyTag(DataContentState.AmountTag, tag => tag.SetInt(900));
                item.onLoaded(item);
                Assert(DataContentState.Capacity(item) == 1000 && DataContentState.Amount(item) == 900,
                    "Do not truncate saved carrier capacity or contents when changing source definitions.");
            }

            var sign = DirectoryMaster.Item(ElectronicsSignHelper.ItemId);
            EmporiumEntry.Instance.showcaseElement.UncheckedAccept(sign);
            StoreReputation.LowerLevel = 0; StoreReputation.BlackMarket = 0;
            ElectronicsSignAttraction.ClearPrediction();
            var first = new List<(string, int, string)>(); ElectronicsSignAttraction.AddPrediction(first);
            StoreReputation.LowerLevel = -25; StoreReputation.BlackMarket = 100;
            var same = new List<(string, int, string)>(); ElectronicsSignAttraction.AddPrediction(same);
            Assert(first.Single() == same.Single(), "Reopening prediction UI or changing reputation cannot reroll a pending supplier.");
            ElectronicsSignAttraction.ClearPrediction();
            var next = new List<(string, int, string)>(); ElectronicsSignAttraction.AddPrediction(next);
            Assert(new[] { ElectronicsSalvageSellers.PettyThiefId, ElectronicsSalvageSellers.ThiefId }.Contains(next.Single().Item1) &&
                next.Single().Item2 == 75 && next.Single().Item3 == ElectronicsSignHelper.ItemId,
                "After the native cache clear, use current faction weights and keep one 75% sign candidate.");
            foreach (string id in new[] { ElectronicsSalvageSellers.ScavengerId, ElectronicsSalvageSellers.PettyThiefId, ElectronicsSalvageSellers.ThiefId })
                Assert(ElectronicsMarket.QueueDebug(id), "Every new seller must have a working F9 factory.");
            mod.OnDisable();
            Assert(StoreClientListDict.storeClientDict.Count == 1 && StoreClientListDict.storeClientDict["thief"] == ordinaryThief &&
                PlayerStore.Instance.storeClientManager.clientStack.Count == 0, "Cleanup removes themed sellers and retains the native thief factory.");
        }
        finally
        {
            mod.OnDisable(); StoreClientListDict.storeClientDict.Remove("thief");
            PlayerStore.Instance = null; StoreReputation.LowerLevel = 0; StoreReputation.BlackMarket = 0;
        }
        Console.WriteLine("PASS: electronic scavenger/petty-thief/thief templates, count-first uniform slot draws, configurable reputation gating, weighted neon attraction, native stolen guards and saved data.");
    }

    private static void CheckClient(Func<StoreClient> create, string template, bool stolen)
    {
        var stock = EmporiumEntry.Instance.frontInvinvElement;
        foreach (var item in stock.childItems.ToList()) item.Destroy();
        StoreClient client = create();
        Assert(client.NativeTemplate == template && client.clientIntent == StoreClient.ClientIntent.SELL &&
            client.sourceModId == ModConstants.ModId, "Use the native seller factory with a separate mod identity.");
        if (template == "scavenger")
            Assert(client.tag.Contains("SCAV") && client.clientFaction == "FACTION_LOWER_LEVEL" && client.isOfferWholesale &&
                client.wholesaleDiscount == -15, "Retain native scavenger faction, portrait class and wholesale behavior.");
        else
            Assert(client.clientFaction == "FACTION_BLACK_MARKET" && client.cooldown == 2 &&
                (template != "petty_thief" || client.isOfferWholesale && client.wholesaleDiscount == -10),
                "Retain native thief cooldowns and the petty thief's discount.");
        PlayerStore.Instance.currentClientInstance = null; client.mainDialogue.endAction();
        Assert(stock.childItems.Count == 0, "Stale dialogue callbacks must not deliver another client's stock.");
        PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
        client.mainDialogue.endAction();
        int maximum = template == "scavenger" ? 8 : template == "petty_thief" ? 5 : 6;
        Assert(stock.childItems.Count <= maximum && stock.childItems.All(item =>
            !GeneralHelper.IsItemOwned(item) && PlayerStore.Instance.StolenRequests[item] == stolen && item.IsTag("STOLEN_TAG") == stolen),
            "Native delivery offers paid goods with the correct source theft flag.");
        var frozen = stock.childItems.Select(item => (item.identifier, DataContentState.IsCarrier(item) ? DataContentState.Amount(item) : 0)).ToArray();
        foreach (var item in stock.childItems) item.onLoaded?.Invoke(item);
        client.mainDialogue.endAction();
        Assert(stock.childItems.Select(item => (item.identifier, DataContentState.IsCarrier(item) ? DataContentState.Amount(item) : 0)).SequenceEqual(frozen) &&
            client.Introductions == 1, "Item load and repeated dialogue do not reroll contents, repeat stock or reopen wholesale introduction.");
    }

    private static void CheckCatalogAndRolls(IReadOnlyList<ItemDefinition> items, ElectronicsSalvageCatalog catalog)
    {
        foreach (var kind in new[] { ElectronicsSalvageKind.Scavenger, ElectronicsSalvageKind.PettyThief, ElectronicsSalvageKind.Thief })
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var offers = catalog.Roll(seed, kind, 100);
                var rule = catalog.GetRule(kind);
                Assert(offers.Count >= rule.MinimumCount && offers.Count <= rule.MaximumCount,
                    "Choose attempt count before slot and item draws.");
                Assert(offers.Where(offer => offer.State == "raw").All(offer => offer.Fraction >= 0.5 && offer.Fraction <= 1),
                    "Raw-carrier fill remains in the truncated interval.");
                var repeat = catalog.Roll(seed, kind, 100);
                Assert(offers.Select(offer => (offer.Id, offer.State, offer.Fraction)).SequenceEqual(
                    repeat.Select(offer => (offer.Id, offer.State, offer.Fraction))),
                    "A seed preserves identities and frozen contents.");
            }
        }
        var single = DropSlotCsvFixture.ParseSalvage(items,
            DropSlotCsvFixture.Salvage("age_of_agi.liquid_cooler"), ElectronicsVisitRules.Load());
        for (int seed = 0; seed < 50; seed++)
            Assert(single.Roll(seed, ElectronicsSalvageKind.Thief, -100).All(offer => offer.Id == "age_of_agi.liquid_cooler"),
                "Standard slots remain available even with negative reputation.");
    }

    private static void CheckSignWeights()
    {
        var counts = new Dictionary<string, int>();
        for (int seed = 0; seed < 20000; seed++)
        {
            string id = ElectronicsSignAttraction.PickSupplier(new Random(seed), 0, 25, false);
            counts[id] = counts.TryGetValue(id, out int count) ? count + 1 : 1;
        }
        foreach (var sample in new[] { (ElectronicsMarket.SellerId, 0.140625), (ElectronicsMarket.DishonestSellerId, 0.046875),
            (ElectronicsSalvageSellers.ScavengerId, 0.140625), (ElectronicsSalvageSellers.DishonestScavengerId, 0.046875),
            (ElectronicsMarket.GpuSupplierId, 0.125), (ElectronicsSalvageSellers.PettyThiefId, 0.25), (ElectronicsSalvageSellers.ThiefId, 0.25) })
            Assert(counts.TryGetValue(sample.Item1, out int count) && Math.Abs(count / 20000.0 - sample.Item2) < 0.02,
                "The GPU supplier receives one quarter of lower-level picks while preserving the other branch ratios.");
        bool advancedSeen = false;
        for (int seed = 0; seed < 1000; seed++)
        {
            string lower = ElectronicsSignAttraction.PickSupplier(new Random(seed), 0, 0, true);
            Assert(new[] { ElectronicsMarket.SellerId, ElectronicsMarket.DishonestSellerId, ElectronicsMarket.GpuSupplierId, ElectronicsSalvageSellers.DishonestScavengerId, ElectronicsMarket.AdvancedSellerId, ElectronicsSalvageSellers.ScavengerId }.Contains(lower),
                "Zero black-market weight excludes thieves from lamp attraction.");
            advancedSeen |= lower == ElectronicsMarket.AdvancedSellerId;
            string black = ElectronicsSignAttraction.PickSupplier(new Random(seed), -25, 100, true);
            Assert(new[] { ElectronicsSalvageSellers.PettyThiefId, ElectronicsSalvageSellers.ThiefId }.Contains(black),
                "Zero lower-level weight selects only the two black-market branches.");
        }
        Assert(advancedSeen && ElectronicsSignAttraction.PickSupplier(new Random(1), int.MinValue, int.MinValue, false) != null &&
            ElectronicsSignAttraction.PickSupplier(new Random(1), int.MaxValue, int.MaxValue, true) != null,
            "Preserve the advanced dealer branch and handle extreme reputation without overflow or empty picks.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
