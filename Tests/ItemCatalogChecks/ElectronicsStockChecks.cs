using AgeOfAgi.Items.Logic.DataContent;
using System;
using System.Collections.Generic;
using System.Linq;
using AgeOfAgi;
using AgeOfAgi.Items;
using AgeOfAgi.Items.Helpers;
using AgeOfAgi.Npcs;

internal static class ElectronicsStockChecks
{
    internal static void Run(IReadOnlyList<ItemDefinition> items)
    {
        var rules = ElectronicsVisitRules.Parse(
            "seller,min_count,max_count\nscavenger,2,4\npetty_thief,1,3\nthief,2,5\ndealer,3,6\nadvanced_dealer,2,7\ngpu_supplier,4,4\n");
        CheckRuleValidation();
        ElectronicsDropSlotChecks.Run(items);
        CheckIndependentDraws(items, rules);
        var loaded = Program.PrepareLoadedMod(items);
        var registration = new ItemRegistration(loaded.Manifest);
        var directory = new ModItemDirectory(); registration.Register(directory, items);
        PlayerStore.Instance = new PlayerStore { isStolenItemIntroduced = true };
        EmporiumEntry.Instance = new EmporiumEntry();
        var stock = EmporiumEntry.Instance.frontInvinvElement;
        try
        {
            StoreStation.Instance.dayCounter = 1;
            var expensive = new MarketOffer { Id = "age_of_agi.gpu3", State = "factory" };
            Assert(ElectronicsStock.TryDeliver(expensive, true) && ElectronicsStock.TryDeliver(expensive, true) &&
                stock.childItems.Count == 2 && stock.childItems.All(item => item.IsTag("STOLEN_TAG")),
                "Opening-day expensive goods and repeated large purchases are not filtered by single-item or visit value.");
            Clear(stock);
            Assert(ElectronicsStock.TryDeliver(new MarketOffer { Id = "age_of_agi.data_container", State = "binary", Fraction = 1 }) &&
                DataContentState.Amount(stock.childItems.Single()) == DataContentState.Capacity(stock.childItems.Single()),
                "Delivery retains frozen content without a price filter.");
            Clear(stock);

            stock.SetShape(1, 1);
            Assert(!ElectronicsStock.TryDeliver(Offer("gpu0")) && stock.childItems.Count == 0,
                "Abandon a footprint that does not fit without replacing it.");
            Assert(ElectronicsStock.TryDeliver(Offer("cpu0")) && !ElectronicsStock.TryDeliver(Offer("cpu0")) &&
                stock.childItems.Count == 1, "A later small item can fit, then a full counter stops placement.");
            Clear(stock); stock.SetShape(3, 3);
            foreach (var position in new[] { (1, 0), (0, 1), (2, 1), (1, 2) })
            {
                var block = DirectoryMaster.Item("age_of_agi.cpu0");
                block.SetShape(new GridShapeBuilder(block.modifiedShape).SetPosition(position.Item1, position.Item2));
                stock.UncheckedAccept(block);
            }
            Assert(!ElectronicsStock.TryDeliver(Offer("disk_empty")) && ElectronicsStock.TryDeliver(Offer("cpu0")),
                "Use actual footprint placement in fragmented free cells.");
            Clear(stock); stock.SetShape(30, 30);

            stock.RejectTransfer = true; PlayerStore.Instance.StolenRequests.Clear();
            Assert(!ElectronicsStock.TryDeliver(Offer("cpu0"), true) &&
                PlayerStore.Instance.StolenRequests.Keys.Single().Destroyed,
                "Final native placement failure destroys the unplaced item.");
            stock.RejectTransfer = false;
            Assert(ElectronicsStock.TryDeliver(Offer("cpu0"), true), "A failed placement does not block later offers.");
            Clear(stock);
            CheckSellerCallbacks(items, rules, directory);
        }
        finally { Clear(stock); registration.Unregister(); PlayerStore.Instance = null; }
        Console.WriteLine("PASS: independent weighted slots, quantity rules, unrestricted values, one-time seller callbacks, real footprints and native failure cleanup.");
    }

    private static void CheckSellerCallbacks(IReadOnlyList<ItemDefinition> items, ElectronicsVisitRules rules, ModItemDirectory directory)
    {
        var market = DropSlotCsvFixture.ParseMarket(items,
            DropSlotCsvFixture.Market("age_of_agi.cpu0"), rules);
        var salvage = DropSlotCsvFixture.ParseSalvage(items,
            DropSlotCsvFixture.Salvage("age_of_agi.cpu0"), rules);
        var originalFactory = directory.Factories["age_of_agi.cpu0"];
        directory.Factories["age_of_agi.cpu0"] = () => { var item = originalFactory(); item.SetValue(50000); return item; };
        bool hadUpgrade = PlayerStore.Instance.UnlockedServices.Contains(NightShiftClient.UpgradeId);
        int previousRep = StoreReputation.BlackMarket;
        ClientDialogueCatalog.Configure();
        ElectronicsMarket.Enable(); ElectronicsMarket.Configure(market, salvage);
        PlayerStore.Instance.UnlockedServices.Add(NightShiftClient.UpgradeId);
        StoreReputation.BlackMarket = 0;
        try
        {
            foreach (var spec in new (Func<StoreClient> Create, string Rule)[]
            {
                (ElectronicsMarket.CreateSeller, "dealer"), (ElectronicsMarket.CreateAdvancedSeller, "advanced_dealer"),
                (ElectronicsSalvageSellers.CreateScavenger, "scavenger"),
                (ElectronicsSalvageSellers.CreatePettyThief, "petty_thief"), (ElectronicsSalvageSellers.CreateThief, "thief")
            })
            {
                Clear(EmporiumEntry.Instance.frontInvinvElement);
                var client = spec.Create();
                PlayerStore.Instance.currentClientInstance = null;
                client.mainDialogue.endAction();
                Assert(EmporiumEntry.Instance.frontInvinvElement.childItems.Count == 0, "Inactive sellers cannot deliver.");
                PlayerStore.Instance.currentClientInstance = new StoreClientInstance { Client = client };
                client.mainDialogue.endAction();
                var delivered = EmporiumEntry.Instance.frontInvinvElement.childItems.ToArray();
                var rule = rules.Get(spec.Rule);
                Assert(delivered.Length >= rule.MinimumCount && delivered.Length <= rule.MaximumCount &&
                    delivered.All(item => item.GetBaseValue() == 50000),
                    "All five sellers deliver expensive draws on day one without cumulative or per-item limits.");
                client.mainDialogue.endAction();
                Assert(delivered.SequenceEqual(EmporiumEntry.Instance.frontInvinvElement.childItems),
                    "A completed visit never delivers twice.");
            }
        }
        finally
        {
            Clear(EmporiumEntry.Instance.frontInvinvElement);
            directory.Factories["age_of_agi.cpu0"] = originalFactory;
            ElectronicsMarket.Shutdown(); PlayerStore.Instance.currentClientInstance = null;
            if (!hadUpgrade) PlayerStore.Instance.UnlockedServices.Remove(NightShiftClient.UpgradeId);
            StoreReputation.BlackMarket = previousRep;
        }
    }

    private static void CheckIndependentDraws(IReadOnlyList<ItemDefinition> items, ElectronicsVisitRules rules)
    {
        var weighted = DropSlotCsvFixture.ParseMarket(items,
            DropSlotCsvFixture.Header +
            "dealer,low,25,0,age_of_agi.disk_empty\n" +
            "dealer,medium,75,0,age_of_agi.net_switch\n" +
            "dealer,high,0,0,\n" +
            "advanced_dealer,low,25,0,age_of_agi.disk_empty\n" +
            "advanced_dealer,medium,75,0,age_of_agi.net_switch\n" +
            "advanced_dealer,high,0,0,\n", rules);
        int disks = 0, total = 0;
        for (int seed = 0; seed < 1000; seed++)
        {
            var offers = weighted.Roll(seed, false);
            Assert(offers.Count >= rules.Get("dealer").MinimumCount && offers.Count <= rules.Get("dealer").MaximumCount &&
                offers.Select(offer => offer.Id).Distinct().Count() <= 2,
                "Roll quantity first, then independently draw with replacement.");
            disks += offers.Count(offer => offer.Id == "age_of_agi.disk_empty"); total += offers.Count;
        }
        Assert(Math.Abs(disks / (double)total - 0.25) < 0.025, "Each draw uses the isolated 25/75 slot probabilities.");
    }

    private static void CheckRuleValidation()
    {
        const string csv = "seller,min_count,max_count\nscavenger,2,4\npetty_thief,1,3\nthief,2,5\ndealer,3,6\nadvanced_dealer,2,7\ngpu_supplier,4,4\n";
        foreach (string invalid in new[] { "", csv.Replace("scavenger,2,4", "scavenger,5,4"),
            csv.Replace("scavenger,2,4", "scavenger,0,4"), csv.Replace("scavenger,2,4", "scavenger,2,101"),
            csv.Replace("scavenger", "unknown"), csv + "scavenger,2,4\n",
            string.Join("\n", csv.Split('\n').Where(line => !line.StartsWith("thief,"))) })
        {
            bool rejected = false;
            try { ElectronicsVisitRules.Parse(invalid); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Reject invalid quantity ranges, unknown/duplicate sellers and missing rules.");
        }
    }

    private static MarketOffer Offer(string id) => new MarketOffer { Id = ModConstants.ModId + "." + id, State = "factory" };
    private static void Clear(GameGridInventory stock) { foreach (var item in stock.childItems.ToList()) item.Destroy(); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
